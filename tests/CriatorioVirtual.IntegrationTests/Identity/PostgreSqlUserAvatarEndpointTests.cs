using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Application.Identity;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Infrastructure.Identity;
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

namespace CriatorioVirtual.IntegrationTests.Identity;

public sealed class PostgreSqlUserAvatarEndpointTests
{
    private static readonly byte[] ValidPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAAAAAA6fptVAAAACklEQVR4nGNgAAAAAgABSK+kcQAAAABJRU5ErkJggg==");

    private static readonly byte[] InvalidJpegWithPlausibleHeaders =
    [
        0xFF, 0xD8,
        0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00,
        0xFF, 0xDA, 0x00, 0x08, 0x01, 0x02, 0x00, 0x00, 0x3F, 0x00,
        0x00, 0xFF, 0xD9
    ];

    [Fact]
    public async Task AvatarUploadReplacementRemovalAndSession_ArePrivateToTheAuthenticatedUser()
    {
        var storageRoot = Path.Combine(Path.GetTempPath(), $"criatorio-user-avatar-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);
        try
        {
            await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
            await database.StartAsync();
            using var certificate = TestCertificate.Create();
            using var factory = CreateFactory(database.GetConnectionString(), certificate, storageRoot);
            await MigrateAsync(factory);
            var storage = new FileSystemPrivateObjectStorage(Options.Create(new PrivateStorageOptions
            {
                PrivateRootPath = storageRoot
            }));

            using var owner = CreateClient(factory);
            var ownerId = await RegisterAndLoginAsync(factory, owner, "avatar-owner@example.com");

            using (var unauthenticated = CreateClient(factory))
            {
                using var get = await unauthenticated.GetAsync("/api/me/avatar");
                using var delete = await unauthenticated.DeleteAsync("/api/me/avatar");
                Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
            }

            using (var sessionWithoutAvatar = await owner.GetAsync("/api/auth/session"))
            {
                Assert.Equal(HttpStatusCode.OK, sessionWithoutAvatar.StatusCode);
                using var document = JsonDocument.Parse(await sessionWithoutAvatar.Content.ReadAsStreamAsync());
                Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("avatarUrl").ValueKind);
            }

            using (var firstUpload = await UploadAsync(owner, "avatar.png", "image/png", ValidPng))
            {
                Assert.True(
                    firstUpload.StatusCode == HttpStatusCode.OK,
                    $"{firstUpload.StatusCode}: {await firstUpload.Content.ReadAsStringAsync()}");
                using var document = JsonDocument.Parse(await firstUpload.Content.ReadAsStreamAsync());
                Assert.Equal("/api/me/avatar", document.RootElement.GetProperty("avatarUrl").GetString());
            }

            string firstKey;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
                var user = await dbContext.Users.SingleAsync(candidate => candidate.Id == ownerId);
                firstKey = Assert.IsType<string>(user.AvatarObjectKey);
                Assert.Equal("image/png", user.AvatarContentType);
                Assert.DoesNotContain("http", firstKey, StringComparison.OrdinalIgnoreCase);
            }

            using (var ownAvatar = await owner.GetAsync("/api/me/avatar"))
            {
                Assert.Equal(HttpStatusCode.OK, ownAvatar.StatusCode);
                Assert.Equal("image/png", ownAvatar.Content.Headers.ContentType?.MediaType);
                Assert.Equal(ValidPng, await ownAvatar.Content.ReadAsByteArrayAsync());
            }

            using (var sessionWithAvatar = await owner.GetAsync("/api/auth/session"))
            {
                using var document = JsonDocument.Parse(await sessionWithAvatar.Content.ReadAsStreamAsync());
                Assert.Equal("/api/me/avatar", document.RootElement.GetProperty("avatarUrl").GetString());
            }

            using (var validJpegUpload = await UploadAsync(owner, "avatar.jpg", "image/jpeg", LoadValidJpeg()))
            {
                Assert.Equal(HttpStatusCode.OK, validJpegUpload.StatusCode);
            }

            using (var replacement = await UploadAsync(owner, "replacement.png", "image/png", ValidPng))
            {
                Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);
            }

            string replacementKey;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
                var user = await dbContext.Users.SingleAsync(candidate => candidate.Id == ownerId);
                replacementKey = Assert.IsType<string>(user.AvatarObjectKey);
            }

            Assert.NotEqual(firstKey, replacementKey);
            await AssertMissingObjectAsync(storage, ownerId, firstKey);
            await using (var replacementContent = await storage.OpenReadAsync(ownerId, replacementKey))
            {
                Assert.True(replacementContent.CanRead);
            }

            var concurrencyToken = await GetAntiforgeryTokenAsync(owner);
            var concurrentUploads = await Task.WhenAll(
                UploadWithTokenAsync(owner, "parallel-a.png", "image/png", ValidPng, concurrencyToken),
                UploadWithTokenAsync(owner, "parallel-b.png", "image/png", ValidPng, concurrencyToken));
            using var parallelFirst = concurrentUploads[0];
            using var parallelSecond = concurrentUploads[1];
            Assert.Contains(concurrentUploads, response => response.StatusCode == HttpStatusCode.OK);
            Assert.All(concurrentUploads, response =>
                Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));

            string currentAvatarKey;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
                var user = await dbContext.Users.SingleAsync(candidate => candidate.Id == ownerId);
                currentAvatarKey = Assert.IsType<string>(user.AvatarObjectKey);
            }

            await AssertMissingObjectAsync(storage, ownerId, replacementKey);
            Assert.Single(Directory.EnumerateFiles(storageRoot, "*", SearchOption.AllDirectories));

            using (var otherUser = CreateClient(factory))
            {
                await RegisterAndLoginAsync(factory, otherUser, "other-avatar-user@example.com");
                using var otherAvatar = await otherUser.GetAsync("/api/me/avatar");
                Assert.Equal(HttpStatusCode.NotFound, otherAvatar.StatusCode);
                using var otherSession = await otherUser.GetAsync("/api/auth/session");
                using var document = JsonDocument.Parse(await otherSession.Content.ReadAsStreamAsync());
                Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("avatarUrl").ValueKind);
            }

            using (var remove = await DeleteAvatarAsync(owner))
            {
                Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
            }

            await AssertMissingObjectAsync(storage, ownerId, currentAvatarKey);
            using (var removedAvatar = await owner.GetAsync("/api/me/avatar"))
            {
                Assert.Equal(HttpStatusCode.NotFound, removedAvatar.StatusCode);
            }

            using (var sessionAfterRemoval = await owner.GetAsync("/api/auth/session"))
            {
                using var document = JsonDocument.Parse(await sessionAfterRemoval.Content.ReadAsStreamAsync());
                Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("avatarUrl").ValueKind);
            }

            using var repeatedRemove = await DeleteAvatarAsync(owner);
            Assert.Equal(HttpStatusCode.NoContent, repeatedRemove.StatusCode);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
                var user = await dbContext.Users.SingleAsync(candidate => candidate.Id == ownerId);
                user.AvatarObjectKey = "user-avatars/inconsistent-reference";
                await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
            }
        }
        finally
        {
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AvatarUpload_RejectsInvalidMetadataCorruptImagesAndOversizedFiles()
    {
        var storageRoot = Path.Combine(Path.GetTempPath(), $"criatorio-user-avatar-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);
        try
        {
            await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
            await database.StartAsync();
            using var certificate = TestCertificate.Create();
            using var factory = CreateFactory(database.GetConnectionString(), certificate, storageRoot);
            await MigrateAsync(factory);
            using var owner = CreateClient(factory);
            await RegisterAndLoginAsync(factory, owner, "avatar-validation@example.com");

            using (var wrongMetadata = await UploadAsync(owner, "avatar.jpg", "image/png", ValidPng))
            {
                Assert.Equal(HttpStatusCode.BadRequest, wrongMetadata.StatusCode);
            }

            using (var unsupportedMime = await UploadAsync(owner, "avatar.svg", "image/svg+xml", "<svg/>"u8.ToArray()))
            {
                Assert.Equal(HttpStatusCode.BadRequest, unsupportedMime.StatusCode);
            }

            using (var corruptImage = await UploadAsync(owner, "avatar.png", "image/png", [0x89, 0x50, 0x4E, 0x47]))
            {
                Assert.Equal(HttpStatusCode.BadRequest, corruptImage.StatusCode);
            }

            using (var corruptJpeg = await UploadAsync(
                       owner,
                       "avatar.jpg",
                       "image/jpeg",
                       InvalidJpegWithPlausibleHeaders))
            {
                Assert.Equal(HttpStatusCode.BadRequest, corruptJpeg.StatusCode);
            }

            var oversizedImage = new byte[UserAvatarUploadLimits.MaxFileLength + 1];
            using (var oversized = await UploadAsync(owner, "avatar.png", "image/png", oversizedImage))
            {
                Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
            }

            await using var scope = factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var user = await dbContext.Users.SingleAsync(candidate => candidate.NormalizedEmail == "AVATAR-VALIDATION@EXAMPLE.COM");
            Assert.Null(user.AvatarObjectKey);
            Assert.Null(user.AvatarContentType);
            Assert.Empty(Directory.EnumerateFiles(storageRoot, "*", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate,
        string storageRoot) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Security:Google:ClientId", "test-client-id");
            builder.UseSetting("Security:Google:ClientSecret", "test-client-secret");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Google:ClientId"] = "test-client-id",
                ["Security:Google:ClientSecret"] = "test-client-secret",
                ["Logging:EventLog:LogLevel:Default"] = "None"
            }));
            builder.ConfigureServices(services =>
            {
                services.AddInfrastructurePersistence(connectionString, certificate);
                services.RemoveAll<IPrivateObjectStorage>();
                services.AddSingleton<IPrivateObjectStorage>(new FileSystemPrivateObjectStorage(Options.Create(
                    new PrivateStorageOptions { PrivateRootPath = storageRoot })));
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });

    private static async Task<Guid> RegisterAndLoginAsync(
        WebApplicationFactory<Program> factory,
        HttpClient client,
        string email)
    {
        var password = "StrongPassword!123";
        using (var register = await client.SendAsync(CreateBrowserRequest(
                         HttpMethod.Post,
                         "/api/auth/register",
                         JsonContent.Create(new { email, password, confirmPassword = password }),
                         await GetAntiforgeryTokenAsync(client))))
        {
            Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        }

        Guid userId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var user = await dbContext.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant());
            user.EmailConfirmed = true;
            await dbContext.SaveChangesAsync();
            userId = user.Id;
        }

        using (var login = await client.SendAsync(CreateBrowserRequest(
                         HttpMethod.Post,
                         "/api/auth/login",
                         JsonContent.Create(new { email, password }),
                         await GetAntiforgeryTokenAsync(client))))
        {
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        }

        return userId;
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        string fileName,
        string contentType,
        byte[] bytes)
    {
        return await UploadWithTokenAsync(
            client,
            fileName,
            contentType,
            bytes,
            await GetAntiforgeryTokenAsync(client));
    }

    private static async Task<HttpResponseMessage> UploadWithTokenAsync(
        HttpClient client,
        string fileName,
        string contentType,
        byte[] bytes,
        string antiforgeryToken)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        using var request = CreateBrowserRequest(
            HttpMethod.Put,
            "/api/me/avatar",
            form,
            antiforgeryToken);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> DeleteAvatarAsync(HttpClient client)
    {
        using var request = CreateBrowserRequest(
            HttpMethod.Delete,
            "/api/me/avatar",
            content: null,
            antiforgeryToken: await GetAntiforgeryTokenAsync(client));
        return await client.SendAsync(request);
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
        HttpContent? content,
        string antiforgeryToken)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName, antiforgeryToken);
        return request;
    }

    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    private static async Task AssertMissingObjectAsync(
        IPrivateObjectStorage storage,
        Guid userId,
        string objectKey)
    {
        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
        {
            await using var _ = await storage.OpenReadAsync(userId, objectKey);
        });
    }

    private static byte[] LoadValidJpeg()
    {
        using var stream = typeof(PrivateObjectStorageUserAvatarAdapter).Assembly.GetManifestResourceStream(
            "CriatorioVirtual.Infrastructure.Documents.Assets.criatorio-virtual-default-bird.jpg")
            ?? throw new InvalidOperationException("The valid JPEG test asset is unavailable.");
        using var content = new MemoryStream();
        stream.CopyTo(content);
        return content.ToArray();
    }
}
