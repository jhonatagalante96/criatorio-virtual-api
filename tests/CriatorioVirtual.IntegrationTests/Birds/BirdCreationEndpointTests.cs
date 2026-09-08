using System.Net;
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

public sealed class BirdCreationEndpointTests
{
    [Fact]
    public async Task CreateRequiresAuthenticationAndASelectedBreedingFarm()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        var speciesId = await GetSpeciesIdAsync(factory);

        using var unauthenticated = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/birds",
            await GetAntiforgeryTokenAsync(client),
            ValidRequest(speciesId)));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await RegisterAndAuthenticateAsync(factory, client, "bird-owner@example.com");
        using var withoutFarm = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/birds",
            await GetAntiforgeryTokenAsync(client),
            ValidRequest(speciesId)));

        Assert.Equal(HttpStatusCode.Conflict, withoutFarm.StatusCode);
    }

    [Fact]
    public async Task CreatePersistsBirdWithActiveStatusAgeAndIdentificationState()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        var userId = await RegisterAndAuthenticateAsync(factory, client, "bird-owner@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birthDate = new DateOnly(2020, 9, 7);

        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/birds",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                name = "Aurora",
                sex = "Female",
                speciesId,
                birthDate = birthDate.ToString("yyyy-MM-dd"),
                ringNumber = "123456",
                externalFatherName = "Pai externo",
                notes = "Ave matriz"
            }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var birdId = document.RootElement.GetProperty("birdId").GetGuid();
        Assert.Equal(farmId, document.RootElement.GetProperty("breedingFarmId").GetGuid());
        Assert.Equal(userId, await GetFarmOwnerIdAsync(factory, farmId));
        Assert.Equal("Aurora", document.RootElement.GetProperty("name").GetString());
        Assert.Equal("Female", document.RootElement.GetProperty("sex").GetString());
        Assert.Equal("123456", document.RootElement.GetProperty("ringNumber").GetString());
        Assert.Equal("Active", document.RootElement.GetProperty("status").GetString());
        Assert.False(document.RootElement.GetProperty("identificationPending").GetBoolean());
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var expectedAge = today.Year - birthDate.Year - (today < birthDate.AddYears(today.Year - birthDate.Year) ? 1 : 0);
        Assert.Equal(expectedAge, document.RootElement.GetProperty("ageInYears").GetInt32());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var bird = await dbContext.Birds.SingleAsync(candidate => candidate.Id == birdId);
        Assert.Equal(farmId, bird.BreedingFarmId);
        Assert.Equal(BirdStatus.Active, bird.Status);
        Assert.Equal(BirdSex.Female, bird.Sex);
        Assert.True(bird.IdentificationPending is false);
    }

    [Fact]
    public async Task CreateRejectsInvalidInputAndInactiveSpecies()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "bird-owner@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var inactiveSpeciesId = await AddInactiveSpeciesAsync(factory);
        var futureBirthDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        using var invalid = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/birds",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                name = " ",
                sex = "Unknown",
                speciesId,
                birthDate = futureBirthDate.ToString("yyyy-MM-dd"),
                ringNumber = "12A"
            }));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var invalidBody = await invalid.Content.ReadAsStringAsync();
        Assert.Contains("Name", invalidBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BirthDate", invalidBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RingNumber", invalidBody, StringComparison.OrdinalIgnoreCase);

        using var inactive = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/birds",
            await GetAntiforgeryTokenAsync(client),
            ValidRequest(inactiveSpeciesId)));
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
    }

    [Fact]
    public async Task CreateRejectsCrossTenantParentsAndParentsWithTheWrongSex()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        var ownerUserId = await RegisterAndAuthenticateAsync(factory, ownerClient, "owner@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);
        await RegisterAndAuthenticateAsync(factory, otherClient, "other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient);
        await SelectFarmAsync(otherClient, otherFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var foreignFatherId = await AddBirdAsync(factory, otherFarmId, speciesId, BirdSex.Male, "Foreign father");
        var localFemaleId = await AddBirdAsync(factory, ownerFarmId, speciesId, BirdSex.Female, "Local female");

        using var crossTenant = await ownerClient.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/birds",
            await GetAntiforgeryTokenAsync(ownerClient),
            ValidRequest(speciesId, fatherBirdId: foreignFatherId)));
        Assert.Equal(HttpStatusCode.BadRequest, crossTenant.StatusCode);

        using var wrongSex = await ownerClient.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/birds",
            await GetAntiforgeryTokenAsync(ownerClient),
            ValidRequest(speciesId, fatherBirdId: localFemaleId)));
        Assert.Equal(HttpStatusCode.BadRequest, wrongSex.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Equal(ownerUserId, await GetFarmOwnerIdAsync(factory, ownerFarmId));
        Assert.Equal(1, await dbContext.Birds.CountAsync(candidate => candidate.BreedingFarmId == ownerFarmId));
    }

    [Fact]
    public async Task ConcurrentSameRingNumberCreatesOneBirdAndReturnsOneConflict()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var firstClient = CreateClient(factory);
        using var secondClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, firstClient, "bird-owner@example.com");
        await AuthenticateExistingUserAsync(secondClient, "bird-owner@example.com");
        var farmId = await CreateFarmAsync(firstClient);
        await SelectFarmAsync(firstClient, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);

        var firstRequest = CreateBrowserRequest(
            HttpMethod.Post,
            "/api/birds",
            await GetAntiforgeryTokenAsync(firstClient),
            ValidRequest(speciesId, name: "First", ringNumber: "999999"));
        var secondRequest = CreateBrowserRequest(
            HttpMethod.Post,
            "/api/birds",
            await GetAntiforgeryTokenAsync(secondClient),
            ValidRequest(speciesId, name: "Second", ringNumber: "999999"));

        var responses = await Task.WhenAll(firstClient.SendAsync(firstRequest), secondClient.SendAsync(secondRequest));
        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }

            firstRequest.Dispose();
            secondRequest.Dispose();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Equal(1, await dbContext.Birds.CountAsync(candidate => candidate.BreedingFarmId == farmId));
    }

    private static object ValidRequest(
        Guid speciesId,
        Guid? fatherBirdId = null,
        string? name = "Aurora",
        string? ringNumber = "123456") => new
        {
            name,
            sex = "Female",
            speciesId,
            birthDate = "2020-09-07",
            ringNumber,
            fatherBirdId
        };

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

        await AuthenticateExistingUserAsync(client, email);
        return userId;
    }

    private static async Task AuthenticateExistingUserAsync(HttpClient client, string email)
    {
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

    private static async Task<Guid> AddBirdAsync(
        WebApplicationFactory<Program> factory,
        Guid farmId,
        Guid speciesId,
        BirdSex sex,
        string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var bird = new Bird(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            farmId,
            name,
            speciesId,
            sex,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateOnly.FromDateTime(DateTime.UtcNow));
        dbContext.Birds.Add(bird);
        await dbContext.SaveChangesAsync();
        return bird.Id;
    }

    private static async Task<Guid> GetFarmOwnerIdAsync(WebApplicationFactory<Program> factory, Guid farmId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        return await dbContext.BreedingFarmUsers
            .Where(membership => membership.BreedingFarmId == farmId && membership.Role == CriatorioVirtual.Domain.BreedingFarms.BreedingFarmRole.Owner)
            .Select(membership => membership.UserId)
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
}
