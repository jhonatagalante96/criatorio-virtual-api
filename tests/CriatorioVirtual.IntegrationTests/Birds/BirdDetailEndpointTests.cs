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

public sealed class BirdDetailEndpointTests
{
    [Fact]
    public async Task GetReturnsPrivateBirdDetailsIncludingSpeciesAndLinkedParents()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "bird-detail-owner@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);

        var fatherId = await CreateBirdAsync(
            client,
            new
            {
                name = "Pai Azul",
                sex = "Male",
                speciesId,
                birthDate = "2018-06-01",
                ringNumber = "930001"
            });
        var motherId = await CreateBirdAsync(
            client,
            new
            {
                name = "Mãe Rubi",
                sex = "Female",
                speciesId,
                birthDate = "2019-07-01",
                ringNumber = "930002"
            });
        var birdId = await CreateBirdAsync(
            client,
            new
            {
                name = "Filhote Aurora",
                sex = "Female",
                speciesId,
                birthDate = "2020-09-07",
                ringNumber = "930003",
                fatherBirdId = fatherId,
                motherBirdId = motherId,
                notes = "Acompanhamento inicial"
            });

        using var response = await client.GetAsync($"/api/birds/{birdId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        Assert.Equal(birdId, root.GetProperty("birdId").GetGuid());
        Assert.NotEqual(Guid.Empty, root.GetProperty("genealogyRootId").GetGuid());
        Assert.Equal(farmId, root.GetProperty("breedingFarmId").GetGuid());
        Assert.Equal("Filhote Aurora", root.GetProperty("name").GetString());
        Assert.Equal(speciesId, root.GetProperty("speciesId").GetGuid());
        Assert.Equal("Turdus rufiventris", root.GetProperty("speciesScientificName").GetString());
        Assert.Equal("Sabiá-laranjeira", root.GetProperty("speciesPopularName").GetString());
        Assert.Equal("Female", root.GetProperty("sex").GetString());
        Assert.Equal("2020-09-07", root.GetProperty("birthDate").GetString());
        Assert.Equal("930003", root.GetProperty("ringNumber").GetString());
        Assert.Equal(fatherId, root.GetProperty("fatherBirdId").GetGuid());
        Assert.Equal("Pai Azul", root.GetProperty("father").GetProperty("name").GetString());
        Assert.Equal("Male", root.GetProperty("father").GetProperty("sex").GetString());
        Assert.Equal(motherId, root.GetProperty("motherBirdId").GetGuid());
        Assert.Equal("Mãe Rubi", root.GetProperty("mother").GetProperty("name").GetString());
        Assert.Equal("Female", root.GetProperty("mother").GetProperty("sex").GetString());
        Assert.Equal("Acompanhamento inicial", root.GetProperty("notes").GetString());
        Assert.Equal("Active", root.GetProperty("status").GetString());
        Assert.False(root.GetProperty("identificationPending").GetBoolean());
        Assert.Equal(
            CalculateAgeInYears(new DateOnly(2020, 9, 7), DateOnly.FromDateTime(DateTime.UtcNow)),
            root.GetProperty("ageInYears").GetInt32());
        Assert.True(root.GetProperty("createdAtUtc").GetDateTimeOffset() <= DateTimeOffset.UtcNow);
        Assert.True(root.GetProperty("updatedAtUtc").GetDateTimeOffset() <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task GetRejectsAuthenticationSelectionAndMissingBirds()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        var missingBirdId = Guid.NewGuid();

        using var unauthenticated = await client.GetAsync($"/api/birds/{missingBirdId}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await RegisterAndAuthenticateAsync(factory, client, "bird-detail-invalid-owner@example.com");
        using var withoutFarm = await client.GetAsync($"/api/birds/{missingBirdId}");
        Assert.Equal(HttpStatusCode.Conflict, withoutFarm.StatusCode);

        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        using var missing = await client.GetAsync($"/api/birds/{missingBirdId}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using var emptyId = await client.GetAsync($"/api/birds/{Guid.Empty}");
        Assert.Equal(HttpStatusCode.NotFound, emptyId.StatusCode);
    }

    [Fact]
    public async Task GetNeverExposesBirdsFromAnotherTenantOrSelectedForeignFarm()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        var ownerUserId = await RegisterAndAuthenticateAsync(factory, ownerClient, "bird-detail-tenant-owner@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);
        await RegisterAndAuthenticateAsync(factory, otherClient, "bird-detail-tenant-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient);
        await SelectFarmAsync(otherClient, otherFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var ownerBirdId = await CreateBirdAsync(
            ownerClient,
            new
            {
                name = "Ave privada",
                sex = "Unknown",
                speciesId,
                birthDate = "2020-09-07",
                ringNumber = (string?)null
            });

        using var foreignBird = await otherClient.GetAsync($"/api/birds/{ownerBirdId}");
        Assert.Equal(HttpStatusCode.NotFound, foreignBird.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var owner = await dbContext.Users.SingleAsync(candidate => candidate.Id == ownerUserId);
            owner.SelectedBreedingFarmId = otherFarmId;
            await dbContext.SaveChangesAsync();
        }

        using var foreignSelection = await ownerClient.GetAsync($"/api/birds/{ownerBirdId}");
        Assert.Equal(HttpStatusCode.NotFound, foreignSelection.StatusCode);
    }

    [Fact]
    public async Task GetReturnsExternalParentsWithoutLinkedBirdDetails()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "bird-detail-external-owner@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(
            client,
            new
            {
                name = "Ave com ancestral externo",
                sex = "Unknown",
                speciesId,
                birthDate = "2020-09-07",
                ringNumber = (string?)null,
                externalFatherName = "Pai não cadastrado",
                externalMotherName = "Mãe não cadastrada"
            });

        using var response = await client.GetAsync($"/api/birds/{birdId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("fatherBirdId").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("father").ValueKind);
        Assert.Equal("Pai não cadastrado", root.GetProperty("externalFatherName").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("motherBirdId").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("mother").ValueKind);
        Assert.Equal("Mãe não cadastrada", root.GetProperty("externalMotherName").GetString());
        Assert.True(root.GetProperty("identificationPending").GetBoolean());
    }

    private static async Task<Guid> CreateBirdAsync(
        HttpClient client,
        object request)
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

    private static int CalculateAgeInYears(DateOnly birthDate, DateOnly today)
    {
        var age = today.Year - birthDate.Year;
        if (birthDate.AddYears(age) > today)
        {
            age--;
        }

        return age;
    }
}
