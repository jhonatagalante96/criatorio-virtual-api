using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Reproductions;
using CriatorioVirtual.Domain.Transfers;
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

namespace CriatorioVirtual.IntegrationTests.BreedingFarms;

public sealed class BreedingFarmStatisticsEndpointTests
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

        using var unauthenticated = await client.GetAsync(StatisticsPath);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await RegisterAndAuthenticateAsync(factory, client, "statistics-owner@example.com");
        using var withoutFarm = await client.GetAsync(StatisticsPath);
        Assert.Equal(HttpStatusCode.Conflict, withoutFarm.StatusCode);
    }

    [Fact]
    public async Task GetReturnsZeroCategoriesAndRejectsUnsafeDateRanges()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "statistics-empty@example.com");
        var farmId = await CreateFarmAsync(client, "Empty farm", "statistics-empty@example.com");
        await SelectFarmAsync(client, farmId);

        using var response = await client.GetAsync(StatisticsPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;

        Assert.Equal(farmId, root.GetProperty("breedingFarmId").GetGuid());
        Assert.Equal(30, root.GetProperty("daily").GetArrayLength());
        Assert.All(
            root.GetProperty("birdsByStatus").EnumerateArray(),
            item => Assert.Equal(0, item.GetProperty("count").GetInt32()));
        Assert.All(
            root.GetProperty("birdsBySex").EnumerateArray(),
            item => Assert.Equal(0, item.GetProperty("count").GetInt32()));
        Assert.All(
            root.GetProperty("daily").EnumerateArray(),
            item => Assert.All(
                item.EnumerateObject().Where(property => property.Name.EndsWith("Count", StringComparison.Ordinal)),
                property => Assert.Equal(0, property.Value.GetInt32())));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        using var tooLong = await client.GetAsync(
            $"{StatisticsPath}?from={today.AddDays(-366):yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        using var future = await client.GetAsync(
            $"{StatisticsPath}?from={today:yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
    }

    [Fact]
    public async Task GetAggregatesSelectedTenantBirdsReproductionsAndTransfersByUtcDate()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        using var viewerClient = CreateClient(factory);
        var ownerId = await RegisterAndAuthenticateAsync(factory, ownerClient, "statistics-owner@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient, "Owner farm", "statistics-owner@example.com");
        await SelectFarmAsync(ownerClient, ownerFarmId);
        var otherOwnerId = await RegisterAndAuthenticateAsync(factory, otherClient, "statistics-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient, "Other farm", "statistics-other@example.com");
        await SelectFarmAsync(otherClient, otherFarmId);
        var viewerId = await RegisterAndAuthenticateAsync(factory, viewerClient, "statistics-viewer@example.com");
        var (speciesId, secondSpeciesId) = await GetSpeciesIdsAsync(factory);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-29);
        var registrationDay = today.AddDays(-15);
        var birthDay = today.AddDays(-14);
        var secondRegistrationDay = today.AddDays(-4);
        var secondBirthDay = today.AddDays(-3);

        var maleBirdId = await AddBirdAsync(
            factory, ownerFarmId, speciesId, BirdSex.Male, "Local male", Utc(registrationDay), birthDay);
        var archivedBirdId = await AddBirdAsync(
            factory,
            ownerFarmId,
            speciesId,
            BirdSex.Female,
            "Archived female",
            Utc(from.AddDays(-1)),
            from.AddDays(-20),
            BirdStatus.Archived);
        var secondBirdId = await AddBirdAsync(
            factory, ownerFarmId, secondSpeciesId, BirdSex.Unknown, "Local unknown", Utc(secondRegistrationDay), secondBirthDay);
        var foreignBirdId = await AddBirdAsync(
            factory, otherFarmId, speciesId, BirdSex.Male, "Foreign bird", Utc(registrationDay), birthDay);
        var foreignFemaleBirdId = await AddBirdAsync(
            factory, otherFarmId, speciesId, BirdSex.Female, "Foreign female", Utc(registrationDay), birthDay);

        await AddReproductionAsync(
            factory,
            ownerFarmId,
            maleBirdId,
            archivedBirdId,
            from.AddDays(2),
            from.AddDays(11));
        await AddReproductionAsync(
            factory,
            ownerFarmId,
            maleBirdId,
            secondBirdId,
            from.AddDays(-3),
            null);
        await AddReproductionAsync(
            factory,
            otherFarmId,
            foreignBirdId,
            foreignFemaleBirdId,
            from.AddDays(5),
            null);

        await AddInternalTransferAsync(
            factory, ownerFarmId, otherFarmId, maleBirdId, ownerId, Utc(from.AddDays(12)), accepted: true);
        await AddInternalTransferAsync(
            factory, otherFarmId, ownerFarmId, foreignBirdId, otherOwnerId, Utc(from.AddDays(13)), accepted: true);
        await AddInternalTransferAsync(
            factory, ownerFarmId, otherFarmId, secondBirdId, ownerId, Utc(from.AddDays(14)), accepted: false);
        await AddInternalTransferAsync(
            factory, otherFarmId, ownerFarmId, foreignBirdId, otherOwnerId, Utc(from.AddDays(15)), accepted: false);
        await AddExternalTransferAsync(factory, ownerFarmId, secondBirdId, Utc(from.AddDays(16)));
        await AddExternalTransferAsync(factory, otherFarmId, foreignBirdId, Utc(from.AddDays(16)));

        await AddViewerMembershipAsync(factory, ownerFarmId, viewerId);
        using var viewerForbidden = await viewerClient.GetAsync(StatisticsPath);
        Assert.Equal(HttpStatusCode.NotFound, viewerForbidden.StatusCode);

        using var response = await ownerClient.GetAsync(
            $"{StatisticsPath}?from={from:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;

        Assert.Equal(ownerFarmId, root.GetProperty("breedingFarmId").GetGuid());
        Assert.Equal(from, DateOnly.Parse(root.GetProperty("from").GetString()!));
        Assert.Equal(3, CountCategory(root.GetProperty("birdsByStatus"), "Active") +
            CountCategory(root.GetProperty("birdsByStatus"), "Archived"));
        Assert.Equal(2, CountCategory(root.GetProperty("birdsByStatus"), "Active"));
        Assert.Equal(1, CountCategory(root.GetProperty("birdsByStatus"), "Archived"));
        Assert.Equal(1, CountCategory(root.GetProperty("birdsBySex"), "Male"));
        Assert.Equal(1, CountCategory(root.GetProperty("birdsBySex"), "Female"));
        Assert.Equal(1, CountCategory(root.GetProperty("birdsBySex"), "Unknown"));
        var speciesCounts = root.GetProperty("birdsBySpecies").EnumerateArray().ToArray();
        Assert.Equal(2, speciesCounts.Single(item => item.GetProperty("speciesId").GetGuid() == speciesId)
            .GetProperty("count").GetInt32());
        Assert.Equal(1, speciesCounts.Single(item => item.GetProperty("speciesId").GetGuid() == secondSpeciesId)
            .GetProperty("count").GetInt32());
        Assert.DoesNotContain(
            speciesCounts,
            item => item.GetProperty("count").GetInt32() > 0 &&
                    item.GetProperty("speciesId").GetGuid() != speciesId &&
                    item.GetProperty("speciesId").GetGuid() != secondSpeciesId);

        var daily = root.GetProperty("daily").EnumerateArray().ToArray();
        Assert.Equal(30, daily.Length);
        Assert.Equal(1, Day(daily, registrationDay).GetProperty("birdsRegisteredCount").GetInt32());
        Assert.Equal(1, Day(daily, birthDay).GetProperty("birthsRecordedCount").GetInt32());
        Assert.Equal(1, Day(daily, secondRegistrationDay).GetProperty("birdsRegisteredCount").GetInt32());
        Assert.Equal(1, Day(daily, secondBirthDay).GetProperty("birthsRecordedCount").GetInt32());
        Assert.Equal(1, Day(daily, from.AddDays(2)).GetProperty("reproductionsStartedCount").GetInt32());
        Assert.Equal(1, Day(daily, from.AddDays(11)).GetProperty("reproductionsCompletedCount").GetInt32());
        Assert.Equal(1, Day(daily, from.AddDays(13)).GetProperty("internalTransfersInCount").GetInt32());
        Assert.Equal(1, Day(daily, from.AddDays(12)).GetProperty("internalTransfersOutCount").GetInt32());
        Assert.Equal(1, Day(daily, from.AddDays(16)).GetProperty("externalTransfersOutCount").GetInt32());

        var transfers = root.GetProperty("transfers");
        Assert.Equal(1, transfers.GetProperty("internalTransfersInCount").GetInt32());
        Assert.Equal(1, transfers.GetProperty("internalTransfersOutCount").GetInt32());
        Assert.Equal(1, transfers.GetProperty("externalTransfersOutCount").GetInt32());
        Assert.Equal(1, CountCategory(transfers.GetProperty("currentIncomingRequestsByStatus"), "Pending"));
        Assert.Equal(1, CountCategory(transfers.GetProperty("currentIncomingRequestsByStatus"), "Accepted"));
        Assert.Equal(1, CountCategory(transfers.GetProperty("currentOutgoingRequestsByStatus"), "Pending"));
        Assert.Equal(1, CountCategory(transfers.GetProperty("currentOutgoingRequestsByStatus"), "Accepted"));
    }

    private const string StatisticsPath = "/api/breeding-farms/current/statistics";

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

    private static async Task<(Guid First, Guid Second)> GetSpeciesIdsAsync(
        WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var ids = await dbContext.Species.OrderBy(species => species.PopularName)
            .Select(species => species.Id)
            .Take(2)
            .ToArrayAsync();
        Assert.Equal(2, ids.Length);
        return (ids[0], ids[1]);
    }

    private static async Task<Guid> AddBirdAsync(
        WebApplicationFactory<Program> factory,
        Guid farmId,
        Guid speciesId,
        BirdSex sex,
        string name,
        DateTimeOffset createdAtUtc,
        DateOnly? birthDate,
        BirdStatus status = BirdStatus.Active)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var bird = new Bird(
            Guid.NewGuid(), createdAtUtc, farmId, name, speciesId, sex, birthDate, null,
            null, null, null, null, null, today);
        if (status != BirdStatus.Active)
        {
            bird.ChangeStatus(status, null, null, today, createdAtUtc.AddMinutes(1));
        }

        dbContext.Birds.Add(bird);
        await dbContext.SaveChangesAsync();
        return bird.Id;
    }

    private static async Task AddReproductionAsync(
        WebApplicationFactory<Program> factory,
        Guid farmId,
        Guid maleBirdId,
        Guid femaleBirdId,
        DateOnly startDate,
        DateOnly? endDate)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var reproduction = new Reproduction(
            Guid.NewGuid(), Utc(startDate), farmId, maleBirdId, femaleBirdId, startDate, null, null, today);
        if (endDate is not null)
        {
            reproduction.Finish(endDate.Value, today, Utc(endDate.Value).AddHours(12));
        }

        dbContext.Reproductions.Add(reproduction);
        await dbContext.SaveChangesAsync();
    }

    private static async Task AddInternalTransferAsync(
        WebApplicationFactory<Program> factory,
        Guid sourceFarmId,
        Guid destinationFarmId,
        Guid birdId,
        Guid requesterId,
        DateTimeOffset atUtc,
        bool accepted)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var request = new InternalTransferRequest(
            Guid.NewGuid(), atUtc, sourceFarmId, destinationFarmId, birdId, requesterId);
        if (accepted)
        {
            request.Accept(atUtc.AddHours(1));
        }

        dbContext.InternalTransferRequests.Add(request);
        await dbContext.SaveChangesAsync();
    }

    private static async Task AddExternalTransferAsync(
        WebApplicationFactory<Program> factory,
        Guid farmId,
        Guid birdId,
        DateTimeOffset atUtc)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        dbContext.ExternalTransfers.Add(new ExternalTransfer(
            Guid.NewGuid(), atUtc, farmId, birdId, "Recipient", null));
        await dbContext.SaveChangesAsync();
    }

    private static async Task AddViewerMembershipAsync(
        WebApplicationFactory<Program> factory,
        Guid farmId,
        Guid viewerId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var viewer = await dbContext.Users.SingleAsync(user => user.Id == viewerId);
        viewer.SelectedBreedingFarmId = farmId;
        dbContext.BreedingFarmUsers.Add(new BreedingFarmUser(
            farmId, viewerId, BreedingFarmRole.Viewer, DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();
    }

    private static int CountCategory(JsonElement array, string name) =>
        array.EnumerateArray()
            .Where(item =>
                (item.TryGetProperty("status", out var status) && status.GetString() == name) ||
                (item.TryGetProperty("sex", out var sex) && sex.GetString() == name))
            .Select(item => item.GetProperty("count").GetInt32())
            .Single();

    private static JsonElement Day(IReadOnlyCollection<JsonElement> days, DateOnly date) =>
        days.Single(item => DateOnly.FromDateTime(item.GetProperty("date").GetDateTime()) == date);

    private static DateTimeOffset Utc(DateOnly date) =>
        new(date.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));

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
