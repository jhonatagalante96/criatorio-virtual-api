using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Birds;

public sealed class BirdAttachmentEndpointTests
{
    [Fact]
    public async Task AttachmentEndpointsRequireAuthenticationAndASelectedBreedingFarm()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        using var unauthenticated = await client.GetAsync($"/api/birds/{Guid.NewGuid()}/attachments");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await RegisterAndAuthenticateAsync(factory, client, "attachments-no-farm@example.com");
        using var withoutFarm = await client.GetAsync($"/api/birds/{Guid.NewGuid()}/attachments");
        Assert.Equal(HttpStatusCode.Conflict, withoutFarm.StatusCode);
    }

    [Fact]
    public async Task UploadListAndDownloadRoundTripPersistsOnlyPrivateMetadata()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "attachments-roundtrip@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await AddBirdAsync(factory, farmId, speciesId, "Aurora");
        var bytes = new byte[] { 1, 2, 3, 4, 5 };

        using var upload = await UploadAsync(
            client,
            birdId,
            await GetAntiforgeryTokenAsync(client),
            "bird.jpg",
            "image/jpeg",
            bytes);

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        using var uploadBody = JsonDocument.Parse(await upload.Content.ReadAsStreamAsync());
        var attachmentId = uploadBody.RootElement.GetProperty("attachmentId").GetGuid();
        Assert.Equal(birdId, uploadBody.RootElement.GetProperty("birdId").GetGuid());
        Assert.Equal("bird.jpg", uploadBody.RootElement.GetProperty("fileName").GetString());
        Assert.Equal("image/jpeg", uploadBody.RootElement.GetProperty("contentType").GetString());
        Assert.Equal(bytes.Length, uploadBody.RootElement.GetProperty("length").GetInt64());
        Assert.Equal(
            $"/api/birds/{birdId}/attachments/{attachmentId}/content",
            uploadBody.RootElement.GetProperty("downloadUrl").GetString());

        using var listing = await client.GetAsync($"/api/birds/{birdId}/attachments");
        Assert.Equal(HttpStatusCode.OK, listing.StatusCode);
        using var listingBody = JsonDocument.Parse(await listing.Content.ReadAsStreamAsync());
        Assert.Equal(farmId, listingBody.RootElement.GetProperty("breedingFarmId").GetGuid());
        var item = Assert.Single(listingBody.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(attachmentId, item.GetProperty("attachmentId").GetGuid());
        Assert.Equal("bird.jpg", item.GetProperty("fileName").GetString());

        using var download = await client.GetAsync(
            $"/api/birds/{birdId}/attachments/{attachmentId}/content");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/jpeg", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var attachment = await dbContext.BirdAttachments.SingleAsync(candidate => candidate.Id == attachmentId);
        Assert.Equal(farmId, attachment.BreedingFarmId);
        Assert.Equal(birdId, attachment.BirdId);
        Assert.DoesNotContain(storage.RootPath, attachment.ObjectKey, StringComparison.OrdinalIgnoreCase);
        var physicalPath = Path.Combine(
            storage.RootPath,
            farmId.ToString("N"),
            attachment.ObjectKey.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(physicalPath));
    }

    [Fact]
    public async Task AttachmentAccessDoesNotCrossBreedingFarmBoundaries()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "attachments-boundary-owner@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);
        await RegisterAndAuthenticateAsync(factory, otherClient, "attachments-boundary-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient);
        await SelectFarmAsync(otherClient, otherFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var ownerBirdId = await AddBirdAsync(factory, ownerFarmId, speciesId, "Owner bird");

        using var upload = await UploadAsync(
            ownerClient,
            ownerBirdId,
            await GetAntiforgeryTokenAsync(ownerClient),
            "owner.pdf",
            "application/pdf",
            [9, 8, 7]);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        using var uploadBody = JsonDocument.Parse(await upload.Content.ReadAsStreamAsync());
        var attachmentId = uploadBody.RootElement.GetProperty("attachmentId").GetGuid();

        using var foreignList = await otherClient.GetAsync($"/api/birds/{ownerBirdId}/attachments");
        Assert.Equal(HttpStatusCode.NotFound, foreignList.StatusCode);
        using var foreignDownload = await otherClient.GetAsync(
            $"/api/birds/{ownerBirdId}/attachments/{attachmentId}/content");
        Assert.Equal(HttpStatusCode.NotFound, foreignDownload.StatusCode);

        using var foreignUpload = await UploadAsync(
            otherClient,
            ownerBirdId,
            await GetAntiforgeryTokenAsync(otherClient),
            "foreign.jpg",
            "image/jpeg",
            [1, 2, 3]);
        Assert.Equal(HttpStatusCode.NotFound, foreignUpload.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Equal(
            1,
            await dbContext.BirdAttachments.CountAsync(candidate => candidate.BreedingFarmId == ownerFarmId));
        Assert.Equal(
            0,
            await dbContext.BirdAttachments.CountAsync(candidate => candidate.BreedingFarmId == otherFarmId));
    }

    [Fact]
    public async Task UploadRejectsEmptyOversizedAndMismatchedFilesWithoutPersistence()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "attachments-validation@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await AddBirdAsync(factory, farmId, speciesId, "Validation bird");

        using var empty = await UploadAsync(
            client,
            birdId,
            await GetAntiforgeryTokenAsync(client),
            "empty.jpg",
            "image/jpeg",
            []);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        using var mismatch = await UploadAsync(
            client,
            birdId,
            await GetAntiforgeryTokenAsync(client),
            "bird.pdf",
            "image/jpeg",
            [1, 2, 3]);
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);

        using var oversized = await UploadAsync(
            client,
            birdId,
            await GetAntiforgeryTokenAsync(client),
            "large.jpg",
            "image/jpeg",
            new byte[10 * 1024 * 1024 + 1]);
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Empty(await dbContext.BirdAttachments.ToArrayAsync());
        Assert.False(Directory.Exists(Path.Combine(storage.RootPath, farmId.ToString("N"))));
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate,
        string storageRootPath) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:EventLog:LogLevel:Default"] = "None",
                ["Storage:PrivateRootPath"] = storageRootPath
            }));
            builder.ConfigureServices(services => services.AddInfrastructurePersistence(connectionString, certificate));
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        Guid birdId,
        string antiforgeryToken,
        string fileName,
        string contentType,
        byte[] bytes)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/birds/{birdId}/attachments");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add(
            HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName,
            antiforgeryToken);
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(content, "file", fileName);
        request.Content = form;
        return await client.SendAsync(request);
    }

    private static async Task<Guid> RegisterAndAuthenticateAsync(
        WebApplicationFactory<Program> factory,
        HttpClient client,
        string email)
    {
        using var registration = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/register",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                email,
                password = "StrongPassword!123",
                confirmPassword = "StrongPassword!123"
            }));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        Guid userId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var user = await dbContext.Users.SingleAsync(candidate => candidate.Email == email);
            user.EmailConfirmed = true;
            userId = user.Id;
            await dbContext.SaveChangesAsync();
        }

        using var login = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/login",
            await GetAntiforgeryTokenAsync(client),
            new { email, password = "StrongPassword!123" }));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return userId;
    }

    private static async Task<Guid> CreateFarmAsync(HttpClient client)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/breeding-farms",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                name = "Sítio Aurora",
                responsibleName = "Owner Principal",
                contactEmail = "owner@example.com"
            }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("breedingFarmId").GetGuid();
    }

    private static async Task SelectFarmAsync(HttpClient client, Guid farmId)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            "/api/breeding-farms/selection",
            await GetAntiforgeryTokenAsync(client),
            new { breedingFarmId = farmId }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<Guid> GetSpeciesIdAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        return await dbContext.Species
            .Where(species => species.ScientificName == "Turdus rufiventris")
            .Select(species => species.Id)
            .SingleAsync();
    }

    private static async Task<Guid> AddBirdAsync(
        WebApplicationFactory<Program> factory,
        Guid farmId,
        Guid speciesId,
        string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var now = DateTimeOffset.UtcNow;
        var bird = new Bird(
            Guid.NewGuid(),
            now,
            farmId,
            name,
            speciesId,
            BirdSex.Female,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateOnly.FromDateTime(now.UtcDateTime));
        dbContext.Birds.Add(bird);
        await dbContext.SaveChangesAsync();
        return bird.Id;
    }

    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/antiforgery/token");
        request.Headers.Add("Origin", "http://localhost:3000");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return response.Headers.GetValues(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName).Single();
    }

    private static HttpRequestMessage CreateBrowserRequest(
        HttpMethod method,
        string path,
        string antiforgeryToken,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName, antiforgeryToken);
        return request;
    }

    private sealed class TemporaryStorage : IAsyncDisposable
    {
        public TemporaryStorage() =>
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "CriatorioVirtualAttachmentTests",
                Guid.NewGuid().ToString("N"));

        public string RootPath { get; }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
