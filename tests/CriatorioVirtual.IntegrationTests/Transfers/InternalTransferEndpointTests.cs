using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Domain.Birds;
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

namespace CriatorioVirtual.IntegrationTests.Transfers;

public sealed class InternalTransferEndpointTests
{
    [Fact]
    public async Task DestinationSearchIsPartialPaginatedMinimalAndAllowsFarmWithoutOfficialRegistration()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var sourceClient = CreateClient(factory);
        using var destinationClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, sourceClient, "transfer-search-source@example.com");
        var sourceFarmId = await CreateFarmAsync(sourceClient, "Criatório Origem", "Responsável Origem", "SRC-001");
        await SelectFarmAsync(sourceClient, sourceFarmId);
        await RegisterAndAuthenticateAsync(factory, destinationClient, "transfer-search-destination@example.com");
        var destinationFarmId = await CreateFarmAsync(destinationClient, "Destino Aurora", "Responsável Azul");

        using var response = await sourceClient.GetAsync(
            "/api/internal-transfers/destinations?search=aurora&page=1&pageSize=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;
        Assert.Equal(sourceFarmId, root.GetProperty("sourceBreedingFarmId").GetGuid());
        Assert.Equal(1, root.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, root.GetProperty("totalPages").GetInt32());
        var destination = root.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(destinationFarmId, destination.GetProperty("breedingFarmId").GetGuid());
        Assert.Equal("Destino Aurora", destination.GetProperty("name").GetString());
        Assert.Equal("Responsável Azul", destination.GetProperty("responsibleName").GetString());
        Assert.False(destination.TryGetProperty("officialRegistrationNumber", out _));
    }

    [Fact]
    public async Task RequestValidatesConfirmationEligibilityAndPersistsPendingTransferAtomically()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "transfer-request-owner@example.com");
        var sourceFarmId = await CreateFarmAsync(client, "Origem", "Responsável Origem", "SRC-002");
        var destinationFarmId = await CreateFarmAsync(client, "Destino Sem Registro", "Responsável Destino");
        await SelectFarmAsync(client, sourceFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(client, speciesId, "Ave transferível", "765432");
        var missingRingBirdId = await CreateBirdAsync(client, speciesId, "Ave sem anilha", null);

        using var unconfirmed = await RequestTransferAsync(client, birdId, destinationFarmId, confirmed: false);
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        Assert.Contains("confirmation", await unconfirmed.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using var sameFarm = await RequestTransferAsync(client, birdId, sourceFarmId, confirmed: true);
        Assert.Equal(HttpStatusCode.BadRequest, sameFarm.StatusCode);

        using var missingRing = await RequestTransferAsync(client, missingRingBirdId, destinationFarmId, confirmed: true);
        Assert.Equal(HttpStatusCode.BadRequest, missingRing.StatusCode);
        Assert.Contains("ring", await missingRing.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using var created = await RequestTransferAsync(client, birdId, destinationFarmId, confirmed: true);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStreamAsync());
        Assert.Equal(birdId, createdBody.RootElement.GetProperty("birdId").GetGuid());
        Assert.Equal(sourceFarmId, createdBody.RootElement.GetProperty("sourceBreedingFarmId").GetGuid());
        Assert.Equal(destinationFarmId, createdBody.RootElement.GetProperty("destinationBreedingFarmId").GetGuid());
        Assert.Equal("Pending", createdBody.RootElement.GetProperty("status").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var bird = await dbContext.Birds.SingleAsync(candidate => candidate.Id == birdId);
        var transfer = await dbContext.InternalTransferRequests.SingleAsync(candidate => candidate.BirdId == birdId);
        Assert.Equal(BirdStatus.Transferred, bird.Status);
        Assert.Equal(InternalTransferRequestStatus.Pending, transfer.Status);
        Assert.Equal(sourceFarmId, transfer.SourceBreedingFarmId);
        Assert.Equal(destinationFarmId, transfer.DestinationBreedingFarmId);
        Assert.Equal(1, await dbContext.InternalTransferRequests.CountAsync());
    }

    [Fact]
    public async Task PendingTransferBlocksConcurrentBirdEditsAndRejectsCrossTenantSourceAccess()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "transfer-access-owner@example.com");
        var sourceFarmId = await CreateFarmAsync(ownerClient, "Origem Privada", "Dono Origem", "SRC-003");
        await SelectFarmAsync(ownerClient, sourceFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(ownerClient, speciesId, "Ave privada", "876543");

        await RegisterAndAuthenticateAsync(factory, otherClient, "transfer-access-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient, "Destino Outro Tenant", "Dono Outro");
        await SelectFarmAsync(otherClient, otherFarmId);

        using var crossTenant = await RequestTransferAsync(otherClient, birdId, sourceFarmId, confirmed: true);
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);

        using var created = await RequestTransferAsync(ownerClient, birdId, otherFarmId, confirmed: true);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var update = await ownerClient.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            $"/api/birds/{birdId}",
            await GetAntiforgeryTokenAsync(ownerClient),
            new
            {
                name = "Nome bloqueado",
                sex = "Female",
                speciesId,
                birthDate = "2020-09-07",
                ringNumber = "876543"
            }));
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        Assert.Contains("transfer", await update.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConcurrentRequestsForOneBirdLeaveExactlyOnePendingTransfer()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var firstClient = CreateClient(factory);
        using var secondClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, firstClient, "transfer-concurrency@example.com");
        var sourceFarmId = await CreateFarmAsync(firstClient, "Origem Concorrente", "Dono Concorrente", "SRC-004");
        var firstDestinationId = await CreateFarmAsync(firstClient, "Destino Concorrente A", "Responsável A");
        var secondDestinationId = await CreateFarmAsync(firstClient, "Destino Concorrente B", "Responsável B");
        await SelectFarmAsync(firstClient, sourceFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(firstClient, speciesId, "Ave concorrente", "987654");
        await AuthenticateExistingUserAsync(secondClient, "transfer-concurrency@example.com");

        var firstRequest = CreateBrowserRequest(
            HttpMethod.Post,
            "/api/internal-transfers",
            await GetAntiforgeryTokenAsync(firstClient),
            new { birdId, destinationBreedingFarmId = firstDestinationId, confirmed = true });
        var secondRequest = CreateBrowserRequest(
            HttpMethod.Post,
            "/api/internal-transfers",
            await GetAntiforgeryTokenAsync(secondClient),
            new { birdId, destinationBreedingFarmId = secondDestinationId, confirmed = true });

        var responses = await Task.WhenAll(
            firstClient.SendAsync(firstRequest),
            secondClient.SendAsync(secondRequest));
        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }

            firstRequest.Dispose();
            secondRequest.Dispose();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var pendingTransfers = await dbContext.InternalTransferRequests
            .Where(candidate => candidate.BirdId == birdId && candidate.Status == InternalTransferRequestStatus.Pending)
            .ToArrayAsync();
        var bird = await dbContext.Birds.SingleAsync(candidate => candidate.Id == birdId);
        Assert.Single(pendingTransfers);
        Assert.Equal(BirdStatus.Transferred, bird.Status);
        Assert.Contains(
            pendingTransfers[0].DestinationBreedingFarmId,
            new[] { firstDestinationId, secondDestinationId });
    }

    [Fact]
    public async Task ConcurrentTransferCreationAndBirdEditHaveOneSerializedWinner()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var transferClient = CreateClient(factory);
        using var editClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, transferClient, "transfer-edit-race@example.com");
        var sourceFarmId = await CreateFarmAsync(transferClient, "Origem Race", "Dono Race", "SRC-005");
        var destinationFarmId = await CreateFarmAsync(transferClient, "Destino Race", "Responsável Race");
        await SelectFarmAsync(transferClient, sourceFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(transferClient, speciesId, "Ave race", "112233");
        await AuthenticateExistingUserAsync(editClient, "transfer-edit-race@example.com");

        var transferRequest = CreateBrowserRequest(
            HttpMethod.Post,
            "/api/internal-transfers",
            await GetAntiforgeryTokenAsync(transferClient),
            new { birdId, destinationBreedingFarmId = destinationFarmId, confirmed = true });
        var editRequest = CreateBrowserRequest(
            HttpMethod.Put,
            $"/api/birds/{birdId}",
            await GetAntiforgeryTokenAsync(editClient),
            new
            {
                name = "Nome concorrente",
                sex = "Female",
                speciesId,
                birthDate = "2020-09-07",
                ringNumber = "112233"
            });

        var responses = await Task.WhenAll(
            transferClient.SendAsync(transferRequest),
            editClient.SendAsync(editRequest));
        var transferStatus = responses[0].StatusCode;
        var editStatus = responses[1].StatusCode;
        try
        {
            Assert.Contains(transferStatus, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });
            Assert.Contains(editStatus, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            Assert.True(
                transferStatus == HttpStatusCode.Created || editStatus == HttpStatusCode.OK,
                "At least one operation must win the serialization race.");
            if (transferStatus == HttpStatusCode.Created)
            {
                Assert.Contains(editStatus, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            }
            else
            {
                Assert.Equal(HttpStatusCode.OK, editStatus);
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }

            transferRequest.Dispose();
            editRequest.Dispose();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var bird = await dbContext.Birds.SingleAsync(candidate => candidate.Id == birdId);
        var pendingCount = await dbContext.InternalTransferRequests
            .CountAsync(candidate => candidate.BirdId == birdId && candidate.Status == InternalTransferRequestStatus.Pending);
        if (bird.Status == BirdStatus.Transferred)
        {
            Assert.Equal(1, pendingCount);
            Assert.Equal(
                editStatus == HttpStatusCode.OK ? "Nome concorrente" : "Ave race",
                bird.Name);
        }
        else
        {
            Assert.Equal(BirdStatus.Active, bird.Status);
            Assert.Equal(0, pendingCount);
            Assert.Equal("Nome concorrente", bird.Name);
        }
    }

    [Fact]
    public async Task SentAndReceivedListsAndDetailExposeOnlyTransferScopedBirdSummary()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var sourceClient = CreateClient(factory);
        using var destinationClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, sourceClient, "transfer-consult-source@example.com");
        var sourceFarmId = await CreateFarmAsync(sourceClient, "Origem Consulta", "Responsável Origem", "SRC-006");
        await SelectFarmAsync(sourceClient, sourceFarmId);
        await RegisterAndAuthenticateAsync(factory, destinationClient, "transfer-consult-destination@example.com");
        var destinationFarmId = await CreateFarmAsync(destinationClient, "Destino Consulta", "Responsável Destino");
        await SelectFarmAsync(destinationClient, destinationFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(sourceClient, speciesId, "Ave consultável", "223344");

        using var created = await RequestTransferAsync(sourceClient, birdId, destinationFarmId, confirmed: true);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStreamAsync());
        var transferRequestId = createdBody.RootElement.GetProperty("transferRequestId").GetGuid();

        using var sent = await sourceClient.GetAsync("/api/internal-transfers/sent?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        using var sentBody = JsonDocument.Parse(await sent.Content.ReadAsStreamAsync());
        var sentRoot = sentBody.RootElement;
        Assert.Equal("Sent", sentRoot.GetProperty("direction").GetString());
        Assert.Equal(1, sentRoot.GetProperty("totalCount").GetInt32());
        var sentItem = sentRoot.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(transferRequestId, sentItem.GetProperty("transferRequestId").GetGuid());
        Assert.Equal("Ave consultável", sentItem.GetProperty("birdName").GetString());
        Assert.Equal("Pending", sentItem.GetProperty("status").GetString());

        using var received = await destinationClient.GetAsync(
            "/api/internal-transfers/received?status=Pending&page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, received.StatusCode);
        using var receivedBody = JsonDocument.Parse(await received.Content.ReadAsStreamAsync());
        var receivedRoot = receivedBody.RootElement;
        Assert.Equal("Received", receivedRoot.GetProperty("direction").GetString());
        Assert.Equal(1, receivedRoot.GetProperty("totalCount").GetInt32());
        var receivedItem = receivedRoot.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(sourceFarmId, receivedItem.GetProperty("sourceBreedingFarmId").GetGuid());
        Assert.Equal(destinationFarmId, receivedItem.GetProperty("destinationBreedingFarmId").GetGuid());

        using var detail = await destinationClient.GetAsync($"/api/internal-transfers/{transferRequestId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var detailBody = JsonDocument.Parse(await detail.Content.ReadAsStreamAsync());
        var detailRoot = detailBody.RootElement;
        Assert.Equal(transferRequestId, detailRoot.GetProperty("transferRequestId").GetGuid());
        Assert.Equal("Pending", detailRoot.GetProperty("status").GetString());
        var birdSummary = detailRoot.GetProperty("bird");
        Assert.Equal(birdId, birdSummary.GetProperty("birdId").GetGuid());
        Assert.Equal("Ave consultável", birdSummary.GetProperty("name").GetString());
        Assert.Equal("223344", birdSummary.GetProperty("ringNumber").GetString());
        Assert.False(birdSummary.TryGetProperty("notes", out _));
        Assert.False(birdSummary.TryGetProperty("fatherBirdId", out _));

        using var genericBird = await destinationClient.GetAsync($"/api/birds/{birdId}");
        Assert.Equal(HttpStatusCode.NotFound, genericBird.StatusCode);
    }

    [Fact]
    public async Task ThirdTenantCannotInspectTransferAndInvalidListInputsAreRejected()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var sourceClient = CreateClient(factory);
        using var thirdClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, sourceClient, "transfer-third-source@example.com");
        var sourceFarmId = await CreateFarmAsync(sourceClient, "Origem Terceiro", "Responsável Origem", "SRC-007");
        await SelectFarmAsync(sourceClient, sourceFarmId);
        var destinationFarmId = await CreateFarmAsync(sourceClient, "Destino Terceiro", "Responsável Destino");
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(sourceClient, speciesId, "Ave protegida", "334455");
        using var created = await RequestTransferAsync(sourceClient, birdId, destinationFarmId, confirmed: true);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStreamAsync());
        var transferRequestId = createdBody.RootElement.GetProperty("transferRequestId").GetGuid();

        using var unauthenticated = CreateClient(factory);
        using var unauthorized = await unauthenticated.GetAsync("/api/internal-transfers/sent");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        await RegisterAndAuthenticateAsync(factory, thirdClient, "transfer-third-reader@example.com");
        var thirdFarmId = await CreateFarmAsync(thirdClient, "Criatório Terceiro", "Responsável Terceiro");
        await SelectFarmAsync(thirdClient, thirdFarmId);

        using var thirdDetail = await thirdClient.GetAsync($"/api/internal-transfers/{transferRequestId}");
        Assert.Equal(HttpStatusCode.NotFound, thirdDetail.StatusCode);
        using var thirdSent = await thirdClient.GetAsync("/api/internal-transfers/sent");
        Assert.Equal(HttpStatusCode.OK, thirdSent.StatusCode);
        using var thirdSentBody = JsonDocument.Parse(await thirdSent.Content.ReadAsStreamAsync());
        Assert.Equal(0, thirdSentBody.RootElement.GetProperty("totalCount").GetInt32());

        using var invalidStatus = await thirdClient.GetAsync("/api/internal-transfers/received?status=Unknown");
        Assert.Equal(HttpStatusCode.BadRequest, invalidStatus.StatusCode);
        using var invalidPagination = await thirdClient.GetAsync("/api/internal-transfers/received?page=0&pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPagination.StatusCode);
    }

    [Fact]
    public async Task SourceHistoryRemainsAvailableWhenTransferIsAccepted()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var sourceClient = CreateClient(factory);
        using var destinationClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, sourceClient, "transfer-history-source@example.com");
        var sourceFarmId = await CreateFarmAsync(sourceClient, "Origem Histórico", "Responsável Origem", "SRC-008");
        await SelectFarmAsync(sourceClient, sourceFarmId);
        await RegisterAndAuthenticateAsync(factory, destinationClient, "transfer-history-destination@example.com");
        var destinationFarmId = await CreateFarmAsync(destinationClient, "Destino Histórico", "Responsável Destino");
        await SelectFarmAsync(destinationClient, destinationFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(sourceClient, speciesId, "Ave histórica", "445566");
        using var created = await RequestTransferAsync(sourceClient, birdId, destinationFarmId, confirmed: true);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStreamAsync());
        var transferRequestId = createdBody.RootElement.GetProperty("transferRequestId").GetGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE app.internal_transfer_requests SET \"Status\" = {(int)InternalTransferRequestStatus.Accepted}, \"UpdatedAtUtc\" = {DateTimeOffset.UtcNow} WHERE \"Id\" = {transferRequestId}");
        }

        using var sourceHistory = await sourceClient.GetAsync("/api/internal-transfers/sent?status=Accepted");
        Assert.Equal(HttpStatusCode.OK, sourceHistory.StatusCode);
        using var sourceHistoryBody = JsonDocument.Parse(await sourceHistory.Content.ReadAsStreamAsync());
        Assert.Equal(1, sourceHistoryBody.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(
            "Accepted",
            sourceHistoryBody.RootElement.GetProperty("items").EnumerateArray().Single().GetProperty("status").GetString());

        using var sourceDetail = await sourceClient.GetAsync($"/api/internal-transfers/{transferRequestId}");
        Assert.Equal(HttpStatusCode.OK, sourceDetail.StatusCode);
        using var sourceDetailBody = JsonDocument.Parse(await sourceDetail.Content.ReadAsStreamAsync());
        Assert.Equal("Accepted", sourceDetailBody.RootElement.GetProperty("status").GetString());

        using var destinationHistory = await destinationClient.GetAsync("/api/internal-transfers/received?status=Accepted");
        Assert.Equal(HttpStatusCode.OK, destinationHistory.StatusCode);
        using var destinationHistoryBody = JsonDocument.Parse(await destinationHistory.Content.ReadAsStreamAsync());
        Assert.Equal(1, destinationHistoryBody.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task ConsultationRequiresASelectedFarm()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "transfer-no-selection@example.com");

        using var sent = await client.GetAsync("/api/internal-transfers/sent");
        Assert.Equal(HttpStatusCode.Conflict, sent.StatusCode);
        using var received = await client.GetAsync("/api/internal-transfers/received");
        Assert.Equal(HttpStatusCode.Conflict, received.StatusCode);
        using var detail = await client.GetAsync($"/api/internal-transfers/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Conflict, detail.StatusCode);
    }

    private static async Task<HttpResponseMessage> RequestTransferAsync(
        HttpClient client,
        Guid birdId,
        Guid destinationBreedingFarmId,
        bool confirmed) =>
        await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/internal-transfers",
            await GetAntiforgeryTokenAsync(client),
            new { birdId, destinationBreedingFarmId, confirmed }));

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
        string responsibleName,
        string? officialRegistrationNumber = null)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/breeding-farms",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                name,
                responsibleName,
                contactEmail = $"{Guid.NewGuid():N}@example.com",
                officialRegistrationNumber
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

        await AuthenticateExistingUserAsync(client, email);
    }

    private static async Task AuthenticateExistingUserAsync(HttpClient client, string email)
    {
        using var login = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/login",
            await GetAntiforgeryTokenAsync(client),
            new { email, password = "StrongPassword!123" }));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
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
        return response.Headers.GetValues("X-XSRF-TOKEN").Single();
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
        request.Headers.Add("X-XSRF-TOKEN", antiforgeryToken);
        return request;
    }
}
