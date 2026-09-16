using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Identity;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.Infrastructure.Storage;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.BreedingFarms;

public sealed class BreedingFarmVisualIdentityEndpointTests
{
    private const string Route = "/api/breeding-farms/visual-identity";

    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/8ZkAAAAASUVORK5CYII=");

    [Fact]
    public async Task OwnerCanReadUploadReplaceAndRemovePrivateVisualIdentity()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "visual-identity-owner@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);

        using var initial = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        using (var initialBody = JsonDocument.Parse(await initial.Content.ReadAsStreamAsync()))
        {
            Assert.Equal(farmId, initialBody.RootElement.GetProperty("breedingFarmId").GetGuid());
            Assert.Equal(JsonValueKind.Null, initialBody.RootElement.GetProperty("identity").ValueKind);
        }

        using var firstUpload = await UploadIdentityAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "logo.png",
            "image/png",
            PngBytes);
        Assert.Equal(HttpStatusCode.OK, firstUpload.StatusCode);
        using var firstBody = JsonDocument.Parse(await firstUpload.Content.ReadAsStreamAsync());
        var firstIdentity = firstBody.RootElement.GetProperty("identity");
        Assert.Equal("Upload", firstIdentity.GetProperty("source").GetString());
        Assert.Equal("logo.png", firstIdentity.GetProperty("fileName").GetString());
        Assert.Equal("image/png", firstIdentity.GetProperty("contentType").GetString());
        var contentUrl = firstIdentity.GetProperty("contentUrl").GetString();
        Assert.Equal($"{Route}/content", contentUrl);

        var firstObjectKey = await GetVisualIdentityReferenceAsync(factory, farmId);
        var firstPath = GetPhysicalPath(storage.RootPath, farmId, firstObjectKey);
        Assert.True(File.Exists(firstPath));
        using var firstContent = await client.GetAsync(contentUrl);
        Assert.Equal(HttpStatusCode.OK, firstContent.StatusCode);
        Assert.Equal("no-store", firstContent.Headers.CacheControl?.NoStore == true ? "no-store" : null);
        Assert.Equal(PngBytes, await firstContent.Content.ReadAsByteArrayAsync());

        var jpeg = await new SpeciesDefaultImageReader().ReadAsync("0047.jpg", "image/jpeg");
        Assert.NotNull(jpeg);
        using var replacement = await UploadIdentityAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "replacement.jpg",
            "image/jpeg",
            jpeg.Content);
        Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);
        var secondObjectKey = await GetVisualIdentityReferenceAsync(factory, farmId);
        Assert.NotEqual(firstObjectKey, secondObjectKey);
        Assert.False(File.Exists(firstPath));
        Assert.True(File.Exists(GetPhysicalPath(storage.RootPath, farmId, secondObjectKey)));

        using var removeRequest = CreateBrowserRequest(
            HttpMethod.Delete,
            Route,
            await GetAntiforgeryTokenAsync(client));
        using var removed = await client.SendAsync(removeRequest);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using var removedBody = JsonDocument.Parse(await removed.Content.ReadAsStreamAsync());
        Assert.Equal(JsonValueKind.Null, removedBody.RootElement.GetProperty("identity").ValueKind);

        using var afterRemoval = await client.GetAsync(Route);
        using var afterRemovalBody = JsonDocument.Parse(await afterRemoval.Content.ReadAsStreamAsync());
        Assert.Equal(JsonValueKind.Null, afterRemovalBody.RootElement.GetProperty("identity").ValueKind);
        using var missingContent = await client.GetAsync($"{Route}/content");
        Assert.Equal(HttpStatusCode.NotFound, missingContent.StatusCode);
        Assert.False(File.Exists(GetPhysicalPath(storage.RootPath, farmId, secondObjectKey)));
    }

    [Fact]
    public async Task UploadRejectsInvalidFormatDimensionsAndSizeBeforePersistence()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "visual-identity-validation@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);

        using var extensionMismatch = await UploadIdentityAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "logo.jpg",
            "image/png",
            PngBytes);
        Assert.Equal(HttpStatusCode.BadRequest, extensionMismatch.StatusCode);

        using var invalidSignature = await UploadIdentityAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "not-an-image.png",
            "image/png",
            [1, 2, 3, 4]);
        Assert.Equal(HttpStatusCode.BadRequest, invalidSignature.StatusCode);

        var oversizedDimensions = PngBytes.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(oversizedDimensions.AsSpan(16, 4), 8193);
        using var invalidDimensions = await UploadIdentityAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "huge.png",
            "image/png",
            oversizedDimensions);
        Assert.Equal(HttpStatusCode.BadRequest, invalidDimensions.StatusCode);

        using var oversizedFile = await UploadIdentityAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "large.png",
            "image/png",
            new byte[BreedingFarmVisualIdentityUploadLimits.MaxFileLength + 1]);
        Assert.Equal(HttpStatusCode.BadRequest, oversizedFile.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var farm = await dbContext.BreedingFarms.SingleAsync(candidate => candidate.Id == farmId);
        Assert.Null(farm.VisualIdentityReference);
        Assert.False(Directory.Exists(Path.Combine(storage.RootPath, farmId.ToString("N"))));
    }

    [Fact]
    public async Task VisualIdentityRequiresAnAuthenticatedOwnerOfTheSelectedFarm()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);

        using var unauthenticated = await ownerClient.GetAsync(Route);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await RegisterAndAuthenticateAsync(factory, ownerClient, "visual-identity-access-owner@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);

        var otherUserId = await RegisterAndAuthenticateAsync(
            factory,
            otherClient,
            "visual-identity-access-other@example.com");
        using var withoutFarm = await otherClient.GetAsync(Route);
        Assert.Equal(HttpStatusCode.Conflict, withoutFarm.StatusCode);
        var otherFarmId = await CreateFarmAsync(otherClient);
        await SelectFarmAsync(otherClient, otherFarmId);

        using var ownerUpload = await UploadIdentityAsync(
            ownerClient,
            await GetAntiforgeryTokenAsync(ownerClient),
            "private-logo.png",
            "image/png",
            PngBytes);
        Assert.Equal(HttpStatusCode.OK, ownerUpload.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var otherUser = await dbContext.Users.SingleAsync(candidate => candidate.Id == otherUserId);
            otherUser.SelectedBreedingFarmId = ownerFarmId;
            dbContext.BreedingFarmUsers.Add(new BreedingFarmUser(
                ownerFarmId,
                otherUserId,
                BreedingFarmRole.Manager,
                DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync();
        }

        using var unauthorizedMetadata = await otherClient.GetAsync(Route);
        Assert.Equal(HttpStatusCode.NotFound, unauthorizedMetadata.StatusCode);
        using var unauthorizedContent = await otherClient.GetAsync($"{Route}/content");
        Assert.Equal(HttpStatusCode.NotFound, unauthorizedContent.StatusCode);
        using var unauthorizedUpload = await UploadIdentityAsync(
            otherClient,
            await GetAntiforgeryTokenAsync(otherClient),
            "foreign.png",
            "image/png",
            PngBytes);
        Assert.Equal(HttpStatusCode.NotFound, unauthorizedUpload.StatusCode);

        using var unauthorizedRemoveRequest = CreateBrowserRequest(
            HttpMethod.Delete,
            Route,
            await GetAntiforgeryTokenAsync(otherClient));
        using var unauthorizedRemove = await otherClient.SendAsync(unauthorizedRemoveRequest);
        Assert.Equal(HttpStatusCode.NotFound, unauthorizedRemove.StatusCode);

        using var ownerMetadata = await ownerClient.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, ownerMetadata.StatusCode);
        using var ownerBody = JsonDocument.Parse(await ownerMetadata.Content.ReadAsStreamAsync());
        Assert.Equal(
            "Upload",
            ownerBody.RootElement.GetProperty("identity").GetProperty("source").GetString());
        Assert.NotEqual(ownerFarmId, otherFarmId);
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
            builder.ConfigureServices(services =>
            {
                services.AddInfrastructurePersistence(connectionString, certificate);
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });

    private static async Task<HttpResponseMessage> UploadIdentityAsync(
        HttpClient client,
        string antiforgeryToken,
        string fileName,
        string contentType,
        byte[] bytes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, Route);
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName, antiforgeryToken);
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
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
            new { email, password = "StrongPassword!123", confirmPassword = "StrongPassword!123" }));
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
            new { name = "Sítio Aurora", responsibleName = "Owner Principal", contactEmail = "owner@example.com" }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("breedingFarmId").GetGuid();
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

    private static async Task<string> GetVisualIdentityReferenceAsync(
        WebApplicationFactory<Program> factory,
        Guid farmId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        return (await dbContext.BreedingFarms.SingleAsync(candidate => candidate.Id == farmId))
            .VisualIdentityReference!;
    }

    private static string GetPhysicalPath(string storageRootPath, Guid farmId, string objectKey) =>
        Path.Combine(storageRootPath, farmId.ToString("N"), objectKey.Replace('/', Path.DirectorySeparatorChar));

    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    private sealed class TemporaryStorage : IAsyncDisposable
    {
        public TemporaryStorage() =>
            RootPath = Path.Combine(Path.GetTempPath(), "CriatorioVirtualIdentityTests", Guid.NewGuid().ToString("N"));

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
