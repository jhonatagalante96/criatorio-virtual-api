using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
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

public sealed class ReproductionOriginEndpointTests
{
    [Fact]
    public async Task LinkOriginReturnsTheSelectedBirdWithTheReproductionParentsAndIsIdempotent()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "reproduction-origin-flow@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var maleBirdId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Male, "Macho origem", "510001");
        var femaleBirdId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Female, "Fêmea origem", "510002");
        var childBirdId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Unknown, "Ave selecionada", "510003");
        var reproductionId = await CreateReproductionAsync(client, maleBirdId, femaleBirdId);

        using var reproductionResponse = await client.GetAsync($"/api/reproductions/{reproductionId}");
        Assert.Equal(HttpStatusCode.OK, reproductionResponse.StatusCode);
        using var reproductionBody = JsonDocument.Parse(await reproductionResponse.Content.ReadAsStreamAsync());
        Assert.Equal("Macho origem", reproductionBody.RootElement.GetProperty("maleBird").GetProperty("name").GetString());
        Assert.Equal("Fêmea origem", reproductionBody.RootElement.GetProperty("femaleBird").GetProperty("name").GetString());

        using var beforeResponse = await client.GetAsync($"/api/birds/{childBirdId}");
        Assert.Equal(HttpStatusCode.OK, beforeResponse.StatusCode);
        using var beforeBody = JsonDocument.Parse(await beforeResponse.Content.ReadAsStreamAsync());
        Assert.Null(beforeBody.RootElement.GetProperty("fatherBirdId").GetGuidOrNull());
        Assert.Null(beforeBody.RootElement.GetProperty("motherBirdId").GetGuidOrNull());

        using var linkResponse = await LinkAsync(client, reproductionId, childBirdId);
        Assert.Equal(HttpStatusCode.OK, linkResponse.StatusCode);
        using var linkBody = JsonDocument.Parse(await linkResponse.Content.ReadAsStreamAsync());
        Assert.Equal(childBirdId, linkBody.RootElement.GetProperty("birdId").GetGuid());
        Assert.Equal(maleBirdId, linkBody.RootElement.GetProperty("fatherBirdId").GetGuid());
        Assert.Equal(femaleBirdId, linkBody.RootElement.GetProperty("motherBirdId").GetGuid());
        Assert.False(linkBody.RootElement.GetProperty("identificationPending").GetBoolean());

        using var retryResponse = await LinkAsync(client, reproductionId, childBirdId);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var child = await dbContext.Birds.SingleAsync(candidate => candidate.Id == childBirdId);
        Assert.Equal(maleBirdId, child.FatherBirdId);
        Assert.Equal(femaleBirdId, child.MotherBirdId);
        Assert.Equal(
            2,
            await dbContext.GenealogyNodes.CountAsync(node =>
                node.GenealogyRootId == linkBody.RootElement.GetProperty("genealogyRootId").GetGuid() && !node.IsRoot));
    }

    [Fact]
    public async Task LinkOriginRejectsUnconfirmedIneligibleAndSelfSelectedBirdsWithoutMutation()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "reproduction-origin-invalid@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var maleBirdId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Male, "Macho inválido", "520001");
        var femaleBirdId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Female, "Fêmea inválida", "520002");
        var missingRingBirdId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Unknown, "Sem anilha", null);
        var archivedBirdId = await AddBirdAsync(
            factory,
            farmId,
            speciesId,
            BirdSex.Unknown,
            "Ave arquivada",
            "520003",
            BirdStatus.Archived);
        var reproductionId = await CreateReproductionAsync(client, maleBirdId, femaleBirdId);

        using var unconfirmed = await LinkAsync(client, reproductionId, missingRingBirdId, confirmed: false);
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);

        using var missingRing = await LinkAsync(client, reproductionId, missingRingBirdId);
        Assert.Equal(HttpStatusCode.BadRequest, missingRing.StatusCode);

        using var archived = await LinkAsync(client, reproductionId, archivedBirdId);
        Assert.Equal(HttpStatusCode.BadRequest, archived.StatusCode);

        using var selfSelected = await LinkAsync(client, reproductionId, maleBirdId);
        Assert.Equal(HttpStatusCode.BadRequest, selfSelected.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Null((await dbContext.Birds.SingleAsync(candidate => candidate.Id == missingRingBirdId)).FatherBirdId);
        Assert.Null((await dbContext.Birds.SingleAsync(candidate => candidate.Id == missingRingBirdId)).MotherBirdId);
        Assert.Null((await dbContext.Birds.SingleAsync(candidate => candidate.Id == archivedBirdId)).FatherBirdId);
        Assert.Null((await dbContext.Birds.SingleAsync(candidate => candidate.Id == archivedBirdId)).MotherBirdId);
    }

    [Fact]
    public async Task LinkOriginRejectsAnotherOriginWithoutOverwritingTheExistingGenealogy()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "reproduction-origin-conflict@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var firstMaleId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Male, "Primeiro macho", "530001");
        var firstFemaleId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Female, "Primeira fêmea", "530002");
        var secondMaleId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Male, "Segundo macho", "530003");
        var secondFemaleId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Female, "Segunda fêmea", "530004");
        var childBirdId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Unknown, "Filhote conflitante", "530005");
        var firstReproductionId = await CreateReproductionAsync(client, firstMaleId, firstFemaleId);
        var secondReproductionId = await CreateReproductionAsync(client, secondMaleId, secondFemaleId);

        using var firstLink = await LinkAsync(client, firstReproductionId, childBirdId);
        Assert.Equal(HttpStatusCode.OK, firstLink.StatusCode);

        using var conflictingLink = await LinkAsync(client, secondReproductionId, childBirdId);
        Assert.Equal(HttpStatusCode.Conflict, conflictingLink.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var child = await dbContext.Birds.SingleAsync(candidate => candidate.Id == childBirdId);
        Assert.Equal(firstMaleId, child.FatherBirdId);
        Assert.Equal(firstFemaleId, child.MotherBirdId);
        var rootId = await dbContext.GenealogyNodes
            .Where(node => node.BirdId == childBirdId && node.IsRoot)
            .Select(node => node.Id)
            .SingleAsync();
        Assert.Equal(
            2,
            await dbContext.GenealogyNodes.CountAsync(node =>
                node.GenealogyRootId == rootId && !node.IsRoot));
    }

    [Fact]
    public async Task LinkOriginEnforcesOwnerAuthorizationAndTenantIsolation()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherOwnerClient = CreateClient(factory);
        using var viewerClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "reproduction-origin-owner@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var maleBirdId = await AddBirdAsync(factory, ownerFarmId, speciesId, BirdSex.Male, "Macho tenant", "540001");
        var femaleBirdId = await AddBirdAsync(factory, ownerFarmId, speciesId, BirdSex.Female, "Fêmea tenant", "540002");
        var childBirdId = await AddBirdAsync(factory, ownerFarmId, speciesId, BirdSex.Unknown, "Ave tenant", "540003");
        var reproductionId = await CreateReproductionAsync(ownerClient, maleBirdId, femaleBirdId);

        await RegisterAndAuthenticateAsync(factory, otherOwnerClient, "reproduction-origin-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherOwnerClient);
        await SelectFarmAsync(otherOwnerClient, otherFarmId);
        var foreignBirdId = await AddBirdAsync(factory, otherFarmId, speciesId, BirdSex.Unknown, "Ave estrangeira", "540004");

        using var foreignReproduction = await LinkAsync(otherOwnerClient, reproductionId, foreignBirdId);
        Assert.Equal(HttpStatusCode.NotFound, foreignReproduction.StatusCode);

        using var foreignBird = await LinkAsync(ownerClient, reproductionId, foreignBirdId);
        Assert.Equal(HttpStatusCode.BadRequest, foreignBird.StatusCode);

        await RegisterAndAuthenticateAsync(factory, viewerClient, "reproduction-origin-viewer@example.com");
        await AddViewerMembershipAsync(factory, ownerFarmId, await GetUserIdAsync(factory, "reproduction-origin-viewer@example.com"));
        using var viewer = await LinkAsync(viewerClient, reproductionId, childBirdId);
        Assert.Equal(HttpStatusCode.NotFound, viewer.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Null((await dbContext.Birds.SingleAsync(candidate => candidate.Id == childBirdId)).FatherBirdId);
    }

    [Fact]
    public async Task ConcurrentLinksForTheSameBirdAllowOnlyOneOrigin()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var firstClient = CreateClient(factory);
        using var secondClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, firstClient, "reproduction-origin-concurrency@example.com");
        var farmId = await CreateFarmAsync(firstClient);
        await SelectFarmAsync(firstClient, farmId);
        await LoginAsync(secondClient, "reproduction-origin-concurrency@example.com");
        await SelectFarmAsync(secondClient, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var firstMaleId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Male, "Macho concorrente 1", "550001");
        var firstFemaleId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Female, "Fêmea concorrente 1", "550002");
        var secondMaleId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Male, "Macho concorrente 2", "550003");
        var secondFemaleId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Female, "Fêmea concorrente 2", "550004");
        var childBirdId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Unknown, "Ave concorrente", "550005");
        var firstReproductionId = await CreateReproductionAsync(firstClient, firstMaleId, firstFemaleId);
        var secondReproductionId = await CreateReproductionAsync(firstClient, secondMaleId, secondFemaleId);
        var firstToken = await GetAntiforgeryTokenAsync(firstClient);
        var secondToken = await GetAntiforgeryTokenAsync(secondClient);

        var firstTask = SendLinkAsync(firstClient, firstReproductionId, childBirdId, firstToken);
        var secondTask = SendLinkAsync(secondClient, secondReproductionId, childBirdId, secondToken);
        var responses = await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        foreach (var response in responses)
        {
            response.Dispose();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var child = await dbContext.Birds.SingleAsync(candidate => candidate.Id == childBirdId);
        Assert.True(child.FatherBirdId is { } fatherId && (fatherId == firstMaleId || fatherId == secondMaleId));
        Assert.True(child.MotherBirdId is { } motherId && (motherId == firstFemaleId || motherId == secondFemaleId));
        var rootId = await dbContext.GenealogyNodes
            .Where(node => node.BirdId == childBirdId && node.IsRoot)
            .Select(node => node.Id)
            .SingleAsync();
        Assert.Equal(
            2,
            await dbContext.GenealogyNodes.CountAsync(node =>
                node.GenealogyRootId == rootId && !node.IsRoot));
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

        await LoginAsync(client, email);
    }

    private static async Task LoginAsync(HttpClient client, string email)
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
                name = $"Sítio origem {Guid.NewGuid():N}",
                responsibleName = "Owner Principal",
                contactEmail = $"owner-{Guid.NewGuid():N}@example.com"
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

    private static async Task<Guid> CreateReproductionAsync(
        HttpClient client,
        Guid maleBirdId,
        Guid femaleBirdId)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/reproductions",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                maleBirdId,
                femaleBirdId,
                startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)).ToString("yyyy-MM-dd")
            }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("reproductionId").GetGuid();
    }

    private static async Task<HttpResponseMessage> LinkAsync(
        HttpClient client,
        Guid reproductionId,
        Guid birdId,
        bool confirmed = true) =>
        await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/reproductions/{reproductionId}/origin",
            await GetAntiforgeryTokenAsync(client),
            new { birdId, confirmed }));

    private static Task<HttpResponseMessage> SendLinkAsync(
        HttpClient client,
        Guid reproductionId,
        Guid birdId,
        string antiforgeryToken) =>
        client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/reproductions/{reproductionId}/origin",
            antiforgeryToken,
            new { birdId, confirmed = true }));

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
        var birdId = Guid.NewGuid();
        var bird = new Bird(
            birdId,
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
        dbContext.GenealogyNodes.Add(new GenealogyNode(Guid.NewGuid(), now, farmId, birdId));
        await dbContext.SaveChangesAsync();
        return birdId;
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

    private static async Task<Guid> GetUserIdAsync(
        WebApplicationFactory<Program> factory,
        string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        return await dbContext.Users
            .Where(candidate => candidate.Email == email)
            .Select(candidate => candidate.Id)
            .SingleAsync();
    }

    private static async Task AddViewerMembershipAsync(
        WebApplicationFactory<Program> factory,
        Guid farmId,
        Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        dbContext.BreedingFarmUsers.Add(new BreedingFarmUser(
            farmId,
            userId,
            BreedingFarmRole.Viewer,
            DateTimeOffset.UtcNow));
        var user = await dbContext.Users.SingleAsync(candidate => candidate.Id == userId);
        user.SelectedBreedingFarmId = farmId;
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

internal static class JsonElementExtensions
{
    public static Guid? GetGuidOrNull(this JsonElement element) =>
        element.ValueKind == JsonValueKind.Null ? null : element.GetGuid();
}
