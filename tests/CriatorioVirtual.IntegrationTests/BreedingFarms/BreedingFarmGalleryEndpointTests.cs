using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.Infrastructure.Storage;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.BreedingFarms;

public sealed class BreedingFarmGalleryEndpointTests
{
    private const string Route = "/api/breeding-farms/gallery";

    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/8ZkAAAAASUVORK5CYII=");

    private static readonly byte[] Mp4Bytes =
    [0, 0, 0, 16, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D];

    [Fact]
    public async Task ExistingBirdMediaAppearsInGalleryWithoutCopyAndKeepsPrimaryPhotoReference()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "gallery-linked@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var birdId = await AddBirdAsync(factory, farmId, "Linked bird");

        using var upload = await UploadBirdAttachmentAsync(
            client,
            birdId,
            "bird.png",
            "image/png",
            PngBytes,
            "Already stored with the bird");
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        using var uploadBody = JsonDocument.Parse(await upload.Content.ReadAsStreamAsync());
        var mediaId = uploadBody.RootElement.GetProperty("attachmentId").GetGuid();

        using var documentUpload = await UploadBirdAttachmentAsync(
            client, birdId, "bird-record.pdf", "application/pdf", [1, 2, 3, 4]);
        Assert.Equal(HttpStatusCode.Created, documentUpload.StatusCode);

        using var primaryPhoto = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            $"/api/birds/{birdId}/primary-photo",
            await GetAntiforgeryTokenAsync(client),
            new { attachmentId = mediaId }));
        Assert.Equal(HttpStatusCode.OK, primaryPhoto.StatusCode);

        using var gallery = await client.GetAsync($"{Route}?type=image&birdId={birdId}");
        Assert.Equal(HttpStatusCode.OK, gallery.StatusCode);
        using var galleryBody = JsonDocument.Parse(await gallery.Content.ReadAsStreamAsync());
        Assert.Equal(1, galleryBody.RootElement.GetProperty("totalCount").GetInt32());
        var item = Assert.Single(galleryBody.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(mediaId, item.GetProperty("mediaId").GetGuid());
        Assert.Equal("Already stored with the bird", item.GetProperty("caption").GetString());
        Assert.True(item.GetProperty("isPrimary").GetBoolean());
        var bird = item.GetProperty("bird");
        Assert.Equal(birdId, bird.GetProperty("birdId").GetGuid());
        Assert.Equal("Linked bird", bird.GetProperty("name").GetString());

        using var galleryContent = await client.GetAsync(item.GetProperty("contentUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, galleryContent.StatusCode);
        Assert.True(galleryContent.Headers.CacheControl?.Private);
        Assert.True(galleryContent.Headers.CacheControl?.NoStore);
        Assert.Equal(PngBytes, await galleryContent.Content.ReadAsByteArrayAsync());

        using var existingBirdRoute = await client.GetAsync($"/api/birds/{birdId}/attachments");
        Assert.Equal(HttpStatusCode.OK, existingBirdRoute.StatusCode);
        using var existingBirdBody = JsonDocument.Parse(await existingBirdRoute.Content.ReadAsStreamAsync());
        Assert.Equal(2, existingBirdBody.RootElement.GetProperty("items").GetArrayLength());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var storedMedia = await dbContext.BirdAttachments.SingleAsync(candidate => candidate.Id == mediaId);
        Assert.Equal(birdId, storedMedia.BirdId);
        Assert.Equal(2, await dbContext.BirdAttachments.CountAsync(candidate => candidate.BreedingFarmId == farmId));
        Assert.True(File.Exists(GetPhysicalPath(storage.RootPath, farmId, storedMedia.ObjectKey)));

        var otherBirdId = await AddBirdAsync(factory, farmId, "Another bird");
        using var otherBirdUpload = await UploadBirdAttachmentAsync(
            client, otherBirdId, "another.png", "image/png", PngBytes);
        Assert.Equal(HttpStatusCode.Created, otherBirdUpload.StatusCode);
        using var otherBirdBody = JsonDocument.Parse(await otherBirdUpload.Content.ReadAsStreamAsync());
        var otherBirdMediaId = otherBirdBody.RootElement.GetProperty("attachmentId").GetGuid();
        var crossBirdPrimaryPhoto = await Assert.ThrowsAsync<PostgresException>(() =>
            dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE app.birds SET \"PrimaryPhotoId\" = {otherBirdMediaId} WHERE \"Id\" = {birdId} AND \"BreedingFarmId\" = {farmId}"));
        Assert.Equal("23503", crossBirdPrimaryPhoto.SqlState);
    }

    [Fact]
    public async Task GallerySupportsStandaloneImagesVideosFiltersPaginationAndCaptionDeletion()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "gallery-standalone@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);

        using var initialGallery = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, initialGallery.StatusCode);
        using var initialBody = JsonDocument.Parse(await initialGallery.Content.ReadAsStreamAsync());
        var limits = initialBody.RootElement.GetProperty("limits");
        Assert.Equal(10 * 1024 * 1024, limits.GetProperty("maxImageFileLength").GetInt64());
        Assert.Equal(100 * 1024 * 1024, limits.GetProperty("maxVideoFileLength").GetInt64());

        using var extensionMismatch = await UploadGalleryMediaAsync(
            client, "wrong-extension.png", "image/jpeg", [0xFF, 0xD8, 0xFF, 0xD9]);
        Assert.Equal(HttpStatusCode.BadRequest, extensionMismatch.StatusCode);
        using var oversizedImage = await UploadGalleryMediaAsync(
            client,
            "too-large.png",
            "image/png",
            new byte[10 * 1024 * 1024 + 1]);
        Assert.Equal(HttpStatusCode.BadRequest, oversizedImage.StatusCode);

        using var imageUpload = await UploadGalleryMediaAsync(
            client, "standalone.png", "image/png", PngBytes, "  Standalone image  ");
        Assert.Equal(HttpStatusCode.Created, imageUpload.StatusCode);
        using var imageBody = JsonDocument.Parse(await imageUpload.Content.ReadAsStreamAsync());
        var imageId = imageBody.RootElement.GetProperty("mediaId").GetGuid();
        Assert.Equal("Standalone image", imageBody.RootElement.GetProperty("caption").GetString());
        Assert.Equal(JsonValueKind.Null, imageBody.RootElement.GetProperty("bird").ValueKind);

        using var videoUpload = await UploadGalleryMediaAsync(
            client, "flock.mp4", "video/mp4", Mp4Bytes, "Flight");
        Assert.Equal(HttpStatusCode.Created, videoUpload.StatusCode);
        using var videoBody = JsonDocument.Parse(await videoUpload.Content.ReadAsStreamAsync());
        var videoId = videoBody.RootElement.GetProperty("mediaId").GetGuid();

        using var allMedia = await client.GetAsync($"{Route}?pageSize=1");
        Assert.Equal(HttpStatusCode.OK, allMedia.StatusCode);
        using var allMediaBody = JsonDocument.Parse(await allMedia.Content.ReadAsStreamAsync());
        Assert.Equal(2, allMediaBody.RootElement.GetProperty("totalCount").GetInt32());
        Assert.True(allMediaBody.RootElement.GetProperty("hasNextPage").GetBoolean());
        Assert.Equal(1, allMediaBody.RootElement.GetProperty("items").GetArrayLength());
        using var secondPage = await client.GetAsync($"{Route}?page=2&pageSize=1");
        using var secondPageBody = JsonDocument.Parse(await secondPage.Content.ReadAsStreamAsync());
        Assert.Equal(1, secondPageBody.RootElement.GetProperty("items").GetArrayLength());

        using var videoFilter = await client.GetAsync($"{Route}?type=video");
        using var videoFilterBody = JsonDocument.Parse(await videoFilter.Content.ReadAsStreamAsync());
        var listedVideo = Assert.Single(videoFilterBody.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(videoId, listedVideo.GetProperty("mediaId").GetGuid());
        Assert.Equal("video/mp4", listedVideo.GetProperty("contentType").GetString());
        using var videoContent = await client.GetAsync(listedVideo.GetProperty("contentUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, videoContent.StatusCode);
        Assert.Equal(Mp4Bytes, await videoContent.Content.ReadAsByteArrayAsync());

        using var imageFilter = await client.GetAsync($"{Route}?type=image");
        using var imageFilterBody = JsonDocument.Parse(await imageFilter.Content.ReadAsStreamAsync());
        var listedImage = Assert.Single(imageFilterBody.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(imageId, listedImage.GetProperty("mediaId").GetGuid());

        using var updated = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            $"{Route}/{imageId}",
            await GetAntiforgeryTokenAsync(client),
            new { caption = "Updated caption" }));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var updatedBody = JsonDocument.Parse(await updated.Content.ReadAsStreamAsync());
        Assert.Equal("Updated caption", updatedBody.RootElement.GetProperty("caption").GetString());

        using var removeImage = CreateBrowserRequest(
            HttpMethod.Delete,
            $"{Route}/{imageId}",
            await GetAntiforgeryTokenAsync(client));
        using var deleted = await client.SendAsync(removeImage);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var remaining = await client.GetAsync($"{Route}?type=image");
        using var remainingBody = JsonDocument.Parse(await remaining.Content.ReadAsStreamAsync());
        Assert.Empty(remainingBody.RootElement.GetProperty("items").EnumerateArray());

        using var invalidSignature = await UploadGalleryMediaAsync(
            client, "not-an-image.png", "image/png", [1, 2, 3, 4]);
        Assert.Equal(HttpStatusCode.BadRequest, invalidSignature.StatusCode);
        using var unsupportedType = await UploadGalleryMediaAsync(
            client, "notes.pdf", "application/pdf", [1, 2, 3, 4]);
        Assert.Equal(HttpStatusCode.BadRequest, unsupportedType.StatusCode);
        using var invalidFilter = await client.GetAsync($"{Route}?type=document");
        Assert.Equal(HttpStatusCode.BadRequest, invalidFilter.StatusCode);
    }

    [Fact]
    public async Task LinkedMediaRemainsTenantScopedAndCannotChangeDuringBirdTransfer()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "gallery-transfer-owner@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);
        var birdId = await AddBirdAsync(factory, ownerFarmId, "Transfer bird");

        using var linkedUpload = await UploadGalleryMediaAsync(
            ownerClient, "linked.png", "image/png", PngBytes, birdId: birdId);
        Assert.Equal(HttpStatusCode.Created, linkedUpload.StatusCode);
        using var linkedBody = JsonDocument.Parse(await linkedUpload.Content.ReadAsStreamAsync());
        var mediaId = linkedBody.RootElement.GetProperty("mediaId").GetGuid();
        Assert.Equal(birdId, linkedBody.RootElement.GetProperty("bird").GetProperty("birdId").GetGuid());

        await RegisterAndAuthenticateAsync(factory, otherClient, "gallery-transfer-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient);
        await SelectFarmAsync(otherClient, otherFarmId);
        using var otherTenantContent = await otherClient.GetAsync($"{Route}/{mediaId}/content");
        Assert.Equal(HttpStatusCode.NotFound, otherTenantContent.StatusCode);
        using var foreignBirdUpload = await UploadGalleryMediaAsync(
            otherClient, "foreign.png", "image/png", PngBytes, birdId: birdId);
        Assert.Equal(HttpStatusCode.NotFound, foreignBirdUpload.StatusCode);
        using var otherTenantCaption = await otherClient.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            $"{Route}/{mediaId}",
            await GetAntiforgeryTokenAsync(otherClient),
            new { caption = "Not authorized" }));
        Assert.Equal(HttpStatusCode.NotFound, otherTenantCaption.StatusCode);
        using var otherTenantDelete = CreateBrowserRequest(
            HttpMethod.Delete,
            $"{Route}/{mediaId}",
            await GetAntiforgeryTokenAsync(otherClient));
        using var foreignDeleteResponse = await otherClient.SendAsync(otherTenantDelete);
        Assert.Equal(HttpStatusCode.NotFound, foreignDeleteResponse.StatusCode);
        Assert.NotEqual(ownerFarmId, otherFarmId);

        await SetBirdTransferredAsync(factory, birdId);
        using var blockedCaption = await ownerClient.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            $"{Route}/{mediaId}",
            await GetAntiforgeryTokenAsync(ownerClient),
            new { caption = "Blocked" }));
        Assert.Equal(HttpStatusCode.Conflict, blockedCaption.StatusCode);
        using var blockedDelete = CreateBrowserRequest(
            HttpMethod.Delete,
            $"{Route}/{mediaId}",
            await GetAntiforgeryTokenAsync(ownerClient));
        using var blockedDeleteResponse = await ownerClient.SendAsync(blockedDelete);
        Assert.Equal(HttpStatusCode.Conflict, blockedDeleteResponse.StatusCode);
        using var blockedUpload = await UploadGalleryMediaAsync(
            ownerClient, "blocked.png", "image/png", PngBytes, birdId: birdId);
        Assert.Equal(HttpStatusCode.Conflict, blockedUpload.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Equal(1, await dbContext.BirdAttachments.CountAsync(candidate => candidate.BreedingFarmId == ownerFarmId));
        Assert.Single(Directory.EnumerateFiles(storage.RootPath, "*", SearchOption.AllDirectories));
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

    private static async Task<HttpResponseMessage> UploadGalleryMediaAsync(
        HttpClient client,
        string fileName,
        string contentType,
        byte[] bytes,
        string? caption = null,
        Guid? birdId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Route);
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName,
            await GetAntiforgeryTokenAsync(client));
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        if (caption is not null)
        {
            form.Add(new StringContent(caption), "caption");
        }

        if (birdId is { } linkedBirdId)
        {
            form.Add(new StringContent(linkedBirdId.ToString()), "birdId");
        }

        request.Content = form;
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> UploadBirdAttachmentAsync(
        HttpClient client,
        Guid birdId,
        string fileName,
        string contentType,
        byte[] bytes,
        string? caption = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/birds/{birdId}/attachments");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName,
            await GetAntiforgeryTokenAsync(client));
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        if (caption is not null)
        {
            form.Add(new StringContent(caption), "caption");
        }

        request.Content = form;
        return await client.SendAsync(request);
    }

    private static async Task RegisterAndAuthenticateAsync(
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

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var user = await dbContext.Users.SingleAsync(candidate => candidate.Email == email);
            user.EmailConfirmed = true;
            await dbContext.SaveChangesAsync();
        }

        using var login = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/login",
            await GetAntiforgeryTokenAsync(client),
            new { email, password = "StrongPassword!123" }));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
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

    private static async Task<Guid> AddBirdAsync(
        WebApplicationFactory<Program> factory,
        Guid farmId,
        string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var speciesId = await dbContext.Species
            .Where(species => species.ScientificName == "Turdus rufiventris")
            .Select(species => species.Id)
            .SingleAsync();
        var now = DateTimeOffset.UtcNow;
        var bird = new Bird(
            Guid.NewGuid(), now, farmId, name, speciesId, BirdSex.Female,
            null, null, null, null, null, null, null,
            DateOnly.FromDateTime(now.UtcDateTime));
        dbContext.Birds.Add(bird);
        await dbContext.SaveChangesAsync();
        return bird.Id;
    }

    private static async Task SetBirdTransferredAsync(WebApplicationFactory<Program> factory, Guid birdId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var bird = await dbContext.Birds.SingleAsync(candidate => candidate.Id == birdId);
        bird.MarkTransferPending(DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync();
    }

    private static string GetPhysicalPath(string storageRootPath, Guid farmId, string objectKey) =>
        Path.Combine(storageRootPath, farmId.ToString("N"), objectKey.Replace('/', Path.DirectorySeparatorChar));

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
        public TemporaryStorage() => RootPath = Path.Combine(
            Path.GetTempPath(), "CriatorioVirtualGalleryTests", Guid.NewGuid().ToString("N"));

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
