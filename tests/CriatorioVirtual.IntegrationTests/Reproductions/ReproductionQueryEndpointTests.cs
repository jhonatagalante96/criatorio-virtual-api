using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Reproductions;
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

public sealed class ReproductionQueryEndpointTests
{
    [Fact]
    public async Task ListAndGetRequireAuthenticationAndASelectedBreedingFarm()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        var reproductionId = Guid.NewGuid();

        using var unauthenticatedList = await client.GetAsync("/api/reproductions");
        using var unauthenticatedDetail = await client.GetAsync($"/api/reproductions/{reproductionId}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedList.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedDetail.StatusCode);

        await RegisterAndAuthenticateAsync(factory, client, "reproduction-query-auth@example.com");

        using var withoutFarmList = await client.GetAsync("/api/reproductions");
        using var withoutFarmDetail = await client.GetAsync($"/api/reproductions/{reproductionId}");
        Assert.Equal(HttpStatusCode.Conflict, withoutFarmList.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, withoutFarmDetail.StatusCode);
    }

    [Fact]
    public async Task ListSupportsHistoricalStatusPaginationAndBirdFiltering()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "reproduction-query-owner@example.com");
        var farmId = await CreateFarmAsync(client, "Reproduction query farm", "reproduction-query-owner@example.com");
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var maleId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Male, "Macho da origem", "400001");
        var femaleId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Female, "Fêmea da origem", "400002");
        var activeId = await AddReproductionAsync(
            factory,
            farmId,
            maleId,
            femaleId,
            new DateOnly(2026, 9, 5),
            notes: "Período ativo");
        var historicalId = await AddReproductionAsync(
            factory,
            farmId,
            maleId,
            femaleId,
            new DateOnly(2026, 8, 1),
            ReproductionStatus.Finished,
            new DateOnly(2026, 8, 20),
            "Período encerrado");

        using var firstPage = await client.GetAsync("/api/reproductions?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
        using var firstPageBody = JsonDocument.Parse(await firstPage.Content.ReadAsStreamAsync());
        var firstPageRoot = firstPageBody.RootElement;
        Assert.Equal(farmId, firstPageRoot.GetProperty("breedingFarmId").GetGuid());
        Assert.Equal(1, firstPageRoot.GetProperty("items").GetArrayLength());
        Assert.Equal(2, firstPageRoot.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, firstPageRoot.GetProperty("totalPages").GetInt32());
        var active = Assert.Single(firstPageRoot.GetProperty("items").EnumerateArray());
        Assert.Equal(activeId, active.GetProperty("reproductionId").GetGuid());
        Assert.Equal("Active", active.GetProperty("status").GetString());
        Assert.Equal("Macho da origem", active.GetProperty("maleBird").GetProperty("name").GetString());
        Assert.Equal("400002", active.GetProperty("femaleBird").GetProperty("ringNumber").GetString());

        using var historicalFilter = await client.GetAsync("/api/reproductions?status=Finished");
        Assert.Equal(HttpStatusCode.OK, historicalFilter.StatusCode);
        using var historicalBody = JsonDocument.Parse(await historicalFilter.Content.ReadAsStreamAsync());
        var historical = Assert.Single(historicalBody.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(historicalId, historical.GetProperty("reproductionId").GetGuid());
        Assert.Equal("Finished", historical.GetProperty("status").GetString());
        Assert.Equal("2026-08-20", historical.GetProperty("endDate").GetString());

        using var birdFilter = await client.GetAsync($"/api/reproductions?birdId={femaleId}");
        Assert.Equal(HttpStatusCode.OK, birdFilter.StatusCode);
        using var birdFilterBody = JsonDocument.Parse(await birdFilter.Content.ReadAsStreamAsync());
        Assert.Equal(2, birdFilterBody.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task TransferMaleAndAlterInDestination_OriginPreservesHistoricalSnapshot_AndCannotNavigate()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var originClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, originClient, "reproduction-male-origin@example.com");
        var originFarmId = await CreateFarmAsync(originClient, "Origin farm", "reproduction-male-origin@example.com");
        await SelectFarmAsync(originClient, originFarmId);
        await RegisterAndAuthenticateAsync(factory, otherClient, "reproduction-male-destination@example.com");
        var destinationFarmId = await CreateFarmAsync(otherClient, "Destination farm", "reproduction-male-destination@example.com");
        await SelectFarmAsync(otherClient, destinationFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);

        var maleId = await AddBirdAsync(factory, originFarmId, speciesId, BirdSex.Male, "Macho Original", "111111");
        var femaleId = await AddBirdAsync(factory, originFarmId, speciesId, BirdSex.Female, "Fêmea Original", "222222");

        var reproductionId = await AddReproductionAsync(
            factory,
            originFarmId,
            maleId,
            femaleId,
            new DateOnly(2026, 9, 1),
            notes: "Snapshot preservado");

        // Simulate internal transfer of male bird to destination breeding farm and update bird properties in destination
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE app.birds
                SET "BreedingFarmId" = {destinationFarmId},
                    "Name" = 'Macho Renomeado no Destino',
                    "RingNumber" = '999999',
                    "Status" = {(int)BirdStatus.Active},
                    "UpdatedAtUtc" = {DateTimeOffset.UtcNow}
                WHERE "Id" = {maleId};
                """);
        }

        // Origin tenant views reproduction details: snapshot must remain unchanged and canNavigate must be false
        using var originGetResponse = await originClient.GetAsync($"/api/reproductions/{reproductionId}");
        Assert.Equal(HttpStatusCode.OK, originGetResponse.StatusCode);
        using var originGetBody = JsonDocument.Parse(await originGetResponse.Content.ReadAsStreamAsync());
        var originGetRoot = originGetBody.RootElement;
        Assert.Equal(originFarmId, originGetRoot.GetProperty("breedingFarmId").GetGuid());

        var originMale = originGetRoot.GetProperty("maleBird");
        Assert.Equal(maleId, originMale.GetProperty("birdId").GetGuid());
        Assert.Equal("Macho Original", originMale.GetProperty("name").GetString());
        Assert.Equal("Male", originMale.GetProperty("sex").GetString());
        Assert.Equal("111111", originMale.GetProperty("ringNumber").GetString());
        Assert.Equal("Active", originMale.GetProperty("status").GetString());
        Assert.False(originMale.GetProperty("canNavigate").GetBoolean());

        var originFemale = originGetRoot.GetProperty("femaleBird");
        Assert.Equal(femaleId, originFemale.GetProperty("birdId").GetGuid());
        Assert.Equal("Fêmea Original", originFemale.GetProperty("name").GetString());
        Assert.True(originFemale.GetProperty("canNavigate").GetBoolean());

        // Origin tenant lists reproductions: snapshot must remain unchanged and canNavigate must be false
        using var originListResponse = await originClient.GetAsync("/api/reproductions");
        Assert.Equal(HttpStatusCode.OK, originListResponse.StatusCode);
        using var originListBody = JsonDocument.Parse(await originListResponse.Content.ReadAsStreamAsync());
        var originListItem = Assert.Single(originListBody.RootElement.GetProperty("items").EnumerateArray());
        var listMale = originListItem.GetProperty("maleBird");
        Assert.Equal("Macho Original", listMale.GetProperty("name").GetString());
        Assert.Equal("111111", listMale.GetProperty("ringNumber").GetString());
        Assert.False(listMale.GetProperty("canNavigate").GetBoolean());

        // Cross-tenant: destination tenant cannot view origin tenant's reproduction
        using var destinationGetResponse = await otherClient.GetAsync($"/api/reproductions/{reproductionId}");
        Assert.Equal(HttpStatusCode.NotFound, destinationGetResponse.StatusCode);

        // Cross-tenant: origin tenant cannot view transferred bird via bird profile endpoint
        using var originBirdResponse = await originClient.GetAsync($"/api/birds/{maleId}");
        Assert.Equal(HttpStatusCode.NotFound, originBirdResponse.StatusCode);
    }

    [Fact]
    public async Task TransferFemaleAndAlterInDestination_OriginPreservesHistoricalSnapshot_AndCannotNavigate()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var originClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, originClient, "reproduction-female-origin@example.com");
        var originFarmId = await CreateFarmAsync(originClient, "Origin farm", "reproduction-female-origin@example.com");
        await SelectFarmAsync(originClient, originFarmId);
        await RegisterAndAuthenticateAsync(factory, otherClient, "reproduction-female-destination@example.com");
        var destinationFarmId = await CreateFarmAsync(otherClient, "Destination farm", "reproduction-female-destination@example.com");
        await SelectFarmAsync(otherClient, destinationFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);

        var maleId = await AddBirdAsync(factory, originFarmId, speciesId, BirdSex.Male, "Macho Origem", "333333");
        var femaleId = await AddBirdAsync(factory, originFarmId, speciesId, BirdSex.Female, "Fêmea Origem", "444444");

        var reproductionId = await AddReproductionAsync(
            factory,
            originFarmId,
            maleId,
            femaleId,
            new DateOnly(2026, 9, 1),
            notes: "Snapshot fêmea preservado");

        // Simulate internal transfer of female bird to destination breeding farm and modify in destination
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE app.birds
                SET "BreedingFarmId" = {destinationFarmId},
                    "Name" = 'Fêmea Alterada no Destino',
                    "RingNumber" = '888888',
                    "Status" = {(int)BirdStatus.Active},
                    "UpdatedAtUtc" = {DateTimeOffset.UtcNow}
                WHERE "Id" = {femaleId};
                """);
        }

        // Origin tenant views reproduction details: female snapshot is unchanged, canNavigate is false
        using var originGetResponse = await originClient.GetAsync($"/api/reproductions/{reproductionId}");
        Assert.Equal(HttpStatusCode.OK, originGetResponse.StatusCode);
        using var originGetBody = JsonDocument.Parse(await originGetResponse.Content.ReadAsStreamAsync());
        var originGetRoot = originGetBody.RootElement;

        var originFemale = originGetRoot.GetProperty("femaleBird");
        Assert.Equal(femaleId, originFemale.GetProperty("birdId").GetGuid());
        Assert.Equal("Fêmea Origem", originFemale.GetProperty("name").GetString());
        Assert.Equal("Female", originFemale.GetProperty("sex").GetString());
        Assert.Equal("444444", originFemale.GetProperty("ringNumber").GetString());
        Assert.Equal("Active", originFemale.GetProperty("status").GetString());
        Assert.False(originFemale.GetProperty("canNavigate").GetBoolean());

        var originMale = originGetRoot.GetProperty("maleBird");
        Assert.Equal(maleId, originMale.GetProperty("birdId").GetGuid());
        Assert.True(originMale.GetProperty("canNavigate").GetBoolean());

        // Cross-tenant checks
        using var destResponse = await otherClient.GetAsync($"/api/reproductions/{reproductionId}");
        Assert.Equal(HttpStatusCode.NotFound, destResponse.StatusCode);

        using var originBirdResponse = await originClient.GetAsync($"/api/birds/{femaleId}");
        Assert.Equal(HttpStatusCode.NotFound, originBirdResponse.StatusCode);
    }

    [Fact]
    public async Task ListAllowsAnActiveNonOwnerMembershipAndRejectsInvalidFilters()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var viewerClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "reproduction-viewer-owner@example.com");
        var farmId = await CreateFarmAsync(ownerClient, "Viewer farm", "reproduction-viewer-owner@example.com");
        var viewerId = await RegisterAndAuthenticateAsync(factory, viewerClient, "reproduction-viewer@example.com");
        await AddViewerMembershipAsync(factory, farmId, viewerId);

        using var viewerResponse = await viewerClient.GetAsync("/api/reproductions");
        Assert.Equal(HttpStatusCode.OK, viewerResponse.StatusCode);

        using var invalidStatus = await ownerClient.GetAsync("/api/reproductions?status=Unknown");
        using var invalidPage = await ownerClient.GetAsync("/api/reproductions?page=0&pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalidStatus.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
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
            sex,
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

    private static async Task<Guid> AddReproductionAsync(
        WebApplicationFactory<Program> factory,
        Guid farmId,
        Guid maleBirdId,
        Guid femaleBirdId,
        DateOnly startDate,
        ReproductionStatus status = ReproductionStatus.Active,
        DateOnly? endDate = null,
        string? notes = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var maleBird = await dbContext.Birds.SingleAsync(b => b.Id == maleBirdId);
        var femaleBird = await dbContext.Birds.SingleAsync(b => b.Id == femaleBirdId);
        var now = DateTimeOffset.UtcNow;
        var reproduction = new Reproduction(
            Guid.NewGuid(),
            now,
            farmId,
            maleBird.Id,
            maleBird.Name,
            maleBird.Sex,
            maleBird.BirthDate,
            maleBird.RingNumber,
            maleBird.Status,
            femaleBird.Id,
            femaleBird.Name,
            femaleBird.Sex,
            femaleBird.BirthDate,
            femaleBird.RingNumber,
            femaleBird.Status,
            startDate,
            endDate,
            notes,
            DateOnly.FromDateTime(now.UtcDateTime));
        if (status != ReproductionStatus.Active)
        {
            dbContext.Entry(reproduction).Property(candidate => candidate.Status).CurrentValue = status;
        }

        dbContext.Reproductions.Add(reproduction);
        await dbContext.SaveChangesAsync();
        return reproduction.Id;
    }

    private static async Task ChangeBirdStatusAsync(
        WebApplicationFactory<Program> factory,
        Guid birdId,
        BirdStatus status)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var bird = await dbContext.Birds.SingleAsync(candidate => candidate.Id == birdId);
        dbContext.Entry(bird).Property(candidate => candidate.Status).CurrentValue = status;
        await dbContext.SaveChangesAsync();
    }

    private static async Task AddViewerMembershipAsync(
        WebApplicationFactory<Program> factory,
        Guid farmId,
        Guid viewerId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var viewer = await dbContext.Users.SingleAsync(candidate => candidate.Id == viewerId);
        viewer.SelectedBreedingFarmId = farmId;
        dbContext.BreedingFarmUsers.Add(new BreedingFarmUser(
            farmId,
            viewerId,
            BreedingFarmRole.Viewer,
            DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();
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
