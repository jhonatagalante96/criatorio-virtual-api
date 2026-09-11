using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Reproductions;
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

namespace CriatorioVirtual.IntegrationTests.Dashboard;

public sealed class DashboardEndpointTests
{
    [Fact]
    public async Task GetRequiresAuthenticationAndASelectedBreedingFarm()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        using var unauthenticated = await client.GetAsync("/api/dashboard");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await RegisterAndAuthenticateAsync(factory, client, "dashboard-owner@example.com");

        using var withoutFarm = await client.GetAsync("/api/dashboard");
        Assert.Equal(HttpStatusCode.Conflict, withoutFarm.StatusCode);
    }

    [Fact]
    public async Task GetReturnsTenantScopedIndicatorsPendenciesAndRecentActivities()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "dashboard-owner@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient, "Owner farm", "dashboard-owner@example.com");
        await SelectFarmAsync(ownerClient, ownerFarmId);
        await RegisterAndAuthenticateAsync(factory, otherClient, "dashboard-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient, "Other farm", "dashboard-other@example.com");
        await SelectFarmAsync(otherClient, otherFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);

        var localMaleId = await AddBirdAsync(factory, ownerFarmId, speciesId, BirdSex.Male, "Local male", "100001");
        var localFemaleId = await AddBirdAsync(factory, ownerFarmId, speciesId, BirdSex.Female, "Local female", "100002");
        var pendingBirdId = await AddBirdAsync(factory, ownerFarmId, speciesId, BirdSex.Unknown, "Pending bird", null);
        var archivedBirdId = await AddBirdAsync(
            factory,
            ownerFarmId,
            speciesId,
            BirdSex.Female,
            "Archived bird",
            "100003",
            BirdStatus.Archived);
        var localReproductionId = await AddReproductionAsync(factory, ownerFarmId, localMaleId, localFemaleId);

        var foreignMaleId = await AddBirdAsync(factory, otherFarmId, speciesId, BirdSex.Male, "Foreign male", "200001");
        var foreignFemaleId = await AddBirdAsync(factory, otherFarmId, speciesId, BirdSex.Female, "Foreign female", "200002");
        var foreignReproductionId = await AddReproductionAsync(factory, otherFarmId, foreignMaleId, foreignFemaleId);

        using var response = await ownerClient.GetAsync("/api/dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        var indicators = root.GetProperty("indicators");

        Assert.Equal(ownerFarmId, root.GetProperty("breedingFarmId").GetGuid());
        Assert.Equal(3, indicators.GetProperty("activeBirdCount").GetInt32());
        Assert.Equal(1, indicators.GetProperty("pendingIdentificationCount").GetInt32());
        Assert.Equal(1, indicators.GetProperty("activeReproductionCount").GetInt32());

        var pending = Assert.Single(root.GetProperty("pending").EnumerateArray());
        Assert.Equal("BirdIdentificationPending", pending.GetProperty("code").GetString());
        Assert.Equal("bird", pending.GetProperty("resourceType").GetString());
        Assert.Equal(1, pending.GetProperty("count").GetInt32());

        var activities = root.GetProperty("activities").EnumerateArray().ToArray();
        Assert.Equal(5, activities.Length);
        var activityIds = activities
            .Select(activity => activity.GetProperty("resourceId").GetGuid())
            .ToHashSet();
        Assert.Equal(
            new[] { localMaleId, localFemaleId, pendingBirdId, archivedBirdId, localReproductionId }.ToHashSet(),
            activityIds);
        Assert.DoesNotContain(foreignReproductionId, activityIds);
        Assert.DoesNotContain(activities, activity => activity.GetProperty("title").GetString() == "Foreign male");
        Assert.DoesNotContain(activities, activity => activity.GetProperty("title").GetString() == "Foreign female");
        Assert.All(
            activities,
            activity => Assert.True(activity.GetProperty("occurredAtUtc").GetDateTimeOffset() <= DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task GetRejectsAViewerEvenWhenTheSelectedFarmIsValid()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var viewerClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "dashboard-owner@example.com");
        var farmId = await CreateFarmAsync(ownerClient, "Owner farm", "dashboard-owner@example.com");
        var viewerId = await RegisterAndAuthenticateAsync(factory, viewerClient, "dashboard-viewer@example.com");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var viewer = await dbContext.Users.SingleAsync(user => user.Id == viewerId);
            viewer.SelectedBreedingFarmId = farmId;
            dbContext.BreedingFarmUsers.Add(new BreedingFarmUser(
                farmId,
                viewerId,
                BreedingFarmRole.Viewer,
                DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync();
        }

        using var response = await viewerClient.GetAsync("/api/dashboard");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

    private static async Task<Guid> CreateFarmAsync(HttpClient client, string name, string contactEmail)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/breeding-farms",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                name,
                responsibleName = "Owner Principal",
                contactEmail
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
        string? ringNumber,
        BirdStatus? status = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var bird = new Bird(
            Guid.NewGuid(),
            now,
            farmId,
            name,
            speciesId,
            sex,
            today.AddYears(-2),
            ringNumber,
            null,
            null,
            null,
            null,
            null,
            today);
        if (status is not null)
        {
            bird.ChangeStatus(status.Value, null, null, today, now.AddSeconds(1));
        }

        dbContext.Birds.Add(bird);
        await dbContext.SaveChangesAsync();
        return bird.Id;
    }

    private static async Task<Guid> AddReproductionAsync(
        WebApplicationFactory<Program> factory,
        Guid farmId,
        Guid maleBirdId,
        Guid femaleBirdId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var now = DateTimeOffset.UtcNow;
        var reproduction = new Reproduction(
            Guid.NewGuid(),
            now,
            farmId,
            maleBirdId,
            femaleBirdId,
            DateOnly.FromDateTime(now.UtcDateTime),
            null,
            null,
            DateOnly.FromDateTime(now.UtcDateTime));
        dbContext.Reproductions.Add(reproduction);
        await dbContext.SaveChangesAsync();
        return reproduction.Id;
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
