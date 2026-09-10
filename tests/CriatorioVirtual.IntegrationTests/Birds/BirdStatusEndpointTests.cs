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

public sealed class BirdStatusEndpointTests
{
    [Fact]
    public async Task ArchivePreservesHistoricalConsultationAndOwnership()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "bird-status-archive@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(client, speciesId, "Arquivada", "123456", "Notas preservadas");

        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Patch,
            $"/api/birds/{birdId}/status",
            await GetAntiforgeryTokenAsync(client),
            new { status = "Archived", confirmed = true }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal(birdId, body.RootElement.GetProperty("birdId").GetGuid());
        Assert.Equal(farmId, body.RootElement.GetProperty("breedingFarmId").GetGuid());
        Assert.Equal("Archived", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("Notas preservadas", body.RootElement.GetProperty("notes").GetString());

        using var details = await client.GetAsync($"/api/birds/{birdId}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        using var detailBody = JsonDocument.Parse(await details.Content.ReadAsStreamAsync());
        Assert.Equal("Archived", detailBody.RootElement.GetProperty("status").GetString());
        Assert.Equal("Arquivada", detailBody.RootElement.GetProperty("name").GetString());

        using var list = await client.GetAsync("/api/birds?status=Archived");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listBody = JsonDocument.Parse(await list.Content.ReadAsStreamAsync());
        var item = listBody.RootElement.GetProperty("items")
            .EnumerateArray()
            .Single(candidate => candidate.GetProperty("birdId").GetGuid() == birdId);
        Assert.Equal("Archived", item.GetProperty("status").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var bird = await dbContext.Birds.SingleAsync(candidate => candidate.Id == birdId);
        Assert.Equal(BirdStatus.Archived, bird.Status);
        Assert.Equal(farmId, bird.BreedingFarmId);
        Assert.Null(bird.DeathDate);
    }

    [Fact]
    public async Task DeathRequiresConfirmationAndDateThenStoresOptionalObservation()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "bird-status-death@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(client, speciesId, "Falecida", "223344");

        using var unconfirmed = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Patch,
            $"/api/birds/{birdId}/status",
            await GetAntiforgeryTokenAsync(client),
            new { status = "Deceased", confirmed = false, deathDate = "2026-09-09" }));
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        Assert.Contains("confirmed", await unconfirmed.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using var missingDate = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Patch,
            $"/api/birds/{birdId}/status",
            await GetAntiforgeryTokenAsync(client),
            new { status = "Deceased", confirmed = true }));
        Assert.Equal(HttpStatusCode.BadRequest, missingDate.StatusCode);
        Assert.Contains("deathDate", await missingDate.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using var futureDate = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Patch,
            $"/api/birds/{birdId}/status",
            await GetAntiforgeryTokenAsync(client),
            new { status = "Deceased", confirmed = true, deathDate = "2999-01-01" }));
        Assert.Equal(HttpStatusCode.BadRequest, futureDate.StatusCode);

        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Patch,
            $"/api/birds/{birdId}/status",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                status = "Deceased",
                confirmed = true,
                deathDate = "2026-09-09",
                notes = "  Observação do falecimento  "
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal("Deceased", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("2026-09-09", body.RootElement.GetProperty("deathDate").GetString());
        Assert.Equal("Observação do falecimento", body.RootElement.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task EscapeRequiresConfirmationAndBlocksFurtherStatusChanges()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "bird-status-escape@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(client, speciesId, "Escapada", "334455");

        using var unconfirmed = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Patch,
            $"/api/birds/{birdId}/status",
            await GetAntiforgeryTokenAsync(client),
            new { status = "Escaped", confirmed = false }));
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);

        using var escaped = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Patch,
            $"/api/birds/{birdId}/status",
            await GetAntiforgeryTokenAsync(client),
            new { status = "Escaped", confirmed = true }));
        Assert.Equal(HttpStatusCode.OK, escaped.StatusCode);

        using var repeated = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Patch,
            $"/api/birds/{birdId}/status",
            await GetAntiforgeryTokenAsync(client),
            new { status = "Archived", confirmed = true }));
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        Assert.Contains("current state", await repeated.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using var unsupported = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Patch,
            $"/api/birds/{birdId}/status",
            await GetAntiforgeryTokenAsync(client),
            new { status = "Active", confirmed = true }));
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
    }

    [Fact]
    public async Task StatusChangeRequiresAuthenticationSelectionAndTenantOwnership()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = Guid.NewGuid();

        using var unauthenticated = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Patch,
            $"/api/birds/{birdId}/status",
            await GetAntiforgeryTokenAsync(client),
            new { status = "Archived", confirmed = true }));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await RegisterAndAuthenticateAsync(factory, client, "bird-status-no-farm@example.com");
        using var withoutFarm = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Patch,
            $"/api/birds/{birdId}/status",
            await GetAntiforgeryTokenAsync(client),
            new { status = "Archived", confirmed = true }));
        Assert.Equal(HttpStatusCode.Conflict, withoutFarm.StatusCode);

        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "bird-status-owner@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);
        var ownerBirdId = await CreateBirdAsync(ownerClient, speciesId, "Privada", "445566");

        await RegisterAndAuthenticateAsync(factory, otherClient, "bird-status-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient);
        await SelectFarmAsync(otherClient, otherFarmId);
        using var crossTenant = await otherClient.SendAsync(CreateBrowserRequest(
            HttpMethod.Patch,
            $"/api/birds/{ownerBirdId}/status",
            await GetAntiforgeryTokenAsync(otherClient),
            new { status = "Archived", confirmed = true }));
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);

        using var missing = await ownerClient.SendAsync(CreateBrowserRequest(
            HttpMethod.Patch,
            $"/api/birds/{Guid.NewGuid()}/status",
            await GetAntiforgeryTokenAsync(ownerClient),
            new { status = "Archived", confirmed = true }));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task StatusChangeIsBlockedForTransferredBirds()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "bird-status-transfer@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(client, speciesId, "Em transferência", "556677");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var bird = await dbContext.Birds.SingleAsync(candidate => candidate.Id == birdId);
            dbContext.Entry(bird).Property(candidate => candidate.Status).CurrentValue = BirdStatus.Transferred;
            await dbContext.SaveChangesAsync();
        }

        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Patch,
            $"/api/birds/{birdId}/status",
            await GetAntiforgeryTokenAsync(client),
            new { status = "Archived", confirmed = true }));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("transfer", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Guid> CreateBirdAsync(
        HttpClient client,
        Guid speciesId,
        string name,
        string ringNumber,
        string? notes = null)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/birds",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                name,
                sex = "Female",
                speciesId,
                birthDate = "2020-09-07",
                ringNumber,
                notes
            }));
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
}
