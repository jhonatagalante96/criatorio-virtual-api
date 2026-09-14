using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Domain.Birds;
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

public sealed class BadgeBatchEndpointTests
{
    [Fact]
    public async Task GenerateBadgeBatch_ReturnsPartialResultsAndPersistsOnlySuccessfulDocuments()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "badge-batch-owner@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient, "Batch owner farm");
        await SelectFarmAsync(ownerClient, ownerFarmId);
        await RegisterAndAuthenticateAsync(factory, otherClient, "badge-batch-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient, "Batch other farm");
        await SelectFarmAsync(otherClient, otherFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);

        var firstBirdId = await CreateBirdAsync(ownerClient, speciesId, "Batch First", "123401");
        var missingRingBirdId = await CreateBirdAsync(ownerClient, speciesId, "Missing Ring", null);
        var secondBirdId = await CreateBirdAsync(ownerClient, speciesId, "Batch Second", "123402");
        var foreignBirdId = await CreateBirdAsync(otherClient, speciesId, "Foreign Bird", "223401");

        using var response = await ownerClient.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/birds/documents/batch",
            await GetAntiforgeryTokenAsync(ownerClient),
            new
            {
                birdIds = new[] { firstBirdId, missingRingBirdId, foreignBirdId, secondBirdId },
                modelId = "Classic",
                printSize = "Small",
                selectedFields = new[] { "Name", "RingNumber", "GenealogyTree" }
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;
        Assert.Equal("Generated", root.GetProperty("status").GetString());
        Assert.Equal(ownerFarmId, root.GetProperty("breedingFarmId").GetGuid());

        var items = root.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(4, items.Length);
        Assert.Equal(firstBirdId, items[0].GetProperty("birdId").GetGuid());
        Assert.Equal("Generated", items[0].GetProperty("status").GetString());
        Assert.Equal("MissingRingNumber", items[1].GetProperty("status").GetString());
        Assert.Equal("missing_ring_number", items[1].GetProperty("errorCode").GetString());
        Assert.Equal("BirdNotFound", items[2].GetProperty("status").GetString());
        Assert.Equal("bird_not_found", items[2].GetProperty("errorCode").GetString());
        Assert.Equal(secondBirdId, items[3].GetProperty("birdId").GetGuid());

        var aggregate = root.GetProperty("aggregatePdf");
        Assert.Equal("application/pdf", aggregate.GetProperty("contentType").GetString());
        Assert.Equal(4, aggregate.GetProperty("pageCount").GetInt32());
        var aggregatePdf = Encoding.ASCII.GetString(
            Convert.FromBase64String(aggregate.GetProperty("contentBase64").GetString()!));
        Assert.StartsWith("%PDF-1.", aggregatePdf, StringComparison.Ordinal);
        AssertPdfContains(aggregatePdf, "Batch First", "Batch Second");
        AssertPdfDoesNotContain(aggregatePdf, "Missing Ring", "Foreign Bird");

        var generatedDocumentIds = items
            .Where(item => item.GetProperty("status").GetString() == "Generated")
            .Select(item => item.GetProperty("document").GetProperty("documentId").GetGuid())
            .ToArray();
        Assert.Equal(2, generatedDocumentIds.Length);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Equal(2, await dbContext.BirdDocuments.CountAsync());
        Assert.All(
            await dbContext.BirdDocuments.ToArrayAsync(),
            document => Assert.Equal(ownerFarmId, document.CreatedByBreedingFarmId));
        Assert.DoesNotContain(
            await dbContext.BirdDocuments.Select(document => document.BirdId).ToArrayAsync(),
            birdId => birdId == missingRingBirdId || birdId == foreignBirdId);
    }

    [Fact]
    public async Task GenerateBadgeBatch_RejectsCrossTenantBirdsWithoutLeakingTheirData()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "badge-batch-isolation-owner@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient, "Private batch farm");
        await SelectFarmAsync(ownerClient, ownerFarmId);
        await RegisterAndAuthenticateAsync(factory, otherClient, "badge-batch-isolation-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient, "Other batch farm");
        await SelectFarmAsync(otherClient, otherFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var privateBirdId = await CreateBirdAsync(ownerClient, speciesId, "Private Batch Bird", "124001");

        using var response = await otherClient.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/birds/documents/batch",
            await GetAntiforgeryTokenAsync(otherClient),
            new
            {
                birdIds = new[] { privateBirdId },
                modelId = "Minimalist",
                printSize = "Medium",
                selectedFields = new[] { "Name", "RingNumber" }
            }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var responseText = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Private Batch Bird", responseText, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(responseText);
        Assert.Equal("NoDocumentsGenerated", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("BirdNotFound", body.RootElement.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("aggregatePdf").ValueKind);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Empty(await dbContext.BirdDocuments.ToArrayAsync());
    }

    [Fact]
    public async Task GenerateBadgeBatch_UsesConfiguredMaximumBirdCount()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(
            database.GetConnectionString(),
            certificate,
            storage.RootPath,
            new Dictionary<string, string?>
            {
                ["Documents:BadgeBatch:MaxBirdCount"] = "1"
            });
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "badge-batch-limit@example.com");
        var farmId = await CreateFarmAsync(client, "Batch limit farm");
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var firstBirdId = await CreateBirdAsync(client, speciesId, "Limit First", "124101");
        var secondBirdId = await CreateBirdAsync(client, speciesId, "Limit Second", "124102");

        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/birds/documents/batch",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                birdIds = new[] { firstBirdId, secondBirdId },
                modelId = "Classic",
                printSize = "Small",
                selectedFields = new[] { "Name" }
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("cannot contain more than 1 birds", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Empty(await dbContext.BirdDocuments.ToArrayAsync());
    }

    [Fact]
    public async Task GenerateBadgeBatch_AllowsConcurrentBatchesWithoutLosingDocuments()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "badge-batch-concurrency@example.com");
        var farmId = await CreateFarmAsync(client, "Batch concurrency farm");
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var firstBirdId = await CreateBirdAsync(client, speciesId, "Concurrent First", "124201");
        var secondBirdId = await CreateBirdAsync(client, speciesId, "Concurrent Second", "124202");
        var requestBody = new
        {
            birdIds = new[] { firstBirdId, secondBirdId },
            modelId = "Photographic",
            printSize = "Small",
            selectedFields = new[] { "Name", "RingNumber" }
        };
        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 2)
                .Select(_ => client.SendAsync(CreateBrowserRequest(
                    HttpMethod.Post,
                    "/api/birds/documents/batch",
                    antiforgeryToken,
                    requestBody))));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        foreach (var response in responses)
        {
            response.Dispose();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Equal(4, await dbContext.BirdDocuments.CountAsync());
        Assert.All(
            await dbContext.BirdDocuments.ToArrayAsync(),
            document => Assert.Equal(farmId, document.CreatedByBreedingFarmId));
    }

    private static void AssertPdfContains(string pdf, params string[] values)
    {
        foreach (var value in values)
        {
            Assert.Contains(ToPdfHex(value), pdf, StringComparison.Ordinal);
        }
    }

    private static void AssertPdfDoesNotContain(string pdf, params string[] values)
    {
        foreach (var value in values)
        {
            Assert.DoesNotContain(ToPdfHex(value), pdf, StringComparison.Ordinal);
        }
    }

    private static string ToPdfHex(string value) =>
        Convert.ToHexString(Encoding.ASCII.GetBytes(
            value.Normalize(NormalizationForm.FormD)
                .Where(character => char.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.NonSpacingMark)
                .Where(character => character <= 127)
                .ToArray()));

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate,
        string storageRootPath,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        var configuration = new Dictionary<string, string?>
        {
            ["Logging:EventLog:LogLevel:Default"] = "None",
            ["Storage:PrivateRootPath"] = storageRootPath
        };
        if (settings is not null)
        {
            foreach (var (key, value) in settings)
            {
                configuration[key] = value;
            }
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(configuration));
            builder.ConfigureServices(services => services.AddInfrastructurePersistence(connectionString, certificate));
        });
    }

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

    private static async Task<Guid> CreateFarmAsync(HttpClient client, string name)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/breeding-farms",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                name,
                responsibleName = "Batch Owner",
                contactEmail = $"{name.Replace(' ', '.').ToLowerInvariant()}@example.com"
            }));
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

    private static async Task<Guid> CreateBirdAsync(
        HttpClient client,
        Guid speciesId,
        string name,
        string? ringNumber)
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
                fatherBirdId = (Guid?)null,
                externalFatherName = (string?)null,
                externalFatherSex = (string?)null,
                motherBirdId = (Guid?)null,
                externalMotherName = (string?)null,
                externalMotherSex = (string?)null,
                notes = (string?)null
            }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("birdId").GetGuid();
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

    private sealed class TemporaryStorage : IAsyncDisposable
    {
        public TemporaryStorage() =>
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "CriatorioVirtualBadgeBatchTests",
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
