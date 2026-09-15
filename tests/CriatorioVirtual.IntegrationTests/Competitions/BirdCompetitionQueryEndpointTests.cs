using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Api;
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

public sealed class BirdCompetitionQueryEndpointTests
{
    [Fact]
    public async Task QueryRequiresAuthenticationAndASelectedBreedingFarm()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        var birdId = Guid.NewGuid();
        var competitionId = Guid.NewGuid();

        using var unauthenticatedList = await client.GetAsync($"/api/birds/{birdId}/competitions");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedList.StatusCode);

        using var unauthenticatedDetail = await client.GetAsync(
            $"/api/birds/{birdId}/competitions/{competitionId}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedDetail.StatusCode);

        await RegisterAndAuthenticateAsync(factory, client, "competition-query-auth@example.com");

        using var withoutFarmList = await client.GetAsync($"/api/birds/{birdId}/competitions");
        Assert.Equal(HttpStatusCode.Conflict, withoutFarmList.StatusCode);

        using var withoutFarmDetail = await client.GetAsync(
            $"/api/birds/{birdId}/competitions/{competitionId}");
        Assert.Equal(HttpStatusCode.Conflict, withoutFarmDetail.StatusCode);
    }

    [Fact]
    public async Task QueryPreservesCompetitionHistoryAfterTransferAndKeepsTenantIsolation()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var sourceClient = CreateClient(factory);
        using var destinationClient = CreateClient(factory);
        using var thirdTenantClient = CreateClient(factory);

        await RegisterAndAuthenticateAsync(factory, sourceClient, "competition-query-source@example.com");
        var sourceFarmId = await CreateFarmAsync(sourceClient, "Origem Competição", "Responsável Origem");
        await SelectFarmAsync(sourceClient, sourceFarmId);

        await RegisterAndAuthenticateAsync(
            factory,
            destinationClient,
            "competition-query-destination@example.com");
        var destinationFarmId = await CreateFarmAsync(
            destinationClient,
            "Destino Competição",
            "Responsável Destino");
        await SelectFarmAsync(destinationClient, destinationFarmId);

        await RegisterAndAuthenticateAsync(
            factory,
            thirdTenantClient,
            "competition-query-third-tenant@example.com");
        var thirdTenantFarmId = await CreateFarmAsync(
            thirdTenantClient,
            "Terceiro Competição",
            "Responsável Terceiro");
        await SelectFarmAsync(thirdTenantClient, thirdTenantFarmId);

        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(sourceClient, speciesId, "Ave campeã", "734567");
        var firstCompetitionId = await CreateCompetitionAsync(
            sourceClient,
            birdId,
            new
            {
                name = "Campeonato Estadual",
                date = "2026-09-06",
                category = "Livre",
                placement = 2,
                location = "Macaé",
                notes = "Final estadual"
            });
        var secondCompetitionId = await CreateCompetitionAsync(
            sourceClient,
            birdId,
            new
            {
                name = "Copa Nacional",
                date = "2026-09-07",
                category = "Azul",
                placement = 1,
                location = "São Paulo",
                notes = "Grande final"
            });

        using var sourceBeforeTransfer = await sourceClient.GetAsync(
            $"/api/birds/{birdId}/competitions");
        Assert.Equal(HttpStatusCode.OK, sourceBeforeTransfer.StatusCode);
        using var sourceBeforeTransferBody = JsonDocument.Parse(
            await sourceBeforeTransfer.Content.ReadAsStreamAsync());
        Assert.Equal(2, sourceBeforeTransferBody.RootElement.GetProperty("items").GetArrayLength());

        using var transferRequest = await RequestTransferAsync(sourceClient, birdId, destinationFarmId);
        Assert.Equal(HttpStatusCode.Created, transferRequest.StatusCode);
        using var transferBody = JsonDocument.Parse(await transferRequest.Content.ReadAsStreamAsync());
        var transferRequestId = transferBody.RootElement.GetProperty("transferRequestId").GetGuid();

        using var acceptedTransfer = await AcceptTransferAsync(destinationClient, transferRequestId);
        Assert.Equal(HttpStatusCode.OK, acceptedTransfer.StatusCode);

        using var destinationList = await destinationClient.GetAsync(
            $"/api/birds/{birdId}/competitions");
        Assert.Equal(HttpStatusCode.OK, destinationList.StatusCode);
        using var destinationListBody = JsonDocument.Parse(
            await destinationList.Content.ReadAsStreamAsync());
        var destinationListRoot = destinationListBody.RootElement;
        Assert.Equal(destinationFarmId, destinationListRoot.GetProperty("breedingFarmId").GetGuid());
        Assert.Equal(birdId, destinationListRoot.GetProperty("birdId").GetGuid());
        var items = destinationListRoot.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal(secondCompetitionId, items[0].GetProperty("competitionId").GetGuid());
        Assert.Equal("Copa Nacional", items[0].GetProperty("name").GetString());
        Assert.Equal(firstCompetitionId, items[1].GetProperty("competitionId").GetGuid());
        Assert.Equal("Campeonato Estadual", items[1].GetProperty("name").GetString());

        using var destinationDetail = await destinationClient.GetAsync(
            $"/api/birds/{birdId}/competitions/{firstCompetitionId}");
        Assert.Equal(HttpStatusCode.OK, destinationDetail.StatusCode);
        using var destinationDetailBody = JsonDocument.Parse(
            await destinationDetail.Content.ReadAsStreamAsync());
        Assert.Equal(firstCompetitionId, destinationDetailBody.RootElement.GetProperty("competitionId").GetGuid());
        Assert.Equal("2026-09-06", destinationDetailBody.RootElement.GetProperty("date").GetString());
        Assert.Equal("Final estadual", destinationDetailBody.RootElement.GetProperty("notes").GetString());

        using var sourceAfterTransfer = await sourceClient.GetAsync(
            $"/api/birds/{birdId}/competitions");
        Assert.Equal(HttpStatusCode.NotFound, sourceAfterTransfer.StatusCode);

        using var sourceDetailAfterTransfer = await sourceClient.GetAsync(
            $"/api/birds/{birdId}/competitions/{firstCompetitionId}");
        Assert.Equal(HttpStatusCode.NotFound, sourceDetailAfterTransfer.StatusCode);

        using var thirdTenantList = await thirdTenantClient.GetAsync(
            $"/api/birds/{birdId}/competitions");
        Assert.Equal(HttpStatusCode.NotFound, thirdTenantList.StatusCode);
    }

    private static async Task<Guid> CreateCompetitionAsync(
        HttpClient client,
        Guid birdId,
        object body)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/birds/{birdId}/competitions",
            await GetAntiforgeryTokenAsync(client),
            body));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("competitionId").GetGuid();
    }

    private static async Task<HttpResponseMessage> RequestTransferAsync(
        HttpClient client,
        Guid birdId,
        Guid destinationFarmId) =>
        await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/internal-transfers",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                birdId,
                destinationBreedingFarmId = destinationFarmId,
                confirmed = true
            }));

    private static async Task<HttpResponseMessage> AcceptTransferAsync(
        HttpClient client,
        Guid transferRequestId) =>
        await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/internal-transfers/{transferRequestId}/accept",
            await GetAntiforgeryTokenAsync(client)));

    private static async Task<Guid> CreateBirdAsync(
        HttpClient client,
        Guid speciesId,
        string name,
        string ringNumber)
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
                ringNumber
            }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("birdId").GetGuid();
    }

    private static async Task<Guid> CreateFarmAsync(
        HttpClient client,
        string name,
        string responsibleName)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/breeding-farms",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                name,
                responsibleName,
                contactEmail = $"farm-{Guid.NewGuid():N}@example.com"
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
            .Where(species => species.IsActive)
            .Select(species => species.Id)
            .FirstAsync();
    }

    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        await dbContext.Database.MigrateAsync();
    }

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
