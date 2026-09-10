using System.Net;
using System.Net.Http.Json;
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
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Birds;

public sealed class BirdUpdateEndpointTests
{
    [Fact]
    public async Task UpdateChangesEditableDetailsAndPreservesOwnershipAndGenealogy()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "bird-update-owner@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(
            client,
            new
            {
                name = "Aurora",
                sex = "Female",
                speciesId,
                birthDate = "2020-09-07",
                ringNumber = "123456",
                externalFatherName = "Pai externo",
                notes = "Original"
            });

        using var beforeResponse = await client.GetAsync($"/api/birds/{birdId}");
        Assert.Equal(HttpStatusCode.OK, beforeResponse.StatusCode);
        using var before = JsonDocument.Parse(await beforeResponse.Content.ReadAsStreamAsync());
        var originalRootId = before.RootElement.GetProperty("genealogyRootId").GetGuid();
        var originalCreatedAt = before.RootElement.GetProperty("createdAtUtc").GetDateTimeOffset();
        var originalUpdatedAt = before.RootElement.GetProperty("updatedAtUtc").GetDateTimeOffset();

        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            $"/api/birds/{birdId}",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                name = "  Beatriz  ",
                sex = "Unknown",
                speciesId,
                birthDate = "2021-02-03",
                ringNumber = " 654321 ",
                notes = "  Atualizada  "
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        Assert.Equal(birdId, root.GetProperty("birdId").GetGuid());
        Assert.Equal(farmId, root.GetProperty("breedingFarmId").GetGuid());
        Assert.Equal(originalRootId, root.GetProperty("genealogyRootId").GetGuid());
        Assert.Equal("Beatriz", root.GetProperty("name").GetString());
        Assert.Equal("Unknown", root.GetProperty("sex").GetString());
        Assert.Equal("2021-02-03", root.GetProperty("birthDate").GetString());
        Assert.Equal("654321", root.GetProperty("ringNumber").GetString());
        Assert.Equal("Atualizada", root.GetProperty("notes").GetString());
        Assert.Equal("Active", root.GetProperty("status").GetString());
        Assert.False(root.GetProperty("identificationPending").GetBoolean());
        Assert.Equal(originalCreatedAt, root.GetProperty("createdAtUtc").GetDateTimeOffset());
        Assert.True(root.GetProperty("updatedAtUtc").GetDateTimeOffset() > originalUpdatedAt);

        using var detailsResponse = await client.GetAsync($"/api/birds/{birdId}");
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        using var details = JsonDocument.Parse(await detailsResponse.Content.ReadAsStreamAsync());
        Assert.Equal("Pai externo", details.RootElement.GetProperty("externalFatherName").GetString());
        Assert.Equal(JsonValueKind.Null, details.RootElement.GetProperty("fatherBirdId").ValueKind);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var bird = await dbContext.Birds.SingleAsync(candidate => candidate.Id == birdId);
        Assert.Equal(farmId, bird.BreedingFarmId);
        Assert.Equal(BirdStatus.Active, bird.Status);
        Assert.Equal(BirdSex.Unknown, bird.Sex);
        Assert.Equal("654321", bird.RingNumber);
        Assert.Equal("Atualizada", bird.Notes);
        Assert.Equal(originalRootId, await dbContext.GenealogyNodes
            .Where(candidate => candidate.BirdId == birdId && candidate.IsRoot)
            .Select(candidate => candidate.Id)
            .SingleAsync());
    }

    [Fact]
    public async Task UpdateClearsAndRestoresIdentificationPending()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "bird-update-identification@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(
            client,
            new
            {
                name = "Sem anilha",
                sex = "Unknown",
                speciesId,
                birthDate = "2020-09-07",
                ringNumber = "123456"
            });

        using var clearResponse = await UpdateAsync(client, birdId, speciesId, ringNumber: null);
        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);
        using var cleared = JsonDocument.Parse(await clearResponse.Content.ReadAsStreamAsync());
        Assert.Equal(JsonValueKind.Null, cleared.RootElement.GetProperty("ringNumber").ValueKind);
        Assert.True(cleared.RootElement.GetProperty("identificationPending").GetBoolean());

        using var setResponse = await UpdateAsync(client, birdId, speciesId, ringNumber: "654321");
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);
        using var identified = JsonDocument.Parse(await setResponse.Content.ReadAsStreamAsync());
        Assert.Equal("654321", identified.RootElement.GetProperty("ringNumber").GetString());
        Assert.False(identified.RootElement.GetProperty("identificationPending").GetBoolean());
    }

    [Fact]
    public async Task UpdateRequiresAuthenticationSelectionAndExistingBird()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = Guid.NewGuid();

        using var unauthenticated = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            $"/api/birds/{birdId}",
            await GetAntiforgeryTokenAsync(client),
            ValidUpdate(speciesId)));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await RegisterAndAuthenticateAsync(factory, client, "bird-update-no-farm@example.com");
        using var withoutFarm = await UpdateAsync(client, birdId, speciesId);
        Assert.Equal(HttpStatusCode.Conflict, withoutFarm.StatusCode);

        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        using var missing = await UpdateAsync(client, birdId, speciesId);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task UpdateRejectsInvalidDataInactiveSpeciesAndLeavesBirdUnchanged()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "bird-update-validation@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var inactiveSpeciesId = await AddInactiveSpeciesAsync(factory);
        var birdId = await CreateBirdAsync(
            client,
            new
            {
                name = "Original",
                sex = "Female",
                speciesId,
                birthDate = "2020-09-07",
                ringNumber = "123456",
                notes = "Preservada"
            });

        var invalidRequests = new object[]
        {
            new { name = " ", sex = "Female", speciesId, birthDate = "2020-09-07", ringNumber = "123456", notes = "Preservada" },
            new { name = "Atual", sex = (string?)null, speciesId, birthDate = "2020-09-07", ringNumber = "123456", notes = "Preservada" },
            new { name = "Atual", sex = "1", speciesId, birthDate = "2020-09-07", ringNumber = "123456", notes = "Preservada" },
            new { name = "Atual", sex = "Female", speciesId = Guid.Empty, birthDate = "2020-09-07", ringNumber = "123456", notes = "Preservada" },
            new { name = "Atual", sex = "Female", speciesId, birthDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)).ToString("yyyy-MM-dd"), ringNumber = "123456", notes = "Preservada" },
            new { name = "Atual", sex = "Female", speciesId, birthDate = "2020-09-07", ringNumber = "12345A", notes = "Preservada" },
            new { name = "Atual", sex = "Female", speciesId, birthDate = "2020-09-07", ringNumber = "123456", notes = new string('N', 2001) }
        };

        foreach (var invalidRequest in invalidRequests)
        {
            using var response = await client.SendAsync(CreateBrowserRequest(
                HttpMethod.Put,
                $"/api/birds/{birdId}",
                await GetAntiforgeryTokenAsync(client),
                invalidRequest));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var inactive = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            $"/api/birds/{birdId}",
            await GetAntiforgeryTokenAsync(client),
            ValidUpdate(inactiveSpeciesId, name: "Atual")));
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);

        using var details = await client.GetAsync($"/api/birds/{birdId}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        using var document = JsonDocument.Parse(await details.Content.ReadAsStreamAsync());
        Assert.Equal("Original", document.RootElement.GetProperty("name").GetString());
        Assert.Equal("123456", document.RootElement.GetProperty("ringNumber").GetString());
        Assert.Equal("Preservada", document.RootElement.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task UpdateRejectsDuplicateRingNumberAcrossTenants()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "bird-update-duplicate-owner@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);
        await RegisterAndAuthenticateAsync(factory, otherClient, "bird-update-duplicate-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient);
        await SelectFarmAsync(otherClient, otherFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var ownerBirdId = await CreateBirdAsync(
            ownerClient,
            new { name = "Owner bird", sex = "Female", speciesId, birthDate = "2020-09-07", ringNumber = "111111" });
        await CreateBirdAsync(
            otherClient,
            new { name = "Other bird", sex = "Male", speciesId, birthDate = "2020-09-07", ringNumber = "222222" });

        using var response = await UpdateAsync(ownerClient, ownerBirdId, speciesId, ringNumber: "222222");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var details = await ownerClient.GetAsync($"/api/birds/{ownerBirdId}");
        using var document = JsonDocument.Parse(await details.Content.ReadAsStreamAsync());
        Assert.Equal("111111", document.RootElement.GetProperty("ringNumber").GetString());
    }

    [Fact]
    public async Task UpdateNeverChangesOrExposesAnotherTenantBird()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        var ownerUserId = await RegisterAndAuthenticateAsync(factory, ownerClient, "bird-update-tenant-owner@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);
        await RegisterAndAuthenticateAsync(factory, otherClient, "bird-update-tenant-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient);
        await SelectFarmAsync(otherClient, otherFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var ownerBirdId = await CreateBirdAsync(
            ownerClient,
            new { name = "Privada", sex = "Female", speciesId, birthDate = "2020-09-07", ringNumber = "333333" });

        using var crossTenant = await UpdateAsync(otherClient, ownerBirdId, speciesId, name: "Não autorizada");
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var owner = await dbContext.Users.SingleAsync(candidate => candidate.Id == ownerUserId);
            owner.SelectedBreedingFarmId = otherFarmId;
            await dbContext.SaveChangesAsync();
        }

        using var foreignSelection = await UpdateAsync(ownerClient, ownerBirdId, speciesId, name: "Também não");
        Assert.Equal(HttpStatusCode.NotFound, foreignSelection.StatusCode);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var bird = await verificationDb.Birds.SingleAsync(candidate => candidate.Id == ownerBirdId);
        Assert.Equal(ownerFarmId, bird.BreedingFarmId);
        Assert.Equal("Privada", bird.Name);
        Assert.Equal("333333", bird.RingNumber);
    }

    private static async Task<HttpResponseMessage> UpdateAsync(
        HttpClient client,
        Guid birdId,
        Guid speciesId,
        string? name = "Atualizada",
        string? ringNumber = "654321") =>
        await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            $"/api/birds/{birdId}",
            await GetAntiforgeryTokenAsync(client),
            ValidUpdate(speciesId, name, ringNumber)));

    private static object ValidUpdate(
        Guid speciesId,
        string? name = "Atualizada",
        string? ringNumber = "654321") => new
        {
            name,
            sex = "Female",
            speciesId,
            birthDate = "2020-09-07",
            ringNumber,
            notes = "Atualizada"
        };

    private static async Task<Guid> CreateBirdAsync(HttpClient client, object request)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/birds",
            await GetAntiforgeryTokenAsync(client),
            request));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("birdId").GetGuid();
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:EventLog:LogLevel:Default"] = "None"
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

    private static async Task<Guid> AddInactiveSpeciesAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var species = new CriatorioVirtual.Domain.Species.Species(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "Species inactiveus",
            "Espécie inativa",
            isActive: false);
        dbContext.Species.Add(species);
        await dbContext.SaveChangesAsync();
        return species.Id;
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
}
