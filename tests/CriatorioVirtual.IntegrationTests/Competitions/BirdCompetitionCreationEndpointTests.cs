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

namespace CriatorioVirtual.IntegrationTests.Competitions;

public sealed class BirdCompetitionCreationEndpointTests
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
            $"/api/birds/{Guid.NewGuid()}/competitions",
            await GetAntiforgeryTokenAsync(client),
            ValidRequest()));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await RegisterAndAuthenticateAsync(factory, client, "competition-auth@example.com");
        using var withoutFarm = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/birds/{Guid.NewGuid()}/competitions",
            await GetAntiforgeryTokenAsync(client),
            ValidRequest()));

        Assert.Equal(HttpStatusCode.Conflict, withoutFarm.StatusCode);
    }

    [Fact]
    public async Task CreatePersistsAllCompetitionFieldsAndPreservesCivilDate()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "competition-owner@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await AddBirdAsync(factory, farmId, speciesId, "Azul campeão", "423456");

        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/birds/{birdId}/competitions",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                name = "  Campeonato Estadual  ",
                date = "2026-09-07",
                category = "Livre / Azul",
                placement = 2,
                location = "Macaé",
                notes = "  Final  "
            }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var competitionId = body.RootElement.GetProperty("competitionId").GetGuid();
        Assert.EndsWith($"/api/birds/{birdId}/competitions/{competitionId}", response.Headers.Location.ToString());
        Assert.Equal(birdId, body.RootElement.GetProperty("birdId").GetGuid());
        Assert.Equal("Campeonato Estadual", body.RootElement.GetProperty("name").GetString());
        Assert.Equal("2026-09-07", body.RootElement.GetProperty("date").GetString());
        Assert.Equal("Livre / Azul", body.RootElement.GetProperty("category").GetString());
        Assert.Equal(2, body.RootElement.GetProperty("placement").GetInt32());
        Assert.Equal("Macaé", body.RootElement.GetProperty("location").GetString());
        Assert.Equal("Final", body.RootElement.GetProperty("notes").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var competition = await dbContext.BirdCompetitions.SingleAsync(candidate => candidate.Id == competitionId);
        Assert.Equal(farmId, competition.BreedingFarmId);
        Assert.Equal(birdId, competition.BirdId);
        Assert.Equal(new DateOnly(2026, 9, 7), competition.CompetitionDate);
        Assert.Equal("Campeonato Estadual", competition.Name);
        Assert.Equal("Livre / Azul", competition.Category);
        Assert.Equal(2, competition.Placement);
        Assert.Equal("Macaé", competition.Location);
        Assert.Equal("Final", competition.Notes);
    }

    [Fact]
    public async Task CreateRejectsBlankAndFutureDates()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "competition-validation@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await AddBirdAsync(factory, farmId, speciesId, "Azul validação", "523456");

        using var blankName = await PostAsync(client, birdId, new
        {
            name = "  ",
            date = "2026-09-07"
        });
        Assert.Equal(HttpStatusCode.BadRequest, blankName.StatusCode);

        using var futureDate = await PostAsync(client, birdId, new
        {
            name = "Campeonato futuro",
            date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)).ToString("yyyy-MM-dd")
        });
        Assert.Equal(HttpStatusCode.BadRequest, futureDate.StatusCode);

        using var invalidPlacement = await PostAsync(client, birdId, new
        {
            name = "Colocação inválida",
            placement = 0
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidPlacement.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Empty(await dbContext.BirdCompetitions.ToArrayAsync());
    }

    [Fact]
    public async Task CreateRejectsForeignBirdWithNotFound()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "competition-tenant-owner@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);
        await RegisterAndAuthenticateAsync(factory, otherClient, "competition-tenant-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient);
        await SelectFarmAsync(otherClient, otherFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var foreignBirdId = await AddBirdAsync(factory, otherFarmId, speciesId, "Ave de outra farm", "623456");

        using var response = await PostAsync(ownerClient, foreignBirdId, new
        {
            name = "Tentativa indevida",
            date = "2026-09-07"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Ave de outra farm", problem, StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Empty(await dbContext.BirdCompetitions.ToArrayAsync());
    }

    private static object ValidRequest() => new
    {
        name = "Campeonato estadual",
        date = "2026-09-07"
    };

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        Guid birdId,
        object body)
    {
        return await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/birds/{birdId}/competitions",
            await GetAntiforgeryTokenAsync(client),
            body));
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
                contactEmail = $"farm-{Guid.NewGuid():N}@example.com"
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
        string name,
        string ringNumber)
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
            BirdSex.Unknown,
            null,
            ringNumber,
            null,
            null,
            null,
            null,
            null,
            DateOnly.FromDateTime(now.UtcDateTime));

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
