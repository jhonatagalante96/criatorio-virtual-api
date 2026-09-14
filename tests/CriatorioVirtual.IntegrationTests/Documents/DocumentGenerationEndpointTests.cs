using System.Net;
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
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Documents;

public sealed class DocumentGenerationEndpointTests
{
    [Fact]
    public async Task GenerateBadgePersistsConfigurationAndReturnsPrivateDownload()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "documents-generate-owner@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(client, speciesId, "Luna", "123456");

        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/birds/{birdId}/documents",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                type = "Badge",
                modelId = "Photographic",
                printSize = "Medium",
                selectedFields = new[] { "Name", "RingNumber", "Species" }
            }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var responseBody = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = responseBody.RootElement;
        var documentId = root.GetProperty("documentId").GetGuid();
        Assert.Equal(birdId, root.GetProperty("birdId").GetGuid());
        Assert.Equal("Badge", root.GetProperty("type").GetString());
        Assert.Equal("Photographic", root.GetProperty("modelId").GetString());
        Assert.Equal("Medium", root.GetProperty("printSize").GetString());
        Assert.Equal(
            ["Name", "RingNumber", "Species"],
            root.GetProperty("selectedFields").EnumerateArray().Select(item => item.GetString()!).ToArray());
        Assert.Equal(
            $"/api/birds/{birdId}/documents/{documentId}/content",
            root.GetProperty("downloadUrl").GetString());
        Assert.Equal($"/api/birds/{birdId}/documents/{documentId}/content", response.Headers.Location?.AbsolutePath);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var document = await dbContext.BirdDocuments.SingleAsync(candidate => candidate.Id == documentId);
            Assert.Equal(farmId, document.CreatedByBreedingFarmId);
            Assert.Equal("application/pdf", document.ContentType);
            Assert.True(document.Length > 0);
            Assert.Equal(
                ["Name", "RingNumber", "Species"],
                JsonDocument.Parse(document.SelectedFieldsJson).RootElement
                    .EnumerateArray()
                    .Select(item => item.GetString()!)
                    .ToArray());
            using var snapshot = JsonDocument.Parse(document.SnapshotJson);
            Assert.Equal("Luna", snapshot.RootElement.GetProperty("name").GetString());
            Assert.Equal("123456", snapshot.RootElement.GetProperty("ringNumber").GetString());
            Assert.Equal("Turdus rufiventris", snapshot.RootElement.GetProperty("species").GetString());
            Assert.True(File.Exists(GetPhysicalPath(storage.RootPath, farmId, document.ObjectKey)));
        }

        using var download = await client.GetAsync(root.GetProperty("downloadUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("%PDF-1.4", (await download.Content.ReadAsStringAsync())[..8]);
    }

    [Fact]
    public async Task GenerateBadgeReemissionCreatesNewVersion()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "documents-reissue-owner@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(client, speciesId, "Ave reemitida", "123457");

        var request = new
        {
            type = "Badge",
            modelId = "Classic",
            printSize = "Small",
            selectedFields = new[] { "Name", "RingNumber" }
        };
        var first = await GenerateAsync(client, birdId, request);
        var second = await GenerateAsync(client, birdId, request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        using var firstBody = JsonDocument.Parse(await first.Content.ReadAsStreamAsync());
        using var secondBody = JsonDocument.Parse(await second.Content.ReadAsStreamAsync());
        var firstDocumentId = firstBody.RootElement.GetProperty("documentId").GetGuid();
        var secondDocumentId = secondBody.RootElement.GetProperty("documentId").GetGuid();
        Assert.NotEqual(firstDocumentId, secondDocumentId);
        Assert.Equal("Badge", firstBody.RootElement.GetProperty("type").GetString());
        Assert.Equal("Classic", firstBody.RootElement.GetProperty("modelId").GetString());
        Assert.Equal("Small", firstBody.RootElement.GetProperty("printSize").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var documents = await dbContext.BirdDocuments
            .Where(document => document.BirdId == birdId)
            .ToArrayAsync();
        Assert.Equal(2, documents.Length);
        Assert.All(documents, document =>
        {
            Assert.Equal(farmId, document.CreatedByBreedingFarmId);
            Assert.Equal(BadgeModelId.Classic, document.ModelId);
            Assert.Equal(BadgePrintSize.Small, document.PrintSize);
            Assert.True(File.Exists(GetPhysicalPath(storage.RootPath, farmId, document.ObjectKey)));
        });
    }

    [Fact]
    public async Task GenerateGenealogyCertificatePersistsFarmSnapshotAndReturnsLandscapePdf()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "genealogy-certificate-owner@example.com");
        var farmId = await CreateFarmAsync(client, includeDetails: true);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var grandparentId = await CreateBirdAsync(client, speciesId, "Avô registrado", "111111");
        var parentId = await CreateBirdAsync(
            client,
            speciesId,
            "Mãe registrada",
            "222222",
            motherBirdId: grandparentId);
        var birdId = await CreateBirdAsync(
            client,
            speciesId,
            "Ave certificada",
            "333333",
            motherBirdId: parentId);

        using var response = await GenerateAsync(client, birdId, new
        {
            type = "GenealogyCertificate"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var responseBody = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = responseBody.RootElement;
        var documentId = root.GetProperty("documentId").GetGuid();
        var downloadUrl = root.GetProperty("downloadUrl").GetString();
        Assert.Equal("GenealogyCertificate", root.GetProperty("type").GetString());
        Assert.Equal("Institutional", root.GetProperty("modelId").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("printSize").ValueKind);
        Assert.Empty(root.GetProperty("selectedFields").EnumerateArray());

        using var selectedModelResponse = await GenerateAsync(client, birdId, new
        {
            type = "GenealogyCertificate",
            modelId = "Modern"
        });
        Assert.Equal(HttpStatusCode.Created, selectedModelResponse.StatusCode);
        using var selectedModelBody = JsonDocument.Parse(await selectedModelResponse.Content.ReadAsStreamAsync());
        var selectedModelRoot = selectedModelBody.RootElement;
        var selectedModelDocumentId = selectedModelRoot.GetProperty("documentId").GetGuid();
        Assert.Equal("Modern", selectedModelRoot.GetProperty("modelId").GetString());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var document = await dbContext.BirdDocuments.SingleAsync(candidate => candidate.Id == documentId);
            Assert.Equal(BirdDocumentType.GenealogyCertificate, document.Type);
            Assert.Null(document.ModelId);
            Assert.Equal(GenealogyCertificateModelId.Institutional, document.CertificateModelId);
            Assert.Null(document.PrintSize);
            Assert.Empty(JsonDocument.Parse(document.SelectedFieldsJson).RootElement.EnumerateArray());
            using var snapshot = JsonDocument.Parse(document.SnapshotJson);
            var snapshotRoot = snapshot.RootElement;
            Assert.Equal("Owner Principal", snapshotRoot.GetProperty("breedingFarmDetails").GetProperty("responsibleName").GetString());
            Assert.Equal("owner@example.com", snapshotRoot.GetProperty("breedingFarmDetails").GetProperty("contactEmail").GetString());
            Assert.Equal("REG-001", snapshotRoot.GetProperty("breedingFarmDetails").GetProperty("officialRegistrationNumber").GetString());
            var genealogy = snapshotRoot.GetProperty("genealogy").EnumerateArray().ToArray();
            Assert.Contains(genealogy, node => node.GetProperty("name").GetString() == "Mãe registrada");
            Assert.Contains(genealogy, node => node.GetProperty("name").GetString() == "Avô registrado");
            Assert.StartsWith("CV-GEN-", snapshotRoot.GetProperty("internalDocumentIdentifier").GetString(), StringComparison.Ordinal);
            Assert.True(File.Exists(GetPhysicalPath(storage.RootPath, farmId, document.ObjectKey)));

            var selectedModelDocument = await dbContext.BirdDocuments.SingleAsync(candidate => candidate.Id == selectedModelDocumentId);
            Assert.Equal(GenealogyCertificateModelId.Modern, selectedModelDocument.CertificateModelId);

            var legacyTypeException = await Assert.ThrowsAsync<PostgresException>(() =>
                dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO app.bird_documents
                        ("Id", "BirdId", "CreatedByBreedingFarmId", "Type", "ModelId", "PrintSize",
                         "ObjectKey", "FileName", "ContentType", "Length", "GeneratedAtUtc",
                         "SelectedFieldsJson", "SnapshotJson", "CreatedAtUtc", "UpdatedAtUtc")
                    VALUES
                        ({Guid.NewGuid()}, {birdId}, {farmId}, {2}, {null}, {null},
                         {"birds/{birdId:N}/documents/legacy.pdf"}, {"legacy.pdf"}, {"application/pdf"}, {1L},
                         {DateTimeOffset.UtcNow}, {"[]"}::jsonb, {"{}"}::jsonb,
                         {DateTimeOffset.UtcNow}, {DateTimeOffset.UtcNow})
                    """));
            Assert.Equal("23514", legacyTypeException.SqlState);
        }

        using var download = await client.GetAsync(downloadUrl);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        var pdf = await download.Content.ReadAsStringAsync();
        Assert.Equal("%PDF-1.4", pdf[..8]);
        Assert.Contains(ToHex("CERTIFICADO DE GENEALOGIA"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("ARVORE GENEALOGICA"), pdf, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateProvenanceDocumentPersistsOptionalAbsenceAndReemission()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "provenance-document-owner@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var grandparentId = await CreateBirdAsync(client, speciesId, "Avô de procedência", "111111");
        var parentId = await CreateBirdAsync(
            client,
            speciesId,
            "Mãe de procedência",
            "222222",
            motherBirdId: grandparentId);
        var birdId = await CreateBirdAsync(
            client,
            speciesId,
            "Ave de procedência",
            "333333",
            motherBirdId: parentId);

        var first = await GenerateAsync(client, birdId, new { type = "ProvenanceDocument" });
        var second = await GenerateAsync(client, birdId, new { type = "ProvenanceDocument" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        using var firstBody = JsonDocument.Parse(await first.Content.ReadAsStreamAsync());
        using var secondBody = JsonDocument.Parse(await second.Content.ReadAsStreamAsync());
        var firstRoot = firstBody.RootElement;
        var secondRoot = secondBody.RootElement;
        var firstDocumentId = firstRoot.GetProperty("documentId").GetGuid();
        var secondDocumentId = secondRoot.GetProperty("documentId").GetGuid();
        Assert.NotEqual(firstDocumentId, secondDocumentId);
        Assert.Equal("ProvenanceDocument", firstRoot.GetProperty("type").GetString());
        Assert.Equal("ProvenanceDocument", secondRoot.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, firstRoot.GetProperty("modelId").ValueKind);
        Assert.Equal(JsonValueKind.Null, firstRoot.GetProperty("printSize").ValueKind);
        Assert.Empty(firstRoot.GetProperty("selectedFields").EnumerateArray());

        string firstSnapshotJson;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var documents = await dbContext.BirdDocuments
                .Where(document => document.BirdId == birdId)
                .OrderBy(document => document.CreatedAtUtc)
                .ToArrayAsync();
            Assert.Equal(2, documents.Length);
            Assert.All(documents, document =>
            {
                Assert.Equal(farmId, document.CreatedByBreedingFarmId);
                Assert.Equal(BirdDocumentType.ProvenanceDocument, document.Type);
                Assert.Null(document.ModelId);
                Assert.Null(document.PrintSize);
                Assert.True(File.Exists(GetPhysicalPath(storage.RootPath, farmId, document.ObjectKey)));
            });

            var firstDocument = documents.Single(document => document.Id == firstDocumentId);
            firstSnapshotJson = firstDocument.SnapshotJson;
            using var snapshot = JsonDocument.Parse(firstDocument.SnapshotJson);
            var snapshotRoot = snapshot.RootElement;
            Assert.Equal("333333", snapshotRoot.GetProperty("ringNumber").GetString());
            Assert.Equal(JsonValueKind.String, snapshotRoot.GetProperty("issuedAtUtc").ValueKind);
            var farmSnapshot = snapshotRoot.GetProperty("breedingFarmDetails");
            Assert.Equal("Owner Principal", farmSnapshot.GetProperty("responsibleName").GetString());
            Assert.Equal("owner@example.com", farmSnapshot.GetProperty("contactEmail").GetString());
            Assert.Equal(JsonValueKind.Null, farmSnapshot.GetProperty("contactPhone").ValueKind);
            Assert.Equal(JsonValueKind.Null, farmSnapshot.GetProperty("officialRegistrationNumber").ValueKind);
            var genealogy = snapshotRoot.GetProperty("genealogy").EnumerateArray().ToArray();
            Assert.Contains(genealogy, node => node.GetProperty("name").GetString() == "Mãe de procedência");
            Assert.Contains(genealogy, node => node.GetProperty("name").GetString() == "Avô de procedência");

            var secondDocument = documents.Single(document => document.Id == secondDocumentId);
            Assert.NotEqual(firstDocument.ObjectKey, secondDocument.ObjectKey);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var original = await dbContext.BirdDocuments.SingleAsync(document => document.Id == firstDocumentId);
            Assert.Equal(firstSnapshotJson, original.SnapshotJson);
        }

        using var firstDownload = await client.GetAsync(firstRoot.GetProperty("downloadUrl").GetString());
        using var secondDownload = await client.GetAsync(secondRoot.GetProperty("downloadUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, firstDownload.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondDownload.StatusCode);
        var firstPdf = await firstDownload.Content.ReadAsStringAsync();
        var secondPdf = await secondDownload.Content.ReadAsStringAsync();
        Assert.Equal("%PDF-1.4", firstPdf[..8]);
        Assert.Equal("%PDF-1.4", secondPdf[..8]);
        Assert.Contains("444F43554D454E544F2044452050524F434544454E434941", firstPdf, StringComparison.Ordinal);
        Assert.Contains("4445434C41524143414F2044452050524F434544454E434941", firstPdf, StringComparison.Ordinal);
        Assert.Contains("6E616F2073756273746974756920726567697374726F732C206465636C617261636F6573206F752070726F636564696D656E746F73206F6669636961697320646F20534953504153532F4942414D412E", firstPdf, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateRejectsInvalidConfigurationAndMissingBadgeIdentification()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "documents-validation-owner@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(client, speciesId, "Sem anilha", null);

        using var missingRing = await GenerateAsync(client, birdId, new
        {
            type = "Badge",
            modelId = "Classic",
            printSize = "Small",
            selectedFields = new[] { "Name" }
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingRing.StatusCode);

        using var missingCertificateRing = await GenerateAsync(client, birdId, new
        {
            type = "GenealogyCertificate"
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingCertificateRing.StatusCode);

        using var missingProvenanceRing = await GenerateAsync(client, birdId, new
        {
            type = "ProvenanceDocument"
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingProvenanceRing.StatusCode);

        using var unsupportedType = await GenerateAsync(client, birdId, new
        {
            type = "InternalRecord",
        });
        Assert.Equal(HttpStatusCode.BadRequest, unsupportedType.StatusCode);
    }

    [Fact]
    public async Task GenerateAndDownloadAreTenantScopedAndGenerationIsOwnerOnly()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "documents-tenant-owner@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);
        await RegisterAndAuthenticateAsync(factory, otherClient, "documents-tenant-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient);
        await SelectFarmAsync(otherClient, otherFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(ownerClient, speciesId, "Ave privada", "654321");

        var documentRequest = new
        {
            type = "Badge",
            modelId = "Minimalist",
            printSize = "Small",
            selectedFields = new[] { "Name", "RingNumber" }
        };

        using var foreignProvenanceGeneration = await GenerateAsync(otherClient, birdId, new
        {
            type = "ProvenanceDocument"
        });
        Assert.Equal(HttpStatusCode.NotFound, foreignProvenanceGeneration.StatusCode);

        using var foreignGeneration = await GenerateAsync(otherClient, birdId, documentRequest);
        Assert.Equal(HttpStatusCode.NotFound, foreignGeneration.StatusCode);

        using var generated = await GenerateAsync(ownerClient, birdId, documentRequest);
        Assert.Equal(HttpStatusCode.Created, generated.StatusCode);
        using var generatedBody = JsonDocument.Parse(await generated.Content.ReadAsStreamAsync());
        var documentId = generatedBody.RootElement.GetProperty("documentId").GetGuid();

        using var foreignDownload = await otherClient.GetAsync(
            $"/api/birds/{birdId}/documents/{documentId}/content");
        Assert.Equal(HttpStatusCode.NotFound, foreignDownload.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var otherUser = await dbContext.Users.SingleAsync(
                candidate => candidate.Email == "documents-tenant-other@example.com");
            otherUser.SelectedBreedingFarmId = ownerFarmId;
            dbContext.BreedingFarmUsers.Add(new BreedingFarmUser(
                ownerFarmId,
                otherUser.Id,
                BreedingFarmRole.Viewer,
                DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync();
        }

        using var nonOwnerGeneration = await GenerateAsync(otherClient, birdId, documentRequest);
        Assert.Equal(HttpStatusCode.NotFound, nonOwnerGeneration.StatusCode);
    }

    private static async Task<HttpResponseMessage> GenerateAsync(
        HttpClient client,
        Guid birdId,
        object request)
    {
        return await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/birds/{birdId}/documents",
            await GetAntiforgeryTokenAsync(client),
            request));
    }

    private static async Task<Guid> CreateBirdAsync(
        HttpClient client,
        Guid speciesId,
        string name,
        string? ringNumber,
        Guid? fatherBirdId = null,
        Guid? motherBirdId = null)
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
                ringNumber,
                fatherBirdId,
                motherBirdId
            }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("birdId").GetGuid();
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

    private static async Task<Guid> CreateFarmAsync(HttpClient client, bool includeDetails = false)
    {
        object request = includeDetails
            ? new
            {
                name = "Sítio Aurora",
                responsibleName = "Owner Principal",
                contactEmail = "owner@example.com",
                contactPhone = "+55 11 99999-0000",
                officialRegistrationNumber = "REG-001"
            }
            : new
            {
                name = "Sítio Aurora",
                responsibleName = "Owner Principal",
                contactEmail = "owner@example.com"
            };
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/breeding-farms",
            await GetAntiforgeryTokenAsync(client),
            request));
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

    private static string ToHex(string value) =>
        Convert.ToHexString(System.Text.Encoding.ASCII.GetBytes(value));

    private sealed class TemporaryStorage : IAsyncDisposable
    {
        public TemporaryStorage() =>
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "CriatorioVirtualDocumentTests",
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
