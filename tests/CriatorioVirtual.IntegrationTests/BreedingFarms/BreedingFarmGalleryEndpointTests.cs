using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.Infrastructure.Storage;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.BreedingFarms;

public sealed class BreedingFarmGalleryEndpointTests
{
    private const string Route = "/api/breeding-farms/gallery";

    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/8ZkAAAAASUVORK5CYII=");

    [Fact]
    public async Task OwnerCanUploadListViewEditAndRemovePrivateGalleryImages()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        using var unauthenticated = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await RegisterAndAuthenticateAsync(factory, client, "gallery-owner@example.com");
        using var withoutFarm = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.Conflict, withoutFarm.StatusCode);

        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        using var initial = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        using (var initialBody = JsonDocument.Parse(await initial.Content.ReadAsStreamAsync()))
        {
            Assert.Equal(farmId, initialBody.RootElement.GetProperty("breedingFarmId").GetGuid());
            Assert.Empty(initialBody.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(BreedingFarmGalleryUploadLimits.MaxImageCount,
                initialBody.RootElement.GetProperty("limits").GetProperty("maxImageCount").GetInt32());
            Assert.Equal(BreedingFarmGalleryUploadLimits.MaxFileLength,
                initialBody.RootElement.GetProperty("limits").GetProperty("maxFileLength").GetInt64());
        }

        using var uploaded = await UploadAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "garden.png",
            "image/png",
            PngBytes,
            "Morning in the aviary");
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        using var uploadedBody = JsonDocument.Parse(await uploaded.Content.ReadAsStreamAsync());
        var uploadedItem = uploadedBody.RootElement;
        var imageId = uploadedItem.GetProperty("imageId").GetGuid();
        Assert.Equal("Morning in the aviary", uploadedItem.GetProperty("caption").GetString());
        Assert.Equal("image/png", uploadedItem.GetProperty("contentType").GetString());
        Assert.Equal(1, uploadedItem.GetProperty("width").GetInt32());
        Assert.Equal(1, uploadedItem.GetProperty("height").GetInt32());
        var contentUrl = uploadedItem.GetProperty("contentUrl").GetString()!;
        var objectKey = await GetObjectKeyAsync(factory, imageId);
        Assert.True(File.Exists(GetPhysicalPath(storage.RootPath, farmId, objectKey)));

        using var content = await client.GetAsync(contentUrl);
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("no-store", content.Headers.CacheControl?.NoStore == true ? "no-store" : null);
        Assert.Equal("image/png", content.Content.Headers.ContentType?.MediaType);
        Assert.Equal(PngBytes, await content.Content.ReadAsByteArrayAsync());

        using var latestUpload = await UploadAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "latest.png",
            "image/png",
            PngBytes,
            null);
        Assert.Equal(HttpStatusCode.Created, latestUpload.StatusCode);
        using var latestBody = JsonDocument.Parse(await latestUpload.Content.ReadAsStreamAsync());
        var latestImageId = latestBody.RootElement.GetProperty("imageId").GetGuid();

        File.Delete(GetPhysicalPath(storage.RootPath, farmId, objectKey));
        using var missingAsset = await client.GetAsync(contentUrl);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, missingAsset.StatusCode);

        using var updated = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            $"{Route}/{imageId}",
            await GetAntiforgeryTokenAsync(client),
            new { caption = "  Updated caption  " }));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var updatedBody = JsonDocument.Parse(await updated.Content.ReadAsStreamAsync());
        Assert.Equal("Updated caption", updatedBody.RootElement.GetProperty("caption").GetString());

        using var listing = await client.GetAsync(Route);
        using var listingBody = JsonDocument.Parse(await listing.Content.ReadAsStreamAsync());
        var listedImages = listingBody.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, listedImages.Length);
        Assert.Equal(latestImageId, listedImages[0].GetProperty("imageId").GetGuid());
        Assert.Equal(imageId, listedImages[1].GetProperty("imageId").GetGuid());

        using var deleteRequest = CreateBrowserRequest(
            HttpMethod.Delete,
            $"{Route}/{imageId}",
            await GetAntiforgeryTokenAsync(client));
        using var deleted = await client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.False(File.Exists(GetPhysicalPath(storage.RootPath, farmId, objectKey)));

        using var repeatedDelete = CreateBrowserRequest(
            HttpMethod.Delete,
            $"{Route}/{imageId}",
            await GetAntiforgeryTokenAsync(client));
        using var repeatedDeleteResponse = await client.SendAsync(repeatedDelete);
        Assert.Equal(HttpStatusCode.NoContent, repeatedDeleteResponse.StatusCode);
        using var missingContent = await client.GetAsync(contentUrl);
        Assert.Equal(HttpStatusCode.NotFound, missingContent.StatusCode);
        using var emptyListing = await client.GetAsync(Route);
        using var emptyListingBody = JsonDocument.Parse(await emptyListing.Content.ReadAsStreamAsync());
        var remaining = Assert.Single(emptyListingBody.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(latestImageId, remaining.GetProperty("imageId").GetGuid());
        using var removeLatest = CreateBrowserRequest(
            HttpMethod.Delete,
            $"{Route}/{latestImageId}",
            await GetAntiforgeryTokenAsync(client));
        using var removedLatest = await client.SendAsync(removeLatest);
        Assert.Equal(HttpStatusCode.NoContent, removedLatest.StatusCode);
    }

    [Fact]
    public async Task GalleryRejectsInvalidFilesAndHidesImagesFromOtherTenants()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "gallery-owner-isolation@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);

        using var extensionMismatch = await UploadAsync(
            ownerClient,
            await GetAntiforgeryTokenAsync(ownerClient),
            "not-jpeg.jpg",
            "image/png",
            PngBytes,
            null);
        Assert.Equal(HttpStatusCode.BadRequest, extensionMismatch.StatusCode);

        using var invalidSignature = await UploadAsync(
            ownerClient,
            await GetAntiforgeryTokenAsync(ownerClient),
            "fake.png",
            "image/png",
            [1, 2, 3, 4],
            null);
        Assert.Equal(HttpStatusCode.BadRequest, invalidSignature.StatusCode);

        var oversizedDimensions = PngBytes.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
            oversizedDimensions.AsSpan(16, 4),
            BreedingFarmGalleryUploadLimits.MaxWidth + 1U);
        using var invalidDimensions = await UploadAsync(
            ownerClient,
            await GetAntiforgeryTokenAsync(ownerClient),
            "too-wide.png",
            "image/png",
            oversizedDimensions,
            null);
        Assert.Equal(HttpStatusCode.BadRequest, invalidDimensions.StatusCode);

        using var oversizedFile = await UploadAsync(
            ownerClient,
            await GetAntiforgeryTokenAsync(ownerClient),
            "too-large.png",
            "image/png",
            new byte[BreedingFarmGalleryUploadLimits.MaxFileLength + 1],
            null);
        Assert.Equal(HttpStatusCode.BadRequest, oversizedFile.StatusCode);

        using var oversizedCaption = await UploadAsync(
            ownerClient,
            await GetAntiforgeryTokenAsync(ownerClient),
            "caption-too-long.png",
            "image/png",
            PngBytes,
            new string('x', BreedingFarmGalleryImage.CaptionMaxLength + 1));
        Assert.Equal(HttpStatusCode.BadRequest, oversizedCaption.StatusCode);

        using var ownerUpload = await UploadAsync(
            ownerClient,
            await GetAntiforgeryTokenAsync(ownerClient),
            "garden.png",
            "image/png",
            PngBytes,
            null);
        Assert.Equal(HttpStatusCode.Created, ownerUpload.StatusCode);
        using var ownerUploadBody = JsonDocument.Parse(await ownerUpload.Content.ReadAsStreamAsync());
        var imageId = ownerUploadBody.RootElement.GetProperty("imageId").GetGuid();

        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, otherClient, "gallery-other-owner@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient);
        await SelectFarmAsync(otherClient, otherFarmId);
        Assert.NotEqual(ownerFarmId, otherFarmId);

        using var content = await otherClient.GetAsync($"{Route}/{imageId}/content");
        Assert.Equal(HttpStatusCode.NotFound, content.StatusCode);
        using var update = await otherClient.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            $"{Route}/{imageId}",
            await GetAntiforgeryTokenAsync(otherClient),
            new { caption = "stolen" }));
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        using var delete = CreateBrowserRequest(
            HttpMethod.Delete,
            $"{Route}/{imageId}",
            await GetAntiforgeryTokenAsync(otherClient));
        using var deleted = await otherClient.SendAsync(delete);
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        using var otherListing = await otherClient.GetAsync(Route);
        using var otherListingBody = JsonDocument.Parse(await otherListing.Content.ReadAsStreamAsync());
        Assert.Empty(otherListingBody.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task ConcurrentUploadsCannotExceedFarmLimitAndRejectedObjectIsCleanedUp()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "gallery-limit@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var now = DateTimeOffset.UtcNow.AddDays(-1);
            dbContext.BreedingFarmGalleryImages.AddRange(Enumerable.Range(0, BreedingFarmGalleryUploadLimits.MaxImageCount - 1)
                .Select(index => new BreedingFarmGalleryImage(
                    Guid.NewGuid(),
                    now.AddTicks(index),
                    farmId,
                    $"gallery/seed-{index}",
                    "seed.png",
                    "image/png",
                    PngBytes.Length,
                    1,
                    1,
                    null)));
            await dbContext.SaveChangesAsync();
        }

        var tokenA = await GetAntiforgeryTokenAsync(client);
        var tokenB = await GetAntiforgeryTokenAsync(client);
        var uploads = await Task.WhenAll(
            UploadAsync(client, tokenA, "a.png", "image/png", PngBytes, null),
            UploadAsync(client, tokenB, "b.png", "image/png", PngBytes, null));
        try
        {
            Assert.Single(uploads, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Single(uploads, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var response in uploads)
            {
                response.Dispose();
            }
        }

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Equal(BreedingFarmGalleryUploadLimits.MaxImageCount,
            await verifyContext.BreedingFarmGalleryImages.CountAsync(image => image.BreedingFarmId == farmId && image.DeletedAtUtc == null));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(storage.RootPath, farmId.ToString("N"), "gallery")));
    }

    [Fact]
    public async Task FailedStorageCleanupIsRecordedAndCanBeRetried()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        var failingStorage = new FailingDeleteStorage(storage.RootPath);
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath, failingStorage);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "gallery-cleanup@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);

        using var upload = await UploadAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "cleanup.png",
            "image/png",
            PngBytes,
            null);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        using var uploadBody = JsonDocument.Parse(await upload.Content.ReadAsStreamAsync());
        var imageId = uploadBody.RootElement.GetProperty("imageId").GetGuid();
        var objectKey = await GetObjectKeyAsync(factory, imageId);
        var imagePath = GetPhysicalPath(storage.RootPath, farmId, objectKey);

        using var delete = CreateBrowserRequest(
            HttpMethod.Delete,
            $"{Route}/{imageId}",
            await GetAntiforgeryTokenAsync(client));
        using var failedDelete = await client.SendAsync(delete);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failedDelete.StatusCode);
        Assert.True(File.Exists(imagePath));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var image = await dbContext.BreedingFarmGalleryImages.SingleAsync(candidate => candidate.Id == imageId);
            Assert.True(image.IsDeleted);
            Assert.True(image.StorageCleanupPending);
        }

        failingStorage.FailDelete = false;
        using var retry = CreateBrowserRequest(
            HttpMethod.Delete,
            $"{Route}/{imageId}",
            await GetAntiforgeryTokenAsync(client));
        using var successfulRetry = await client.SendAsync(retry);
        Assert.Equal(HttpStatusCode.NoContent, successfulRetry.StatusCode);
        Assert.False(File.Exists(imagePath));
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var cleanedImage = await verifyContext.BreedingFarmGalleryImages.SingleAsync(candidate => candidate.Id == imageId);
        Assert.False(cleanedImage.StorageCleanupPending);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate,
        string storageRootPath,
        IPrivateObjectStorage? storageOverride = null) =>
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
                if (storageOverride is not null)
                {
                    services.RemoveAll<IPrivateObjectStorage>();
                    services.AddSingleton(storageOverride);
                }
            });
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
        string antiforgeryToken,
        string fileName,
        string contentType,
        byte[] bytes,
        string? caption)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Route);
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName, antiforgeryToken);
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

    private static async Task<string> GetObjectKeyAsync(WebApplicationFactory<Program> factory, Guid imageId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        return (await dbContext.BreedingFarmGalleryImages.SingleAsync(image => image.Id == imageId)).ObjectKey;
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
        public TemporaryStorage() => RootPath = Path.Combine(Path.GetTempPath(), "CriatorioVirtualGalleryTests", Guid.NewGuid().ToString("N"));

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

    private sealed class FailingDeleteStorage : IPrivateObjectStorage
    {
        private readonly FileSystemPrivateObjectStorage innerStorage;

        public FailingDeleteStorage(string rootPath) =>
            innerStorage = new FileSystemPrivateObjectStorage(Options.Create(new PrivateStorageOptions
            {
                PrivateRootPath = rootPath
            }));

        public bool FailDelete { get; set; } = true;

        public Task<PrivateObjectDescriptor> PutAsync(PrivateObjectUpload upload, CancellationToken cancellationToken = default) =>
            innerStorage.PutAsync(upload, cancellationToken);

        public Task<Stream> OpenReadAsync(Guid breedingFarmId, string objectKey, CancellationToken cancellationToken = default) =>
            innerStorage.OpenReadAsync(breedingFarmId, objectKey, cancellationToken);

        public Task DeleteAsync(Guid breedingFarmId, string objectKey, CancellationToken cancellationToken = default)
        {
            if (FailDelete)
            {
                throw new IOException("Simulated private storage failure.");
            }

            return innerStorage.DeleteAsync(breedingFarmId, objectKey, cancellationToken);
        }
    }
}
