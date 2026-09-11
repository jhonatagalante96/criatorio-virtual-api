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

namespace CriatorioVirtual.IntegrationTests.Reproductions;

public sealed class ReproductionCreationEndpointTests
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

        using var unauthenticated = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/reproductions",
            await GetAntiforgeryTokenAsync(client),
            ValidRequest()));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await RegisterAndAuthenticateAsync(factory, client, "reproduction-auth@example.com");
        using var withoutFarm = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/reproductions",
            await GetAntiforgeryTokenAsync(client),
            ValidRequest()));

        Assert.Equal(HttpStatusCode.Conflict, withoutFarm.StatusCode);
    }

    [Fact]
    public async Task CreatePersistsAnActiveReproductionForEligibleBirds()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "reproduction-owner@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var maleBirdId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Male, "Macho", "123456");
        var femaleBirdId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Female, "Fêmea", "123457");

        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/reproductions",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                maleBirdId,
                femaleBirdId,
                startDate = "2026-09-01",
                notes = "  Primeiro período  "
            }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var reproductionId = body.RootElement.GetProperty("reproductionId").GetGuid();
        Assert.Equal(farmId, body.RootElement.GetProperty("breedingFarmId").GetGuid());
        Assert.Equal(maleBirdId, body.RootElement.GetProperty("maleBirdId").GetGuid());
        Assert.Equal(femaleBirdId, body.RootElement.GetProperty("femaleBirdId").GetGuid());
        Assert.Equal("2026-09-01", body.RootElement.GetProperty("startDate").GetString());
        Assert.Equal("Primeiro período", body.RootElement.GetProperty("notes").GetString());
        Assert.Equal("Active", body.RootElement.GetProperty("status").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var reproduction = await dbContext.Reproductions.SingleAsync(candidate => candidate.Id == reproductionId);
        Assert.Equal(farmId, reproduction.BreedingFarmId);
        Assert.Equal(maleBirdId, reproduction.MaleBirdId);
        Assert.Equal(femaleBirdId, reproduction.FemaleBirdId);
        Assert.Equal(new DateOnly(2026, 9, 1), reproduction.StartDate);
        Assert.Equal("Primeiro período", reproduction.Notes);
    }

    [Fact]
    public async Task CreateRejectsForeignInactiveAndSexIncompatibleBirdsAndFutureStart()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "reproduction-owner-negative@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);
        await RegisterAndAuthenticateAsync(factory, otherClient, "reproduction-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient);
        await SelectFarmAsync(otherClient, otherFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var localFemaleId = await AddBirdAsync(factory, ownerFarmId, speciesId, BirdSex.Female, "Fêmea local", "223344");
        var foreignMaleId = await AddBirdAsync(factory, otherFarmId, speciesId, BirdSex.Male, "Macho estrangeiro", "223345");
        var inactiveMaleId = await AddBirdAsync(
            factory,
            ownerFarmId,
            speciesId,
            BirdSex.Male,
            "Macho inativo",
            "223346",
            BirdStatus.Archived);
        var femaleAsMaleId = await AddBirdAsync(factory, ownerFarmId, speciesId, BirdSex.Female, "Fêmea no campo macho", "223347");
        var unknownMaleId = await AddBirdAsync(factory, ownerFarmId, speciesId, BirdSex.Unknown, "Sexo desconhecido", "223348");

        using var foreign = await PostAsync(ownerClient, foreignMaleId, localFemaleId, "2026-09-01");
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);

        using var inactive = await PostAsync(ownerClient, inactiveMaleId, localFemaleId, "2026-09-01");
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);

        using var wrongSex = await PostAsync(ownerClient, femaleAsMaleId, localFemaleId, "2026-09-01");
        Assert.Equal(HttpStatusCode.BadRequest, wrongSex.StatusCode);

        using var unknownSex = await PostAsync(ownerClient, unknownMaleId, localFemaleId, "2026-09-01");
        Assert.Equal(HttpStatusCode.BadRequest, unknownSex.StatusCode);

        using var future = await PostAsync(
            ownerClient,
            femaleAsMaleId,
            localFemaleId,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)).ToString("yyyy-MM-dd"));
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Empty(await dbContext.Reproductions.ToArrayAsync());
    }

    [Fact]
    public async Task CreateAllowsDistinctPeriodsForTheSamePair()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "reproduction-periods@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var maleBirdId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Male, "Macho períodos", "323456");
        var femaleBirdId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Female, "Fêmea períodos", "323457");

        using var first = await PostAsync(client, maleBirdId, femaleBirdId, "2026-09-01");
        using var second = await PostAsync(client, maleBirdId, femaleBirdId, "2026-09-05");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        using var firstBody = JsonDocument.Parse(await first.Content.ReadAsStreamAsync());
        using var secondBody = JsonDocument.Parse(await second.Content.ReadAsStreamAsync());
        Assert.NotEqual(
            firstBody.RootElement.GetProperty("reproductionId").GetGuid(),
            secondBody.RootElement.GetProperty("reproductionId").GetGuid());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Equal(
            2,
            await dbContext.Reproductions.CountAsync(candidate =>
                candidate.BreedingFarmId == farmId &&
                candidate.MaleBirdId == maleBirdId &&
                candidate.FemaleBirdId == femaleBirdId));
    }

    private static object ValidRequest() => new
    {
        maleBirdId = Guid.NewGuid(),
        femaleBirdId = Guid.NewGuid(),
        startDate = "2026-09-01"
    };

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        Guid maleBirdId,
        Guid femaleBirdId,
        string startDate)
    {
        return await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/reproductions",
            await GetAntiforgeryTokenAsync(client),
            new { maleBirdId, femaleBirdId, startDate }));
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

    private static async Task<Guid> AddBirdAsync(
        WebApplicationFactory<Program> factory,
        Guid farmId,
        Guid speciesId,
        BirdSex sex,
        string name,
        string ringNumber,
        BirdStatus? status = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var now = DateTimeOffset.UtcNow;
        var bird = new Bird(
            Guid.NewGuid(),
            now,
            farmId,
            name,
            speciesId,
            sex,
            null,
            ringNumber,
            null,
            null,
            null,
            null,
            null,
            DateOnly.FromDateTime(now.UtcDateTime));
        if (status is not null)
        {
            bird.ChangeStatus(
                status.Value,
                null,
                null,
                DateOnly.FromDateTime(now.UtcDateTime),
                now.AddSeconds(1));
        }

        dbContext.Birds.Add(bird);
        await dbContext.SaveChangesAsync();
        return bird.Id;
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
