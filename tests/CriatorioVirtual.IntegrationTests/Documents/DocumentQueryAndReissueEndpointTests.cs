using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Documents;
using CriatorioVirtual.Infrastructure.Identity;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Documents;

public sealed class DocumentQueryAndReissueEndpointTests
{
    private static readonly byte[] VisualIdentityPngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/8ZkAAAAASUVORK5CYII=");

    [Fact]
    public async Task ListDownloadAndReissuePreservePrivateDocumentVersionsAndCurrentSnapshots()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "documents-query-owner@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(client, speciesId, "Nome original", "123456");

        var originalBadge = await GenerateAsync(client, birdId, new
        {
            type = "Badge",
            modelId = "Classic",
            printSize = "Small",
            selectedFields = new[] { "Name", "RingNumber" }
        });
        var originalCertificate = await GenerateAsync(client, birdId, new { type = "GenealogyCertificate" });
        var originalProvenance = await GenerateAsync(client, birdId, new { type = "ProvenanceDocument" });
        Assert.Equal(HttpStatusCode.Created, originalBadge.StatusCode);
        Assert.Equal(HttpStatusCode.Created, originalCertificate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, originalProvenance.StatusCode);

        var originalIds = new Dictionary<string, Guid>();
        var originalBytes = new Dictionary<Guid, byte[]>();
        using (var list = await client.GetAsync($"/api/birds/{birdId}/documents"))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            using var listBody = JsonDocument.Parse(await list.Content.ReadAsStreamAsync());
            var root = listBody.RootElement;
            Assert.Equal(farmId, root.GetProperty("breedingFarmId").GetGuid());
            Assert.Equal(birdId, root.GetProperty("birdId").GetGuid());
            var items = root.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(3, items.Length);
            Assert.DoesNotContain(items, item => item.GetProperty("type").GetString() == "InternalRecord");

            foreach (var item in items)
            {
                var type = item.GetProperty("type").GetString()!;
                var documentId = item.GetProperty("documentId").GetGuid();
                originalIds.Add(type, documentId);
                Assert.Equal(birdId, item.GetProperty("birdId").GetGuid());
                Assert.Equal(
                    $"/api/birds/{birdId}/documents/{documentId}/content",
                    item.GetProperty("downloadUrl").GetString());
                Assert.Equal("application/pdf", item.GetProperty("contentType").GetString());

                using var download = await client.GetAsync(item.GetProperty("downloadUrl").GetString());
                Assert.Equal(HttpStatusCode.OK, download.StatusCode);
                Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
                var bytes = await download.Content.ReadAsByteArrayAsync();
                Assert.Equal("%PDF-1.4", System.Text.Encoding.ASCII.GetString(bytes, 0, 8));
                originalBytes.Add(documentId, bytes);
            }

            Assert.Equal(["Badge", "GenealogyCertificate", "ProvenanceDocument"], originalIds.Keys.OrderBy(type => type));
        }

        var originalSnapshots = await ReadSnapshotsAsync(factory, originalIds.Values);

        using (var update = await client.SendAsync(CreateBrowserRequest(
                   HttpMethod.Put,
                   $"/api/birds/{birdId}",
                   await GetAntiforgeryTokenAsync(client),
                   new
                   {
                       name = "Nome atualizado",
                       sex = "Female",
                       speciesId,
                       birthDate = "2020-09-07",
                       ringNumber = "123456",
                       notes = (string?)null
                   })))
        {
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        }

        using var badgeReissue = await ReissueAsync(client, birdId, originalIds["Badge"], new
        {
            modelId = "Minimalist",
            printSize = "Medium",
            selectedFields = new[] { "Name", "Species" }
        });
        using var certificateReissue = await ReissueAsync(client, birdId, originalIds["GenealogyCertificate"], null);
        using var provenanceReissue = await ReissueAsync(client, birdId, originalIds["ProvenanceDocument"], null);
        Assert.Equal(HttpStatusCode.Created, badgeReissue.StatusCode);
        Assert.Equal(HttpStatusCode.Created, certificateReissue.StatusCode);
        Assert.Equal(HttpStatusCode.Created, provenanceReissue.StatusCode);

        var reissuedIds = new Dictionary<string, Guid>();
        using (var response = badgeReissue)
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()))
        {
            var root = body.RootElement;
            reissuedIds["Badge"] = root.GetProperty("documentId").GetGuid();
            Assert.Equal("Minimalist", root.GetProperty("modelId").GetString());
            Assert.Equal("Medium", root.GetProperty("printSize").GetString());
            Assert.Equal(
                ["Name", "Species"],
                root.GetProperty("selectedFields").EnumerateArray().Select(field => field.GetString()!).ToArray());
        }

        using (var response = certificateReissue)
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()))
        {
            reissuedIds["GenealogyCertificate"] = body.RootElement.GetProperty("documentId").GetGuid();
        }

        using (var response = provenanceReissue)
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()))
        {
            reissuedIds["ProvenanceDocument"] = body.RootElement.GetProperty("documentId").GetGuid();
        }

        Assert.Equal(3, reissuedIds.Count);
        Assert.All(originalIds, pair => Assert.NotEqual(pair.Value, reissuedIds[pair.Key]));

        using (var list = await client.GetAsync($"/api/birds/{birdId}/documents"))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            using var listBody = JsonDocument.Parse(await list.Content.ReadAsStreamAsync());
            var items = listBody.RootElement.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(6, items.Length);
            Assert.All(reissuedIds.Values, documentId => Assert.Contains(
                items,
                item => item.GetProperty("documentId").GetGuid() == documentId));
        }

        var allDocuments = await ReadDocumentsAsync(factory, birdId);
        Assert.Equal(6, allDocuments.Count);
        Assert.All(allDocuments, document =>
        {
            Assert.True(File.Exists(GetPhysicalPath(storage.RootPath, farmId, document.ObjectKey)));
            Assert.Equal(farmId, document.CreatedByBreedingFarmId);
        });

        foreach (var pair in originalIds)
        {
            var original = allDocuments.Single(document => document.Id == pair.Value);
            var reissued = allDocuments.Single(document => document.Id == reissuedIds[pair.Key]);
            Assert.Equal(originalSnapshots[pair.Value], original.SnapshotJson);
            using var reissuedSnapshot = JsonDocument.Parse(reissued.SnapshotJson);
            Assert.Equal("Nome atualizado", reissuedSnapshot.RootElement.GetProperty("name").GetString());
            Assert.NotEqual(original.ObjectKey, reissued.ObjectKey);

            using var originalDownload = await client.GetAsync($"/api/birds/{birdId}/documents/{pair.Value}/content");
            Assert.Equal(HttpStatusCode.OK, originalDownload.StatusCode);
            Assert.Equal(originalBytes[pair.Value], await originalDownload.Content.ReadAsByteArrayAsync());

            using var reissuedDownload = await client.GetAsync($"/api/birds/{birdId}/documents/{reissued.Id}/content");
            Assert.Equal(HttpStatusCode.OK, reissuedDownload.StatusCode);
            Assert.Equal("application/pdf", reissuedDownload.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task NewEmissionsAndHistoricalReissuesKeepTheirVisualIdentitySnapshot()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "documents-identity-snapshot-owner@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(client, speciesId, "Ave identificada", "123456");
        var requests = new Dictionary<string, object>
        {
            ["Badge"] = new
            {
                type = "Badge",
                modelId = "Classic",
                printSize = "Small",
                selectedFields = new[] { "Name", "RingNumber" }
            },
            ["GenealogyCertificate"] = new { type = "GenealogyCertificate" },
            ["ProvenanceDocument"] = new { type = "ProvenanceDocument" }
        };

        using (var upload = await UploadVisualIdentityAsync(client, "original-logo.png"))
        {
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }

        var originalIds = await GenerateDocumentsAsync(client, birdId, requests);
        using (var upload = await UploadVisualIdentityAsync(client, "replacement-logo.png"))
        {
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }

        var replacementIds = await GenerateDocumentsAsync(client, birdId, requests);
        var reissuedIds = new Dictionary<string, Guid>();
        foreach (var pair in originalIds)
        {
            using var response = await ReissueAsync(client, birdId, pair.Value, null);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            reissuedIds[pair.Key] = body.RootElement.GetProperty("documentId").GetGuid();
        }

        var documents = await ReadDocumentsAsync(factory, birdId);
        async Task AssertIdentitySnapshotAsync(Guid documentId, string expectedFileName)
        {
            var document = documents.Single(candidate => candidate.Id == documentId);
            using var snapshot = JsonDocument.Parse(document.SnapshotJson);
            var identity = snapshot.RootElement
                .GetProperty("breedingFarmDetails")
                .GetProperty("visualIdentity");
            Assert.Equal(expectedFileName, identity.GetProperty("fileName").GetString());
            var objectKey = identity.GetProperty("objectKey").GetString()!;
            Assert.True(File.Exists(GetPhysicalPath(storage.RootPath, farmId, objectKey)));
            Assert.Equal(
                VisualIdentityPngBytes,
                await File.ReadAllBytesAsync(GetPhysicalPath(storage.RootPath, farmId, objectKey)));
        }

        foreach (var documentId in originalIds.Values.Concat(reissuedIds.Values))
        {
            await AssertIdentitySnapshotAsync(documentId, "original-logo.png");
        }

        foreach (var documentId in replacementIds.Values)
        {
            await AssertIdentitySnapshotAsync(documentId, "replacement-logo.png");
        }
    }

    [Fact]
    public async Task DocumentListingAndReissueRespectTenantAndOwnerBoundaries()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        using var anonymousClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "documents-boundary-owner@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(ownerClient, speciesId, "Ave privada", "654321");
        using var generated = await GenerateAsync(ownerClient, birdId, new
        {
            type = "Badge",
            modelId = "Classic",
            printSize = "Small",
            selectedFields = new[] { "Name", "RingNumber" }
        });
        Assert.Equal(HttpStatusCode.Created, generated.StatusCode);
        using var generatedBody = JsonDocument.Parse(await generated.Content.ReadAsStreamAsync());
        var documentId = generatedBody.RootElement.GetProperty("documentId").GetGuid();

        using (var anonymousList = await anonymousClient.GetAsync($"/api/birds/{birdId}/documents"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousList.StatusCode);
        }

        await RegisterAndAuthenticateAsync(factory, otherClient, "documents-boundary-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient);
        await SelectFarmAsync(otherClient, otherFarmId);

        using (var foreignList = await otherClient.GetAsync($"/api/birds/{birdId}/documents"))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreignList.StatusCode);
        }

        using (var foreignDownload = await otherClient.GetAsync($"/api/birds/{birdId}/documents/{documentId}/content"))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreignDownload.StatusCode);
        }

        using (var foreignReissue = await ReissueAsync(otherClient, birdId, documentId, null))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreignReissue.StatusCode);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var otherUser = await dbContext.Users.SingleAsync(
                candidate => candidate.Email == "documents-boundary-other@example.com");
            otherUser.SelectedBreedingFarmId = ownerFarmId;
            dbContext.BreedingFarmUsers.Add(new BreedingFarmUser(
                ownerFarmId,
                otherUser.Id,
                BreedingFarmRole.Viewer,
                DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync();
        }

        using (var viewerList = await otherClient.GetAsync($"/api/birds/{birdId}/documents"))
        {
            Assert.Equal(HttpStatusCode.OK, viewerList.StatusCode);
        }

        using (var viewerDownload = await otherClient.GetAsync($"/api/birds/{birdId}/documents/{documentId}/content"))
        {
            Assert.Equal(HttpStatusCode.OK, viewerDownload.StatusCode);
        }

        using (var viewerReissue = await ReissueAsync(otherClient, birdId, documentId, null))
        {
            Assert.Equal(HttpStatusCode.NotFound, viewerReissue.StatusCode);
        }
    }

    [Fact]
    public async Task ReissueRejectsPartialBadgeConfigurationAndOverridesForFixedDocuments()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "documents-reissue-validation@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(client, speciesId, "Ave validada", "765432");
        using var badge = await GenerateAsync(client, birdId, new
        {
            type = "Badge",
            modelId = "Classic",
            printSize = "Small",
            selectedFields = new[] { "Name" }
        });
        using var certificateDocument = await GenerateAsync(client, birdId, new { type = "GenealogyCertificate" });
        Assert.Equal(HttpStatusCode.Created, badge.StatusCode);
        Assert.Equal(HttpStatusCode.Created, certificateDocument.StatusCode);
        using var badgeBody = JsonDocument.Parse(await badge.Content.ReadAsStreamAsync());
        using var certificateBody = JsonDocument.Parse(await certificateDocument.Content.ReadAsStreamAsync());
        var badgeId = badgeBody.RootElement.GetProperty("documentId").GetGuid();
        var certificateId = certificateBody.RootElement.GetProperty("documentId").GetGuid();

        using var partial = await ReissueAsync(client, birdId, badgeId, new { modelId = "Minimalist" });
        Assert.Equal(HttpStatusCode.BadRequest, partial.StatusCode);

        using var fixedOverride = await ReissueAsync(client, birdId, certificateId, new
        {
            modelId = "Classic",
            printSize = "Small",
            selectedFields = new[] { "Name" }
        });
        Assert.Equal(HttpStatusCode.BadRequest, fixedOverride.StatusCode);

        using var list = await client.GetAsync($"/api/birds/{birdId}/documents");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listBody = JsonDocument.Parse(await list.Content.ReadAsStreamAsync());
        Assert.Equal(2, listBody.RootElement.GetProperty("items").GetArrayLength());
    }

    private static async Task<HttpResponseMessage> GenerateAsync(HttpClient client, Guid birdId, object request) =>
        await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/birds/{birdId}/documents",
            await GetAntiforgeryTokenAsync(client),
            request));

    private static async Task<Dictionary<string, Guid>> GenerateDocumentsAsync(
        HttpClient client,
        Guid birdId,
        IReadOnlyDictionary<string, object> requests)
    {
        var documentIds = new Dictionary<string, Guid>();
        foreach (var pair in requests)
        {
            using var response = await GenerateAsync(client, birdId, pair.Value);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            documentIds[pair.Key] = body.RootElement.GetProperty("documentId").GetGuid();
        }

        return documentIds;
    }

    private static async Task<HttpResponseMessage> UploadVisualIdentityAsync(
        HttpClient client,
        string fileName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/breeding-farms/visual-identity");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add(
            HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName,
            await GetAntiforgeryTokenAsync(client));
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(VisualIdentityPngBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "file", fileName);
        request.Content = form;
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> ReissueAsync(
        HttpClient client,
        Guid birdId,
        Guid documentId,
        object? request) =>
        await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/birds/{birdId}/documents/{documentId}/reissue",
            await GetAntiforgeryTokenAsync(client),
            request));

    private static async Task<Guid> CreateBirdAsync(
        HttpClient client,
        Guid speciesId,
        string name,
        string ringNumber)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/birds",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                name,
                sex = "Female",
                speciesId,
                birthDate = "2020-09-07",
                ringNumber
            }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("birdId").GetGuid();
    }

    private static async Task<IReadOnlyDictionary<Guid, string>> ReadSnapshotsAsync(
        WebApplicationFactory<Program> factory,
        IEnumerable<Guid> documentIds)
    {
        var ids = documentIds.ToArray();
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        return await dbContext.BirdDocuments
            .AsNoTracking()
            .Where(document => ids.Contains(document.Id))
            .ToDictionaryAsync(document => document.Id, document => document.SnapshotJson);
    }

    private static async Task<IReadOnlyCollection<BirdDocument>> ReadDocumentsAsync(
        WebApplicationFactory<Program> factory,
        Guid birdId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        return await dbContext.BirdDocuments
            .AsNoTracking()
            .Where(document => document.BirdId == birdId)
            .ToArrayAsync();
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

    private static async Task RegisterAndAuthenticateAsync(
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

    private static string GetPhysicalPath(string rootPath, Guid farmId, string objectKey) =>
        Path.Combine(rootPath, farmId.ToString("N"), objectKey.Replace('/', Path.DirectorySeparatorChar));

    private sealed class TemporaryStorage : IAsyncDisposable
    {
        public TemporaryStorage() =>
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "CriatorioVirtualDocumentQueryTests",
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
