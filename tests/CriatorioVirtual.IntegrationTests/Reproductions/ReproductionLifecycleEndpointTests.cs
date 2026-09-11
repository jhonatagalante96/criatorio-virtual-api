using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
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

namespace CriatorioVirtual.IntegrationTests.Reproductions;

public sealed class ReproductionLifecycleEndpointTests
{
    [Fact]
    public async Task UpdateChangesAnActiveReproductionAndRejectsInvalidDataWithoutPersistence()
    {
        await using var scenario = await CreateScenarioAsync("reproduction-lifecycle-update@example.com");
        var alternativeMaleId = await AddBirdAsync(
            scenario.Factory,
            scenario.FarmId,
            scenario.SpeciesId,
            BirdSex.Male,
            "Macho alternativo",
            "700003");
        var alternativeFemaleId = await AddBirdAsync(
            scenario.Factory,
            scenario.FarmId,
            scenario.SpeciesId,
            BirdSex.Female,
            "Fêmea alternativa",
            "700004");

        using var updated = await SendAsync(
            scenario.Client,
            HttpMethod.Put,
            $"/api/reproductions/{scenario.ReproductionId}",
            new
            {
                maleBirdId = alternativeMaleId,
                femaleBirdId = alternativeFemaleId,
                startDate = "2026-09-02",
                endDate = "2026-09-05",
                notes = "  Período corrigido  "
            });

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var updatedBody = JsonDocument.Parse(await updated.Content.ReadAsStreamAsync());
        Assert.Equal(alternativeMaleId, updatedBody.RootElement.GetProperty("maleBirdId").GetGuid());
        Assert.Equal(alternativeFemaleId, updatedBody.RootElement.GetProperty("femaleBirdId").GetGuid());
        Assert.Equal("2026-09-02", updatedBody.RootElement.GetProperty("startDate").GetString());
        Assert.Equal("2026-09-05", updatedBody.RootElement.GetProperty("endDate").GetString());
        Assert.Equal("Período corrigido", updatedBody.RootElement.GetProperty("notes").GetString());
        Assert.Equal("Active", updatedBody.RootElement.GetProperty("status").GetString());

        using var clearedEndDate = await SendAsync(
            scenario.Client,
            HttpMethod.Put,
            $"/api/reproductions/{scenario.ReproductionId}",
            new
            {
                maleBirdId = alternativeMaleId,
                femaleBirdId = alternativeFemaleId,
                startDate = "2026-09-02",
                endDate = (string?)null,
                notes = "Período em andamento"
            });

        Assert.Equal(HttpStatusCode.OK, clearedEndDate.StatusCode);
        using var clearedBody = JsonDocument.Parse(await clearedEndDate.Content.ReadAsStreamAsync());
        Assert.Equal(JsonValueKind.Null, clearedBody.RootElement.GetProperty("endDate").ValueKind);

        using var invalid = await SendAsync(
            scenario.Client,
            HttpMethod.Put,
            $"/api/reproductions/{scenario.ReproductionId}",
            new
            {
                maleBirdId = alternativeMaleId,
                femaleBirdId = alternativeFemaleId,
                startDate = "2026-09-02",
                endDate = "2999-01-01",
                notes = "Não deve persistir"
            });

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var details = await scenario.Client.GetAsync($"/api/reproductions/{scenario.ReproductionId}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        using var detailBody = JsonDocument.Parse(await details.Content.ReadAsStreamAsync());
        Assert.Equal(JsonValueKind.Null, detailBody.RootElement.GetProperty("endDate").ValueKind);
        Assert.Equal("Período em andamento", detailBody.RootElement.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task FinishPreservesHistoryAllowsNotesCorrectionAndRejectsReopeningOrPeriodChanges()
    {
        await using var scenario = await CreateScenarioAsync("reproduction-lifecycle-finish@example.com");

        using var unconfirmed = await SendStatusAsync(
            scenario.Client,
            scenario.ReproductionId,
            new { status = "Finished", confirmed = false, endDate = "2026-09-06" });
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);

        using var finished = await SendStatusAsync(
            scenario.Client,
            scenario.ReproductionId,
            new { status = "Finished", confirmed = true, endDate = "2026-09-06" });
        Assert.Equal(HttpStatusCode.OK, finished.StatusCode);
        using var finishedBody = JsonDocument.Parse(await finished.Content.ReadAsStreamAsync());
        Assert.Equal("Finished", finishedBody.RootElement.GetProperty("status").GetString());
        Assert.Equal("2026-09-06", finishedBody.RootElement.GetProperty("endDate").GetString());

        using var repeated = await SendStatusAsync(
            scenario.Client,
            scenario.ReproductionId,
            new { status = "Finished", confirmed = true, endDate = "2026-09-06" });
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);

        using var notes = await SendAsync(
            scenario.Client,
            HttpMethod.Put,
            $"/api/reproductions/{scenario.ReproductionId}",
            new { notes = "  Correção histórica  " });
        Assert.Equal(HttpStatusCode.OK, notes.StatusCode);
        using var notesBody = JsonDocument.Parse(await notes.Content.ReadAsStreamAsync());
        Assert.Equal("Correção histórica", notesBody.RootElement.GetProperty("notes").GetString());
        Assert.Equal("Finished", notesBody.RootElement.GetProperty("status").GetString());

        using var periodChange = await SendAsync(
            scenario.Client,
            HttpMethod.Put,
            $"/api/reproductions/{scenario.ReproductionId}",
            new { startDate = "2026-09-02", notes = "Tentativa inválida" });
        Assert.Equal(HttpStatusCode.Conflict, periodChange.StatusCode);

        using var removeEndDate = await SendAsync(
            scenario.Client,
            HttpMethod.Put,
            $"/api/reproductions/{scenario.ReproductionId}",
            new { endDate = (string?)null, notes = "Tentativa inválida" });
        Assert.Equal(HttpStatusCode.Conflict, removeEndDate.StatusCode);

        using var reopen = await SendStatusAsync(
            scenario.Client,
            scenario.ReproductionId,
            new { status = "Active", confirmed = true });
        Assert.Equal(HttpStatusCode.BadRequest, reopen.StatusCode);

        using var list = await scenario.Client.GetAsync("/api/reproductions?status=Finished");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listBody = JsonDocument.Parse(await list.Content.ReadAsStreamAsync());
        var item = Assert.Single(listBody.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(scenario.ReproductionId, item.GetProperty("reproductionId").GetGuid());
        Assert.Equal("Finished", item.GetProperty("status").GetString());
        Assert.Equal("2026-09-06", item.GetProperty("endDate").GetString());
    }

    [Fact]
    public async Task CancelPreservesHistoryAllowsNotesCorrectionAndRejectsRepeatedTransition()
    {
        await using var scenario = await CreateScenarioAsync("reproduction-lifecycle-cancel@example.com");

        using var cancelled = await SendStatusAsync(
            scenario.Client,
            scenario.ReproductionId,
            new { status = "Cancelled", confirmed = true });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        using var cancelledBody = JsonDocument.Parse(await cancelled.Content.ReadAsStreamAsync());
        Assert.Equal("Cancelled", cancelledBody.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, cancelledBody.RootElement.GetProperty("endDate").ValueKind);

        using var repeated = await SendStatusAsync(
            scenario.Client,
            scenario.ReproductionId,
            new { status = "Cancelled", confirmed = true });
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);

        using var invalidPeriod = await SendAsync(
            scenario.Client,
            HttpMethod.Put,
            $"/api/reproductions/{scenario.ReproductionId}",
            new { endDate = "2026-09-05", notes = "Tentativa inválida" });
        Assert.Equal(HttpStatusCode.Conflict, invalidPeriod.StatusCode);

        using var notes = await SendAsync(
            scenario.Client,
            HttpMethod.Put,
            $"/api/reproductions/{scenario.ReproductionId}",
            new { notes = "  Cancelamento corrigido  " });
        Assert.Equal(HttpStatusCode.OK, notes.StatusCode);

        using var details = await scenario.Client.GetAsync($"/api/reproductions/{scenario.ReproductionId}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        using var detailBody = JsonDocument.Parse(await details.Content.ReadAsStreamAsync());
        Assert.Equal("Cancelled", detailBody.RootElement.GetProperty("status").GetString());
        Assert.Equal("Cancelamento corrigido", detailBody.RootElement.GetProperty("notes").GetString());
        Assert.Equal(JsonValueKind.Null, detailBody.RootElement.GetProperty("endDate").ValueKind);
    }

    [Fact]
    public async Task MutationsRequireOwnerMembershipAndKeepTenantsIsolated()
    {
        await using var scenario = await CreateScenarioAsync("reproduction-lifecycle-owner@example.com");
        using var unauthenticated = CreateClient(scenario.Factory);
        using var otherClient = CreateClient(scenario.Factory);
        using var viewerClient = CreateClient(scenario.Factory);

        using var unauthenticatedUpdate = await SendAsync(
            unauthenticated,
            HttpMethod.Put,
            $"/api/reproductions/{scenario.ReproductionId}",
            new { notes = "Sem sessão" });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedUpdate.StatusCode);

        using var unauthenticatedStatus = await SendStatusAsync(
            unauthenticated,
            scenario.ReproductionId,
            new { status = "Cancelled", confirmed = true });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedStatus.StatusCode);

        var viewerId = await RegisterAndAuthenticateAsync(
            scenario.Factory,
            viewerClient,
            "reproduction-lifecycle-viewer@example.com");
        await AddViewerMembershipAsync(scenario.Factory, scenario.FarmId, viewerId);
        using var viewerUpdate = await SendAsync(
            viewerClient,
            HttpMethod.Put,
            $"/api/reproductions/{scenario.ReproductionId}",
            new { notes = "Sem permissão" });
        Assert.Equal(HttpStatusCode.NotFound, viewerUpdate.StatusCode);

        await RegisterAndAuthenticateAsync(
            scenario.Factory,
            otherClient,
            "reproduction-lifecycle-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient, "Other reproduction farm", "other@example.com");
        await SelectFarmAsync(otherClient, otherFarmId);
        using var crossTenantStatus = await SendStatusAsync(
            otherClient,
            scenario.ReproductionId,
            new { status = "Cancelled", confirmed = true });
        Assert.Equal(HttpStatusCode.NotFound, crossTenantStatus.StatusCode);

        using var ownerDetails = await scenario.Client.GetAsync($"/api/reproductions/{scenario.ReproductionId}");
        Assert.Equal(HttpStatusCode.OK, ownerDetails.StatusCode);
        using var ownerBody = JsonDocument.Parse(await ownerDetails.Content.ReadAsStreamAsync());
        Assert.Equal("Active", ownerBody.RootElement.GetProperty("status").GetString());
        Assert.Equal("Período inicial", ownerBody.RootElement.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task ConcurrentTerminalTransitionsAllowOneWinnerAndKeepAConsistentTerminalState()
    {
        await using var scenario = await CreateScenarioAsync("reproduction-lifecycle-concurrency@example.com");
        using var secondClient = CreateClient(scenario.Factory);
        await LoginExistingAsync(
            secondClient,
            "reproduction-lifecycle-concurrency@example.com");

        var responses = await Task.WhenAll(
            SendStatusAsync(
                scenario.Client,
                scenario.ReproductionId,
                new { status = "Finished", confirmed = true, endDate = "2026-09-06" }),
            SendStatusAsync(
                secondClient,
                scenario.ReproductionId,
                new { status = "Cancelled", confirmed = true }));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        foreach (var response in responses)
        {
            response.Dispose();
        }

        using var details = await scenario.Client.GetAsync($"/api/reproductions/{scenario.ReproductionId}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        using var detailBody = JsonDocument.Parse(await details.Content.ReadAsStreamAsync());
        var status = detailBody.RootElement.GetProperty("status").GetString();
        Assert.Contains(status, new[] { "Finished", "Cancelled" });
        if (status == "Finished")
        {
            Assert.Equal("2026-09-06", detailBody.RootElement.GetProperty("endDate").GetString());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, detailBody.RootElement.GetProperty("endDate").ValueKind);
        }
    }

    private static async Task<Scenario> CreateScenarioAsync(string email)
    {
        var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        var certificate = TestCertificate.Create();
        var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, email);
        var farmId = await CreateFarmAsync(client, "Lifecycle farm", email);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var maleBirdId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Male, "Macho inicial", "700001");
        var femaleBirdId = await AddBirdAsync(factory, farmId, speciesId, BirdSex.Female, "Fêmea inicial", "700002");
        var reproductionId = await CreateReproductionAsync(client, maleBirdId, femaleBirdId);
        return new Scenario(database, certificate, factory, client, farmId, speciesId, reproductionId);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object body)
    {
        return await client.SendAsync(CreateBrowserRequest(
            method,
            path,
            await GetAntiforgeryTokenAsync(client),
            body));
    }

    private static async Task<HttpResponseMessage> SendStatusAsync(
        HttpClient client,
        Guid reproductionId,
        object body) =>
        await SendAsync(client, HttpMethod.Patch, $"/api/reproductions/{reproductionId}/status", body);

    private static async Task<Guid> CreateReproductionAsync(
        HttpClient client,
        Guid maleBirdId,
        Guid femaleBirdId)
    {
        using var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/reproductions",
            new
            {
                maleBirdId,
                femaleBirdId,
                startDate = "2026-09-01",
                notes = "Período inicial"
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("reproductionId").GetGuid();
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

        await LoginExistingAsync(client, email);
        return userId;
    }

    private static async Task LoginExistingAsync(HttpClient client, string email)
    {
        using var login = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/login",
            await GetAntiforgeryTokenAsync(client),
            new { email, password = "StrongPassword!123" }));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
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
                responsibleName = "Lifecycle Owner",
                contactEmail
            }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("breedingFarmId").GetGuid();
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

    private sealed class Scenario(
        PostgreSqlContainer database,
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate,
        WebApplicationFactory<Program> factory,
        HttpClient client,
        Guid farmId,
        Guid speciesId,
        Guid reproductionId) : IAsyncDisposable
    {
        public PostgreSqlContainer Database { get; } = database;
        public System.Security.Cryptography.X509Certificates.X509Certificate2 Certificate { get; } = certificate;
        public WebApplicationFactory<Program> Factory { get; } = factory;
        public HttpClient Client { get; } = client;
        public Guid FarmId { get; } = farmId;
        public Guid SpeciesId { get; } = speciesId;
        public Guid ReproductionId { get; } = reproductionId;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            Factory.Dispose();
            Certificate.Dispose();
            await Database.DisposeAsync();
        }
    }
}
