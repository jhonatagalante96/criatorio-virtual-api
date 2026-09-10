using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.Species;
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

public sealed class BirdListingEndpointTests
{
    [Fact]
    public async Task ListReturnsFilteredSortedAndPaginatedBirdsFromSelectedFarm()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "bird-list-owner@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var otherSpeciesId = await AddSpeciesAsync(factory);

        await AddBirdsAsync(
            factory,
            new BirdSeed("Zebra", speciesId, BirdSex.Female, "900001", new DateOnly(2020, 9, 7), BirdStatus.Active, DateTimeOffset.UtcNow.AddMinutes(-4), farmId),
            new BirdSeed("Aurora", speciesId, BirdSex.Female, "900002", new DateOnly(2021, 9, 7), BirdStatus.Active, DateTimeOffset.UtcNow.AddMinutes(-3), farmId),
            new BirdSeed("Sem Anel", speciesId, BirdSex.Unknown, null, null, BirdStatus.Active, DateTimeOffset.UtcNow.AddMinutes(-2), farmId),
            new BirdSeed("Boreal", speciesId, BirdSex.Male, "900003", new DateOnly(2019, 9, 7), BirdStatus.Archived, DateTimeOffset.UtcNow.AddMinutes(-1), farmId),
            new BirdSeed("Ametista", otherSpeciesId, BirdSex.Female, "900004", new DateOnly(2022, 9, 7), BirdStatus.Active, DateTimeOffset.UtcNow, farmId));

        using var response = await client.GetAsync(
            "/api/birds?search=a&sex=Female&speciesId=" + speciesId +
            "&status=Active&identificationPending=false&sortBy=name&sortDirection=desc&page=1&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        Assert.Equal(farmId, root.GetProperty("breedingFarmId").GetGuid());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(2, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, root.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, root.GetProperty("totalPages").GetInt32());

        var items = root.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal("Zebra", items[0].GetProperty("name").GetString());
        Assert.Equal("Aurora", items[1].GetProperty("name").GetString());
        Assert.All(items, item =>
        {
            Assert.Equal("Female", item.GetProperty("sex").GetString());
            Assert.Equal("Active", item.GetProperty("status").GetString());
            Assert.False(item.GetProperty("identificationPending").GetBoolean());
            Assert.Equal("Sabiá-laranjeira", item.GetProperty("speciesPopularName").GetString());
        });
    }

    [Fact]
    public async Task ListUsesTwentyItemsByDefaultAndSupportsSecondPage()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "bird-pagination-owner@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);

        await AddBirdsAsync(
            factory,
            Enumerable.Range(0, 21)
                .Select(index => new BirdSeed(
                    $"Bird {index:00}",
                    speciesId,
                    BirdSex.Unknown,
                    $"91{index:0000}",
                    null,
                    BirdStatus.Active,
                    DateTimeOffset.UtcNow.AddMinutes(-index),
                    farmId))
                .ToArray());

        using var firstPage = await client.GetAsync("/api/birds");
        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
        using var firstDocument = JsonDocument.Parse(await firstPage.Content.ReadAsStreamAsync());
        Assert.Equal(20, firstDocument.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(21, firstDocument.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, firstDocument.RootElement.GetProperty("totalPages").GetInt32());
        Assert.Equal(20, firstDocument.RootElement.GetProperty("items").GetArrayLength());

        using var secondPage = await client.GetAsync("/api/birds?page=2&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, secondPage.StatusCode);
        using var secondDocument = JsonDocument.Parse(await secondPage.Content.ReadAsStreamAsync());
        Assert.Equal(2, secondDocument.RootElement.GetProperty("page").GetInt32());
        Assert.Single(secondDocument.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Bird 20", secondDocument.RootElement.GetProperty("items")[0].GetProperty("name").GetString());

        using var farPage = await client.GetAsync($"/api/birds?page={int.MaxValue}&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, farPage.StatusCode);
        using var farPageDocument = JsonDocument.Parse(await farPage.Content.ReadAsStreamAsync());
        Assert.Equal(21, farPageDocument.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Empty(farPageDocument.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task ListReturnsEmptyPageWhenSelectedFarmHasNoBirds()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "bird-empty-owner@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);

        using var response = await client.GetAsync("/api/birds");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal(farmId, document.RootElement.GetProperty("breedingFarmId").GetGuid());
        Assert.Equal(0, document.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, document.RootElement.GetProperty("totalPages").GetInt32());
        Assert.Empty(document.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task ListRejectsAuthenticationSelectionAndQueryValidationFailures()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        using var unauthenticated = await client.GetAsync("/api/birds");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await RegisterAndAuthenticateAsync(factory, client, "bird-invalid-owner@example.com");
        using var withoutFarm = await client.GetAsync("/api/birds");
        Assert.Equal(HttpStatusCode.Conflict, withoutFarm.StatusCode);

        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var invalidPaths = new[]
        {
            "/api/birds?sex=1",
            "/api/birds?status=Unknown",
            "/api/birds?identificationPending=maybe",
            "/api/birds?sortBy=invalid",
            "/api/birds?sortDirection=sideways",
            "/api/birds?page=0",
            "/api/birds?pageSize=101",
            "/api/birds?speciesId=00000000-0000-0000-0000-000000000000",
            "/api/birds?search=" + new string('x', 101)
        };

        foreach (var path in invalidPaths)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var validSelectorRequest = await client.GetAsync(
            $"/api/birds?sex=unknown&pageSize=1&speciesId={speciesId}");
        Assert.Equal(HttpStatusCode.OK, validSelectorRequest.StatusCode);
    }

    [Fact]
    public async Task ListNeverCrossesTenantBoundariesOrUsesASelectedForeignFarm()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        var ownerUserId = await RegisterAndAuthenticateAsync(factory, ownerClient, "bird-tenant-owner@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);
        var otherUserId = await RegisterAndAuthenticateAsync(factory, otherClient, "bird-tenant-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient);
        await SelectFarmAsync(otherClient, otherFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        await AddBirdsAsync(
            factory,
            new BirdSeed("Owner bird", speciesId, BirdSex.Female, "920001", null, BirdStatus.Active, DateTimeOffset.UtcNow.AddMinutes(-2), ownerFarmId),
            new BirdSeed("Other bird", speciesId, BirdSex.Male, "920002", null, BirdStatus.Active, DateTimeOffset.UtcNow.AddMinutes(-1), otherFarmId));

        using var ownerResponse = await ownerClient.GetAsync("/api/birds");
        using var ownerDocument = JsonDocument.Parse(await ownerResponse.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
        Assert.Equal(1, ownerDocument.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal("Owner bird", ownerDocument.RootElement.GetProperty("items")[0].GetProperty("name").GetString());

        using var otherResponse = await otherClient.GetAsync("/api/birds");
        using var otherDocument = JsonDocument.Parse(await otherResponse.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, otherResponse.StatusCode);
        Assert.Equal(1, otherDocument.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal("Other bird", otherDocument.RootElement.GetProperty("items")[0].GetProperty("name").GetString());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var owner = await dbContext.Users.SingleAsync(candidate => candidate.Id == ownerUserId);
            owner.SelectedBreedingFarmId = otherFarmId;
            await dbContext.SaveChangesAsync();
        }

        using var foreignSelection = await ownerClient.GetAsync("/api/birds");
        Assert.Equal(HttpStatusCode.NotFound, foreignSelection.StatusCode);
        _ = otherUserId;
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

    private static async Task AddBirdsAsync(
        WebApplicationFactory<Program> factory,
        params BirdSeed[] seeds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        foreach (var seed in seeds)
        {
            var bird = new Bird(
                Guid.NewGuid(),
                seed.CreatedAtUtc,
                seed.BreedingFarmId,
                seed.Name,
                seed.SpeciesId,
                seed.Sex,
                seed.BirthDate,
                seed.RingNumber,
                null,
                null,
                null,
                null,
                null,
                DateOnly.FromDateTime(DateTime.UtcNow));
            dbContext.Birds.Add(bird);
            dbContext.Entry(bird).Property(candidate => candidate.Status).CurrentValue = seed.Status;
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task<Guid> AddSpeciesAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var species = new Species(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "Carduelis carduelis",
            "Pintassilgo",
            isActive: true);
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

    private sealed record BirdSeed(
        string Name,
        Guid SpeciesId,
        BirdSex Sex,
        string? RingNumber,
        DateOnly? BirthDate,
        BirdStatus Status,
        DateTimeOffset CreatedAtUtc,
        Guid BreedingFarmId = default);
}
