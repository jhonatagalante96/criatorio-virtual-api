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

public sealed class GlobalCompetitionQueryEndpointTests
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

        using var unauthenticatedList = await client.GetAsync("/api/competitions");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedList.StatusCode);

        await RegisterAndAuthenticateAsync(factory, client, "global-competition-auth@example.com");

        using var withoutFarmList = await client.GetAsync("/api/competitions");
        Assert.Equal(HttpStatusCode.Conflict, withoutFarmList.StatusCode);
    }

    [Fact]
    public async Task QueryEnforcesTenantIsolation()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var tenantAClient = CreateClient(factory);
        using var tenantBClient = CreateClient(factory);

        await RegisterAndAuthenticateAsync(factory, tenantAClient, "tenant-a-competitions@example.com");
        var farmAId = await CreateFarmAsync(tenantAClient, "Criatório A", "Responsável A");
        await SelectFarmAsync(tenantAClient, farmAId);

        await RegisterAndAuthenticateAsync(factory, tenantBClient, "tenant-b-competitions@example.com");
        var farmBId = await CreateFarmAsync(tenantBClient, "Criatório B", "Responsável B");
        await SelectFarmAsync(tenantBClient, farmBId);

        var speciesId = await GetSpeciesIdAsync(factory);

        var birdAId = await CreateBirdAsync(tenantAClient, speciesId, "Ave A", "111111");
        var compAId = await CreateCompetitionAsync(tenantAClient, birdAId, new
        {
            name = "Competição Tenant A",
            date = "2026-09-01",
            category = "Canto"
        });

        var birdBId = await CreateBirdAsync(tenantBClient, speciesId, "Ave B", "222222");
        var compBId = await CreateCompetitionAsync(tenantBClient, birdBId, new
        {
            name = "Competição Tenant B",
            date = "2026-09-02",
            category = "Fibra"
        });

        using var responseA = await tenantAClient.GetAsync("/api/competitions");
        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        using var docA = JsonDocument.Parse(await responseA.Content.ReadAsStreamAsync());
        var itemsA = docA.RootElement.GetProperty("items");
        Assert.Equal(1, itemsA.GetArrayLength());
        Assert.Equal(compAId, itemsA[0].GetProperty("competitionId").GetGuid());
        Assert.Equal("Competição Tenant A", itemsA[0].GetProperty("name").GetString());
        Assert.Equal(birdAId, itemsA[0].GetProperty("bird").GetProperty("birdId").GetGuid());

        using var responseB = await tenantBClient.GetAsync("/api/competitions");
        Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);
        using var docB = JsonDocument.Parse(await responseB.Content.ReadAsStreamAsync());
        var itemsB = docB.RootElement.GetProperty("items");
        Assert.Equal(1, itemsB.GetArrayLength());
        Assert.Equal(compBId, itemsB[0].GetProperty("competitionId").GetGuid());
        Assert.Equal("Competição Tenant B", itemsB[0].GetProperty("name").GetString());
        Assert.Equal(birdBId, itemsB[0].GetProperty("bird").GetProperty("birdId").GetGuid());
    }

    [Fact]
    public async Task QuerySupportsPaginationAndDeterministicOrdering()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        await RegisterAndAuthenticateAsync(factory, client, "pagination-competitions@example.com");
        var farmId = await CreateFarmAsync(client, "Criatório Paginação", "Responsável Pag");
        await SelectFarmAsync(client, farmId);

        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(client, speciesId, "Ave Veloz", "333333");

        // 1. Older date
        var compOlder = await CreateCompetitionAsync(client, birdId, new
        {
            name = "Competição Antiga",
            date = "2026-09-01"
        });
        // 2. Newer date
        var compNewer = await CreateCompetitionAsync(client, birdId, new
        {
            name = "Competição Mais Recente",
            date = "2026-09-10"
        });
        // 3. No date, created earlier
        var compNoDate1 = await CreateCompetitionAsync(client, birdId, new
        {
            name = "Competição Sem Data 1"
        });
        // 4. Middle date
        var compMiddle = await CreateCompetitionAsync(client, birdId, new
        {
            name = "Competição Intermediária",
            date = "2026-09-05"
        });
        // 5. No date, created later
        var compNoDate2 = await CreateCompetitionAsync(client, birdId, new
        {
            name = "Competição Sem Data 2"
        });

        // Expected order:
        // 1) compNewer (2026-09-10)
        // 2) compMiddle (2026-09-05)
        // 3) compOlder (2026-09-01)
        // 4) compNoDate2 (no date, created after compNoDate1)
        // 5) compNoDate1 (no date, created first)

        // Page 1 with pageSize = 2
        using var page1Response = await client.GetAsync("/api/competitions?page=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, page1Response.StatusCode);
        using var page1Doc = JsonDocument.Parse(await page1Response.Content.ReadAsStreamAsync());
        Assert.Equal(5, page1Doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, page1Doc.RootElement.GetProperty("totalPages").GetInt32());
        Assert.Equal(1, page1Doc.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(2, page1Doc.RootElement.GetProperty("pageSize").GetInt32());
        var page1Items = page1Doc.RootElement.GetProperty("items");
        Assert.Equal(2, page1Items.GetArrayLength());
        Assert.Equal(compNewer, page1Items[0].GetProperty("competitionId").GetGuid());
        Assert.Equal(compMiddle, page1Items[1].GetProperty("competitionId").GetGuid());

        // Page 2 with pageSize = 2
        using var page2Response = await client.GetAsync("/api/competitions?page=2&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, page2Response.StatusCode);
        using var page2Doc = JsonDocument.Parse(await page2Response.Content.ReadAsStreamAsync());
        var page2Items = page2Doc.RootElement.GetProperty("items");
        Assert.Equal(2, page2Items.GetArrayLength());
        Assert.Equal(compOlder, page2Items[0].GetProperty("competitionId").GetGuid());
        Assert.Equal(compNoDate2, page2Items[1].GetProperty("competitionId").GetGuid());

        // Page 3 with pageSize = 2
        using var page3Response = await client.GetAsync("/api/competitions?page=3&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, page3Response.StatusCode);
        using var page3Doc = JsonDocument.Parse(await page3Response.Content.ReadAsStreamAsync());
        var page3Items = page3Doc.RootElement.GetProperty("items");
        Assert.Equal(1, page3Items.GetArrayLength());
        Assert.Equal(compNoDate1, page3Items[0].GetProperty("competitionId").GetGuid());
    }

    [Fact]
    public async Task QueryFiltersByBirdIdCategoryDateRangeAndSearchHandlingBirdWithoutRing()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        await RegisterAndAuthenticateAsync(factory, client, "filter-competitions@example.com");
        var farmId = await CreateFarmAsync(client, "Criatório Filtros", "Responsável Filtros");
        await SelectFarmAsync(client, farmId);

        var speciesId = await GetSpeciesIdAsync(factory);

        // Bird 1: has ring number
        var bird1Id = await CreateBirdAsync(client, speciesId, "Canário Dourado", "123456");
        var compAId = await CreateCompetitionAsync(client, bird1Id, new
        {
            name = "Copa Sul de Canto",
            date = "2026-08-10",
            category = "Canto Clássico",
            location = "Curitiba",
            placement = 1
        });

        // Bird 2: ave sem anilha (ringNumber = null)
        var bird2Id = await CreateBirdAsync(client, speciesId, "Azulão Bravo", null);
        var compBId = await CreateCompetitionAsync(client, bird2Id, new
        {
            name = "Torneio Master Brasil",
            date = "2026-08-20",
            category = "Fibra Velocidade",
            location = "Florianópolis",
            placement = 3
        });

        // 1. Filter by birdId
        using var birdFilterResponse = await client.GetAsync($"/api/competitions?birdId={bird1Id}");
        Assert.Equal(HttpStatusCode.OK, birdFilterResponse.StatusCode);
        using var birdDoc = JsonDocument.Parse(await birdFilterResponse.Content.ReadAsStreamAsync());
        var birdItems = birdDoc.RootElement.GetProperty("items");
        Assert.Equal(1, birdItems.GetArrayLength());
        Assert.Equal(compAId, birdItems[0].GetProperty("competitionId").GetGuid());
        Assert.Equal("123456", birdItems[0].GetProperty("bird").GetProperty("ringNumber").GetString());

        // 2. Filter by category
        using var categoryResponse = await client.GetAsync("/api/competitions?category=Fibra%20Velocidade");
        Assert.Equal(HttpStatusCode.OK, categoryResponse.StatusCode);
        using var categoryDoc = JsonDocument.Parse(await categoryResponse.Content.ReadAsStreamAsync());
        var categoryItems = categoryDoc.RootElement.GetProperty("items");
        Assert.Equal(1, categoryItems.GetArrayLength());
        Assert.Equal(compBId, categoryItems[0].GetProperty("competitionId").GetGuid());
        // Verify bird without ring returns null ringNumber
        Assert.Equal(JsonValueKind.Null, categoryItems[0].GetProperty("bird").GetProperty("ringNumber").ValueKind);
        Assert.Equal("Azulão Bravo", categoryItems[0].GetProperty("bird").GetProperty("name").GetString());

        // 3. Filter by date range
        using var dateRangeResponse = await client.GetAsync("/api/competitions?fromDate=2026-08-15&toDate=2026-08-25");
        Assert.Equal(HttpStatusCode.OK, dateRangeResponse.StatusCode);
        using var dateRangeDoc = JsonDocument.Parse(await dateRangeResponse.Content.ReadAsStreamAsync());
        var dateRangeItems = dateRangeDoc.RootElement.GetProperty("items");
        Assert.Equal(1, dateRangeItems.GetArrayLength());
        Assert.Equal(compBId, dateRangeItems[0].GetProperty("competitionId").GetGuid());

        // 4. Search by bird name
        using var searchBirdResponse = await client.GetAsync("/api/competitions?search=Dourado");
        Assert.Equal(HttpStatusCode.OK, searchBirdResponse.StatusCode);
        using var searchBirdDoc = JsonDocument.Parse(await searchBirdResponse.Content.ReadAsStreamAsync());
        var searchBirdItems = searchBirdDoc.RootElement.GetProperty("items");
        Assert.Equal(1, searchBirdItems.GetArrayLength());
        Assert.Equal(compAId, searchBirdItems[0].GetProperty("competitionId").GetGuid());

        // 5. Search by bird ring
        using var searchRingResponse = await client.GetAsync("/api/competitions?search=123");
        Assert.Equal(HttpStatusCode.OK, searchRingResponse.StatusCode);
        using var searchRingDoc = JsonDocument.Parse(await searchRingResponse.Content.ReadAsStreamAsync());
        var searchRingItems = searchRingDoc.RootElement.GetProperty("items");
        Assert.Equal(1, searchRingItems.GetArrayLength());
        Assert.Equal(compAId, searchRingItems[0].GetProperty("competitionId").GetGuid());

        // 6. Search by competition location
        using var searchLocationResponse = await client.GetAsync("/api/competitions?search=Florianópolis");
        Assert.Equal(HttpStatusCode.OK, searchLocationResponse.StatusCode);
        using var searchLocDoc = JsonDocument.Parse(await searchLocationResponse.Content.ReadAsStreamAsync());
        var searchLocItems = searchLocDoc.RootElement.GetProperty("items");
        Assert.Equal(1, searchLocItems.GetArrayLength());
        Assert.Equal(compBId, searchLocItems[0].GetProperty("competitionId").GetGuid());
    }

    [Fact]
    public async Task QueryValidatesInvalidParameters()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        await RegisterAndAuthenticateAsync(factory, client, "invalid-params-competitions@example.com");
        var farmId = await CreateFarmAsync(client, "Criatório Params", "Responsável Params");
        await SelectFarmAsync(client, farmId);

        // Page < 1
        using var pageZero = await client.GetAsync("/api/competitions?page=0");
        Assert.Equal(HttpStatusCode.BadRequest, pageZero.StatusCode);

        // PageSize < 1
        using var pageSizeZero = await client.GetAsync("/api/competitions?pageSize=0");
        Assert.Equal(HttpStatusCode.BadRequest, pageSizeZero.StatusCode);

        // PageSize > 100
        using var pageSizeTooLarge = await client.GetAsync("/api/competitions?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, pageSizeTooLarge.StatusCode);

        // Empty birdId
        using var emptyBirdId = await client.GetAsync("/api/competitions?birdId=00000000-0000-0000-0000-000000000000");
        Assert.Equal(HttpStatusCode.BadRequest, emptyBirdId.StatusCode);

        // fromDate > toDate
        using var invalidRange = await client.GetAsync("/api/competitions?fromDate=2026-09-10&toDate=2026-09-01");
        Assert.Equal(HttpStatusCode.BadRequest, invalidRange.StatusCode);

        // Search > 100 chars
        var longSearch = new string('a', 101);
        using var searchTooLong = await client.GetAsync($"/api/competitions?search={longSearch}");
        Assert.Equal(HttpStatusCode.BadRequest, searchTooLong.StatusCode);

        // Category > 200 chars
        var longCategory = new string('c', 201);
        using var categoryTooLong = await client.GetAsync($"/api/competitions?category={longCategory}");
        Assert.Equal(HttpStatusCode.BadRequest, categoryTooLong.StatusCode);
    }

    [Fact]
    public async Task QueryPreservesTransferredBirdCompetitionsForNewOwnerAndHidesThemFromSourceTenant()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var sourceClient = CreateClient(factory);
        using var destinationClient = CreateClient(factory);

        await RegisterAndAuthenticateAsync(factory, sourceClient, "transfer-source@example.com");
        var sourceFarmId = await CreateFarmAsync(sourceClient, "Criatório Origem", "Dono Origem");
        await SelectFarmAsync(sourceClient, sourceFarmId);

        await RegisterAndAuthenticateAsync(factory, destinationClient, "transfer-destination@example.com");
        var destinationFarmId = await CreateFarmAsync(destinationClient, "Criatório Destino", "Dono Destino");
        await SelectFarmAsync(destinationClient, destinationFarmId);

        var speciesId = await GetSpeciesIdAsync(factory);

        // 1. Criar ave + competição no tenant A
        var birdId = await CreateBirdAsync(sourceClient, speciesId, "Ave Campeã Transferida", "654321");
        var compId = await CreateCompetitionAsync(sourceClient, birdId, new
        {
            name = "Torneio Nacional de Ouro",
            date = "2026-08-15",
            category = "Canto Clássico",
            placement = 1
        });

        // Verificar que tenant A vê a competição antes da transferência
        using var sourceBeforeTransfer = await sourceClient.GetAsync("/api/competitions");
        Assert.Equal(HttpStatusCode.OK, sourceBeforeTransfer.StatusCode);
        using var sourceBeforeDoc = JsonDocument.Parse(await sourceBeforeTransfer.Content.ReadAsStreamAsync());
        var sourceBeforeItems = sourceBeforeDoc.RootElement.GetProperty("items");
        Assert.Equal(1, sourceBeforeItems.GetArrayLength());
        Assert.Equal(compId, sourceBeforeItems[0].GetProperty("competitionId").GetGuid());

        // 2. Transferir a ave para o tenant B
        using var transferRequest = await RequestTransferAsync(sourceClient, birdId, destinationFarmId);
        Assert.Equal(HttpStatusCode.Created, transferRequest.StatusCode);
        using var transferBody = JsonDocument.Parse(await transferRequest.Content.ReadAsStreamAsync());
        var transferRequestId = transferBody.RootElement.GetProperty("transferRequestId").GetGuid();

        using var acceptedTransfer = await AcceptTransferAsync(destinationClient, transferRequestId);
        Assert.Equal(HttpStatusCode.OK, acceptedTransfer.StatusCode);

        // 3. GET /api/competitions em A não retorna a competição
        using var sourceAfterTransfer = await sourceClient.GetAsync("/api/competitions");
        Assert.Equal(HttpStatusCode.OK, sourceAfterTransfer.StatusCode);
        using var sourceAfterDoc = JsonDocument.Parse(await sourceAfterTransfer.Content.ReadAsStreamAsync());
        var sourceAfterItems = sourceAfterDoc.RootElement.GetProperty("items");
        Assert.Equal(0, sourceAfterItems.GetArrayLength());
        Assert.Equal(0, sourceAfterDoc.RootElement.GetProperty("totalCount").GetInt32());

        // 4. GET /api/competitions em B retorna a competição e o resumo da ave
        using var destinationAfterTransfer = await destinationClient.GetAsync("/api/competitions");
        Assert.Equal(HttpStatusCode.OK, destinationAfterTransfer.StatusCode);
        using var destinationAfterDoc = JsonDocument.Parse(await destinationAfterTransfer.Content.ReadAsStreamAsync());
        var destinationAfterItems = destinationAfterDoc.RootElement.GetProperty("items");
        Assert.Equal(1, destinationAfterItems.GetArrayLength());
        var returnedComp = destinationAfterItems[0];
        Assert.Equal(compId, returnedComp.GetProperty("competitionId").GetGuid());
        Assert.Equal(destinationFarmId, returnedComp.GetProperty("breedingFarmId").GetGuid());
        Assert.Equal("Torneio Nacional de Ouro", returnedComp.GetProperty("name").GetString());
        Assert.Equal(birdId, returnedComp.GetProperty("bird").GetProperty("birdId").GetGuid());
        Assert.Equal("Ave Campeã Transferida", returnedComp.GetProperty("bird").GetProperty("name").GetString());
        Assert.Equal("654321", returnedComp.GetProperty("bird").GetProperty("ringNumber").GetString());
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

    private static async Task<Guid> CreateBirdAsync(
        HttpClient client,
        Guid speciesId,
        string name,
        string? ringNumber)
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
