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
    public async Task DestinationOwnerAcceptsTransferAndMovesBirdAndGenealogyRootAtomically()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var sourceClient = CreateClient(factory);
        using var destinationClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, sourceClient, "transfer-accept-source@example.com");
        var sourceFarmId = await CreateFarmAsync(sourceClient, "Origem Aceite", "Responsável Origem", "SRC-009");
        await SelectFarmAsync(sourceClient, sourceFarmId);
        await RegisterAndAuthenticateAsync(factory, destinationClient, "transfer-accept-destination@example.com");
        var destinationFarmId = await CreateFarmAsync(destinationClient, "Destino Aceite", "Responsável Destino");
        await SelectFarmAsync(destinationClient, destinationFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(sourceClient, speciesId, "Ave aceita", "556677");

        using var created = await RequestTransferAsync(sourceClient, birdId, destinationFarmId, confirmed: true);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStreamAsync());
        var transferRequestId = createdBody.RootElement.GetProperty("transferRequestId").GetGuid();
        Guid originalRootId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            originalRootId = await dbContext.GenealogyNodes
                .Where(node => node.BirdId == birdId && node.IsRoot)
                .Select(node => node.Id)
                .SingleAsync();
        }

        using var accepted = await AcceptTransferAsync(destinationClient, transferRequestId);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var acceptedBody = JsonDocument.Parse(await accepted.Content.ReadAsStreamAsync());
        Assert.Equal(birdId, acceptedBody.RootElement.GetProperty("birdId").GetGuid());
        Assert.Equal("Accepted", acceptedBody.RootElement.GetProperty("status").GetString());

        using var destinationBird = await destinationClient.GetAsync($"/api/birds/{birdId}");
        Assert.Equal(HttpStatusCode.OK, destinationBird.StatusCode);
        using var destinationBirdBody = JsonDocument.Parse(await destinationBird.Content.ReadAsStreamAsync());
        Assert.Equal(birdId, destinationBirdBody.RootElement.GetProperty("birdId").GetGuid());
        Assert.Equal(destinationFarmId, destinationBirdBody.RootElement.GetProperty("breedingFarmId").GetGuid());
        Assert.Equal("556677", destinationBirdBody.RootElement.GetProperty("ringNumber").GetString());
        Assert.Equal("Active", destinationBirdBody.RootElement.GetProperty("status").GetString());

        using var sourceBird = await sourceClient.GetAsync($"/api/birds/{birdId}");
        Assert.Equal(HttpStatusCode.NotFound, sourceBird.StatusCode);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var bird = await verificationDb.Birds.SingleAsync(candidate => candidate.Id == birdId);
        var transfer = await verificationDb.InternalTransferRequests.SingleAsync(candidate => candidate.Id == transferRequestId);
        var root = await verificationDb.GenealogyNodes.SingleAsync(candidate => candidate.BirdId == birdId && candidate.IsRoot);
        Assert.Equal(destinationFarmId, bird.BreedingFarmId);
        Assert.Equal(BirdStatus.Active, bird.Status);
        Assert.Equal("556677", bird.RingNumber);
        Assert.Equal(InternalTransferRequestStatus.Accepted, transfer.Status);
        Assert.Equal(originalRootId, root.Id);
        Assert.Equal(destinationFarmId, root.BreedingFarmId);
    }

    [Fact]
    public async Task AcceptedTransferCarriesGenealogySnapshotsAndBlocksSourcePrivateAccess()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var sourceClient = CreateClient(factory);
        using var destinationClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, sourceClient, "transfer-tree-source@example.com");
        var sourceFarmId = await CreateFarmAsync(sourceClient, "Origem da Árvore", "Responsável Origem", "SRC-012");
        await SelectFarmAsync(sourceClient, sourceFarmId);
        await RegisterAndAuthenticateAsync(factory, destinationClient, "transfer-tree-destination@example.com");
        var destinationFarmId = await CreateFarmAsync(destinationClient, "Destino da Árvore", "Responsável Destino");
        await SelectFarmAsync(destinationClient, destinationFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var fatherId = await CreateBirdAsync(sourceClient, speciesId, "Pai preservado", "889901", "Male");
        var motherId = await CreateBirdAsync(sourceClient, speciesId, "Mãe preservada", "889902");
        var childId = await CreateBirdAsync(sourceClient, speciesId, "Filhote transferido", "889903");

        using var genealogyUpdate = await UpdateGenealogyAsync(sourceClient, childId, fatherId, motherId);
        Assert.Equal(HttpStatusCode.OK, genealogyUpdate.StatusCode);

        Guid rootId;
        Guid fatherSnapshotId;
        Guid motherSnapshotId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            rootId = await dbContext.GenealogyNodes
                .Where(node => node.BirdId == childId && node.IsRoot)
                .Select(node => node.Id)
                .SingleAsync();
            fatherSnapshotId = await dbContext.GenealogyNodes
                .Where(node => node.GenealogyRootId == rootId && node.Position == "father")
                .Select(node => node.Id)
                .SingleAsync();
            motherSnapshotId = await dbContext.GenealogyNodes
                .Where(node => node.GenealogyRootId == rootId && node.Position == "mother")
                .Select(node => node.Id)
                .SingleAsync();
        }

        using var created = await RequestTransferAsync(sourceClient, childId, destinationFarmId, confirmed: true);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStreamAsync());
        var transferRequestId = createdBody.RootElement.GetProperty("transferRequestId").GetGuid();
        using var accepted = await AcceptTransferAsync(destinationClient, transferRequestId);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        using var destinationDetails = await destinationClient.GetAsync($"/api/birds/{childId}");
        Assert.Equal(HttpStatusCode.OK, destinationDetails.StatusCode);
        using var destinationDetailsBody = JsonDocument.Parse(await destinationDetails.Content.ReadAsStreamAsync());
        Assert.Equal(destinationFarmId, destinationDetailsBody.RootElement.GetProperty("breedingFarmId").GetGuid());
        Assert.Equal("Pai preservado", destinationDetailsBody.RootElement.GetProperty("father").GetProperty("name").GetString());
        Assert.Equal("889901", destinationDetailsBody.RootElement.GetProperty("father").GetProperty("ringNumber").GetString());
        Assert.Equal("Mãe preservada", destinationDetailsBody.RootElement.GetProperty("mother").GetProperty("name").GetString());
        Assert.Equal("889902", destinationDetailsBody.RootElement.GetProperty("mother").GetProperty("ringNumber").GetString());

        using var destinationGenealogy = await destinationClient.GetAsync(
            $"/api/birds/{childId}/genealogy?maxGenerations=1");
        Assert.Equal(HttpStatusCode.OK, destinationGenealogy.StatusCode);
        using var destinationGenealogyBody = JsonDocument.Parse(await destinationGenealogy.Content.ReadAsStreamAsync());
        var snapshotNodes = destinationGenealogyBody.RootElement.GetProperty("nodes")
            .EnumerateArray()
            .Where(node => node.GetProperty("generation").GetInt32() == 1)
            .ToArray();
        Assert.Equal(2, snapshotNodes.Length);
        Assert.All(snapshotNodes, node =>
        {
            Assert.Equal(JsonValueKind.Null, node.GetProperty("birdId").ValueKind);
            Assert.Equal("Snapshot", node.GetProperty("source").GetString());
            Assert.True(node.GetProperty("isSnapshot").GetBoolean());
            Assert.False(node.GetProperty("isAccessible").GetBoolean());
            Assert.False(node.GetProperty("canNavigate").GetBoolean());
        });
        Assert.Contains(snapshotNodes, node => node.GetProperty("name").GetString() == "Pai preservado");
        Assert.Contains(snapshotNodes, node => node.GetProperty("name").GetString() == "Mãe preservada");

        using var sourceDetails = await sourceClient.GetAsync($"/api/birds/{childId}");
        Assert.Equal(HttpStatusCode.NotFound, sourceDetails.StatusCode);
        using var sourceGenealogy = await sourceClient.GetAsync($"/api/birds/{childId}/genealogy");
        Assert.Equal(HttpStatusCode.NotFound, sourceGenealogy.StatusCode);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var bird = await verificationDb.Birds.SingleAsync(candidate => candidate.Id == childId);
        var root = await verificationDb.GenealogyNodes.SingleAsync(candidate => candidate.Id == rootId);
        var snapshots = await verificationDb.GenealogyNodes
            .Where(candidate => candidate.GenealogyRootId == rootId && !candidate.IsRoot)
            .ToArrayAsync();
        Assert.Equal(destinationFarmId, bird.BreedingFarmId);
        Assert.Null(bird.FatherBirdId);
        Assert.Null(bird.MotherBirdId);
        Assert.Equal(destinationFarmId, root.BreedingFarmId);
        Assert.Equal(2, snapshots.Length);
        Assert.Contains(snapshots, node =>
            node.Id == fatherSnapshotId &&
            node.BreedingFarmId == sourceFarmId &&
            node.LinkedBirdId == fatherId &&
            node.SnapshotName == "Pai preservado" &&
            node.SnapshotRingNumber == "889901");
        Assert.Contains(snapshots, node =>
            node.Id == motherSnapshotId &&
            node.BreedingFarmId == sourceFarmId &&
            node.LinkedBirdId == motherId &&
            node.SnapshotName == "Mãe preservada" &&
            node.SnapshotRingNumber == "889902");
    }

    [Fact]
    public async Task AcceptedTransferKeepsReproductionHistoryAtOriginAndMakesCurrentDependentsAvailableAtDestination()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var sourceClient = CreateClient(factory);
        using var destinationClient = CreateClient(factory);
        using var thirdClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, sourceClient, "transfer-history-access-source@example.com");
        var sourceFarmId = await CreateFarmAsync(sourceClient, "Origem Histórico Acesso", "Responsável Origem", "SRC-045");
        await SelectFarmAsync(sourceClient, sourceFarmId);
        await RegisterAndAuthenticateAsync(factory, destinationClient, "transfer-history-access-destination@example.com");
        var destinationFarmId = await CreateFarmAsync(destinationClient, "Destino Histórico Acesso", "Responsável Destino");
        await SelectFarmAsync(destinationClient, destinationFarmId);
        await RegisterAndAuthenticateAsync(factory, thirdClient, "transfer-history-access-third@example.com");
        var thirdFarmId = await CreateFarmAsync(thirdClient, "Terceiro Histórico Acesso", "Responsável Terceiro");
        await SelectFarmAsync(thirdClient, thirdFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var maleId = await CreateBirdAsync(sourceClient, speciesId, "Macho histórico", "450001", "Male");
        var femaleId = await CreateBirdAsync(sourceClient, speciesId, "Fêmea histórica", "450002");
        var reproductionId = await CreateReproductionAsync(sourceClient, maleId, femaleId);

        using var requested = await RequestTransferAsync(sourceClient, maleId, destinationFarmId, confirmed: true);
        Assert.Equal(HttpStatusCode.Created, requested.StatusCode);
        using var requestedBody = JsonDocument.Parse(await requested.Content.ReadAsStreamAsync());
        var transferRequestId = requestedBody.RootElement.GetProperty("transferRequestId").GetGuid();
        using var accepted = await AcceptTransferAsync(destinationClient, transferRequestId);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        using var sourceBird = await sourceClient.GetAsync($"/api/birds/{maleId}");
        Assert.Equal(HttpStatusCode.NotFound, sourceBird.StatusCode);
        using var destinationBird = await destinationClient.GetAsync($"/api/birds/{maleId}");
        Assert.Equal(HttpStatusCode.OK, destinationBird.StatusCode);
        using var destinationBirdBody = JsonDocument.Parse(await destinationBird.Content.ReadAsStreamAsync());
        Assert.Equal(destinationFarmId, destinationBirdBody.RootElement.GetProperty("breedingFarmId").GetGuid());
        Assert.Equal("Active", destinationBirdBody.RootElement.GetProperty("status").GetString());

        using var sourceHistory = await sourceClient.GetAsync($"/api/reproductions?birdId={maleId}");
        Assert.Equal(HttpStatusCode.OK, sourceHistory.StatusCode);
        using var sourceHistoryBody = JsonDocument.Parse(await sourceHistory.Content.ReadAsStreamAsync());
        Assert.Equal(1, sourceHistoryBody.RootElement.GetProperty("totalCount").GetInt32());
        var sourceHistoryItem = sourceHistoryBody.RootElement.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(reproductionId, sourceHistoryItem.GetProperty("reproductionId").GetGuid());
        Assert.Equal("Macho histórico", sourceHistoryItem.GetProperty("maleBird").GetProperty("name").GetString());

        using var sourceDetail = await sourceClient.GetAsync($"/api/reproductions/{reproductionId}");
        Assert.Equal(HttpStatusCode.OK, sourceDetail.StatusCode);
        using var destinationHistory = await destinationClient.GetAsync($"/api/reproductions/{reproductionId}");
        Assert.Equal(HttpStatusCode.NotFound, destinationHistory.StatusCode);
        using var thirdDetail = await thirdClient.GetAsync($"/api/reproductions/{reproductionId}");
        Assert.Equal(HttpStatusCode.NotFound, thirdDetail.StatusCode);

        var destinationFemaleId = await CreateBirdAsync(
            destinationClient,
            speciesId,
            "Fêmea do destino",
            "450003");
        using var destinationReproduction = await CreateReproductionRequestAsync(
            destinationClient,
            maleId,
            destinationFemaleId);
        Assert.Equal(HttpStatusCode.Created, destinationReproduction.StatusCode);
    }

    [Fact]
    public async Task ExternalTransferDoesNotGrantThirdTenantAccessToBirdOrOriginHistory()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var sourceClient = CreateClient(factory);
        using var thirdClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, sourceClient, "external-history-source@example.com");
        var sourceFarmId = await CreateFarmAsync(sourceClient, "Origem Externa Histórico", "Responsável Origem", "EXT-045");
        await SelectFarmAsync(sourceClient, sourceFarmId);
        await RegisterAndAuthenticateAsync(factory, thirdClient, "external-history-third@example.com");
        var thirdFarmId = await CreateFarmAsync(thirdClient, "Terceiro Externo Histórico", "Responsável Terceiro");
        await SelectFarmAsync(thirdClient, thirdFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var maleId = await CreateBirdAsync(sourceClient, speciesId, "Macho externo histórico", "450004", "Male");
        var femaleId = await CreateBirdAsync(sourceClient, speciesId, "Fêmea externa histórica", "450005");
        var reproductionId = await CreateReproductionAsync(sourceClient, maleId, femaleId);

        using var completed = await CompleteExternalTransferAsync(
            sourceClient,
            maleId,
            "Recebedor externo V0-045",
            null,
            confirmed: true);
        Assert.Equal(HttpStatusCode.Created, completed.StatusCode);

        using var sourceBird = await sourceClient.GetAsync($"/api/birds/{maleId}");
        Assert.Equal(HttpStatusCode.OK, sourceBird.StatusCode);
        using var thirdBird = await thirdClient.GetAsync($"/api/birds/{maleId}");
        Assert.Equal(HttpStatusCode.NotFound, thirdBird.StatusCode);
        using var sourceHistory = await sourceClient.GetAsync($"/api/reproductions/{reproductionId}");
        Assert.Equal(HttpStatusCode.OK, sourceHistory.StatusCode);
        using var thirdHistory = await thirdClient.GetAsync($"/api/reproductions/{reproductionId}");
        Assert.Equal(HttpStatusCode.NotFound, thirdHistory.StatusCode);
    }

    [Fact]
    public async Task OnlyDestinationOwnerCanAcceptAndAcceptedTransferCannotBeReplayed()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var sourceClient = CreateClient(factory);
        using var destinationClient = CreateClient(factory);
        using var thirdClient = CreateClient(factory);
        using var unauthenticatedClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, sourceClient, "transfer-authorization-source@example.com");
        var sourceFarmId = await CreateFarmAsync(sourceClient, "Origem Autorização", "Responsável Origem", "SRC-010");
        await SelectFarmAsync(sourceClient, sourceFarmId);
        await RegisterAndAuthenticateAsync(factory, destinationClient, "transfer-authorization-destination@example.com");
        var destinationFarmId = await CreateFarmAsync(destinationClient, "Destino Autorização", "Responsável Destino");
        await SelectFarmAsync(destinationClient, destinationFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(sourceClient, speciesId, "Ave autorizada", "667788");
        using var created = await RequestTransferAsync(sourceClient, birdId, destinationFarmId, confirmed: true);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStreamAsync());
        var transferRequestId = createdBody.RootElement.GetProperty("transferRequestId").GetGuid();

        using var unauthenticated = await unauthenticatedClient.PostAsync(
            $"/api/internal-transfers/{transferRequestId}/accept",
            content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        using var sourceAttempt = await AcceptTransferAsync(sourceClient, transferRequestId);
        Assert.Equal(HttpStatusCode.NotFound, sourceAttempt.StatusCode);

        await RegisterAndAuthenticateAsync(factory, thirdClient, "transfer-authorization-third@example.com");
        var thirdFarmId = await CreateFarmAsync(thirdClient, "Criatório Terceiro", "Responsável Terceiro");
        await SelectFarmAsync(thirdClient, thirdFarmId);
        using var thirdAttempt = await AcceptTransferAsync(thirdClient, transferRequestId);
        Assert.Equal(HttpStatusCode.NotFound, thirdAttempt.StatusCode);

        using var accepted = await AcceptTransferAsync(destinationClient, transferRequestId);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var replay = await AcceptTransferAsync(destinationClient, transferRequestId);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Equal(BirdStatus.Active, (await dbContext.Birds.SingleAsync(candidate => candidate.Id == birdId)).Status);
        Assert.Equal(
            InternalTransferRequestStatus.Accepted,
            (await dbContext.InternalTransferRequests.SingleAsync(candidate => candidate.Id == transferRequestId)).Status);
    }

    [Fact]
    public async Task DestinationOwnerCanRejectTransferAndSourceBirdRemainsActiveAndReusable()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var sourceClient = CreateClient(factory);
        using var destinationClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, sourceClient, "transfer-reject-source@example.com");
        var sourceFarmId = await CreateFarmAsync(sourceClient, "Origem Rejeição", "Responsável Origem", "SRC-012");
        await SelectFarmAsync(sourceClient, sourceFarmId);
        await RegisterAndAuthenticateAsync(factory, destinationClient, "transfer-reject-destination@example.com");
        var destinationFarmId = await CreateFarmAsync(destinationClient, "Destino Rejeição", "Responsável Destino");
        await SelectFarmAsync(destinationClient, destinationFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(sourceClient, speciesId, "Ave rejeitada", "889900");

        using var created = await RequestTransferAsync(sourceClient, birdId, destinationFarmId, confirmed: true);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStreamAsync());
        var transferRequestId = createdBody.RootElement.GetProperty("transferRequestId").GetGuid();

        using var rejected = await RejectTransferAsync(destinationClient, transferRequestId);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        using var rejectedBody = JsonDocument.Parse(await rejected.Content.ReadAsStreamAsync());
        Assert.Equal("Rejected", rejectedBody.RootElement.GetProperty("status").GetString());

        using var sourceBird = await sourceClient.GetAsync($"/api/birds/{birdId}");
        Assert.Equal(HttpStatusCode.OK, sourceBird.StatusCode);
        using var sourceBirdBody = JsonDocument.Parse(await sourceBird.Content.ReadAsStreamAsync());
        Assert.Equal(sourceFarmId, sourceBirdBody.RootElement.GetProperty("breedingFarmId").GetGuid());
        Assert.Equal("Active", sourceBirdBody.RootElement.GetProperty("status").GetString());

        using var destinationBird = await destinationClient.GetAsync($"/api/birds/{birdId}");
        Assert.Equal(HttpStatusCode.NotFound, destinationBird.StatusCode);

        using var history = await sourceClient.GetAsync("/api/internal-transfers/sent?status=Rejected");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        using var historyBody = JsonDocument.Parse(await history.Content.ReadAsStreamAsync());
        Assert.Equal(1, historyBody.RootElement.GetProperty("totalCount").GetInt32());

        using var retried = await RequestTransferAsync(sourceClient, birdId, destinationFarmId, confirmed: true);
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
    }

    [Fact]
    public async Task OnlySourceRequesterCanCancelTransferAndCancellationIsTerminal()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var sourceClient = CreateClient(factory);
        using var destinationClient = CreateClient(factory);
        using var unauthenticatedClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, sourceClient, "transfer-cancel-source@example.com");
        var sourceFarmId = await CreateFarmAsync(sourceClient, "Origem Cancelamento", "Responsável Origem", "SRC-013");
        await SelectFarmAsync(sourceClient, sourceFarmId);
        await RegisterAndAuthenticateAsync(factory, destinationClient, "transfer-cancel-destination@example.com");
        var destinationFarmId = await CreateFarmAsync(destinationClient, "Destino Cancelamento", "Responsável Destino");
        await SelectFarmAsync(destinationClient, destinationFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(sourceClient, speciesId, "Ave cancelada", "990011");

        using var created = await RequestTransferAsync(sourceClient, birdId, destinationFarmId, confirmed: true);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStreamAsync());
        var transferRequestId = createdBody.RootElement.GetProperty("transferRequestId").GetGuid();

        using var unauthenticated = await unauthenticatedClient.PostAsync(
            $"/api/internal-transfers/{transferRequestId}/cancel",
            content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        using var destinationAttempt = await CancelTransferAsync(destinationClient, transferRequestId);
        Assert.Equal(HttpStatusCode.NotFound, destinationAttempt.StatusCode);

        using var cancelled = await CancelTransferAsync(sourceClient, transferRequestId);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        using var cancelledBody = JsonDocument.Parse(await cancelled.Content.ReadAsStreamAsync());
        Assert.Equal("Cancelled", cancelledBody.RootElement.GetProperty("status").GetString());

        using var replay = await CancelTransferAsync(sourceClient, transferRequestId);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);

        using var sourceBird = await sourceClient.GetAsync($"/api/birds/{birdId}");
        Assert.Equal(HttpStatusCode.OK, sourceBird.StatusCode);
        using var sourceBirdBody = JsonDocument.Parse(await sourceBird.Content.ReadAsStreamAsync());
        Assert.Equal(sourceFarmId, sourceBirdBody.RootElement.GetProperty("breedingFarmId").GetGuid());
        Assert.Equal("Active", sourceBirdBody.RootElement.GetProperty("status").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var transfer = await dbContext.InternalTransferRequests.SingleAsync(candidate => candidate.Id == transferRequestId);
        Assert.Equal(InternalTransferRequestStatus.Cancelled, transfer.Status);
    }

    [Fact]
    public async Task AcceptAndCancelRaceHasOneWinnerAndLeavesCompatibleBirdAndTransferState()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var sourceClient = CreateClient(factory);
        using var destinationClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, sourceClient, "transfer-resolve-race-source@example.com");
        var sourceFarmId = await CreateFarmAsync(sourceClient, "Origem Resolve Race", "Responsável Origem", "SRC-014");
        await SelectFarmAsync(sourceClient, sourceFarmId);
        await RegisterAndAuthenticateAsync(factory, destinationClient, "transfer-resolve-race-destination@example.com");
        var destinationFarmId = await CreateFarmAsync(destinationClient, "Destino Resolve Race", "Responsável Destino");
        await SelectFarmAsync(destinationClient, destinationFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(sourceClient, speciesId, "Ave resolve race", "101112");

        using var created = await RequestTransferAsync(sourceClient, birdId, destinationFarmId, confirmed: true);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStreamAsync());
        var transferRequestId = createdBody.RootElement.GetProperty("transferRequestId").GetGuid();

        var cancelRequest = CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/internal-transfers/{transferRequestId}/cancel",
            await GetAntiforgeryTokenAsync(sourceClient));
        var acceptRequest = CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/internal-transfers/{transferRequestId}/accept",
            await GetAntiforgeryTokenAsync(destinationClient));
        var responses = await Task.WhenAll(
            sourceClient.SendAsync(cancelRequest),
            destinationClient.SendAsync(acceptRequest));
        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }

            cancelRequest.Dispose();
            acceptRequest.Dispose();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var bird = await dbContext.Birds.SingleAsync(candidate => candidate.Id == birdId);
        var transfer = await dbContext.InternalTransferRequests.SingleAsync(candidate => candidate.Id == transferRequestId);
        Assert.NotEqual(InternalTransferRequestStatus.Pending, transfer.Status);
        if (transfer.Status == InternalTransferRequestStatus.Accepted)
        {
            Assert.Equal(destinationFarmId, bird.BreedingFarmId);
            Assert.Equal(BirdStatus.Active, bird.Status);
        }
        else
        {
            Assert.Equal(InternalTransferRequestStatus.Cancelled, transfer.Status);
            Assert.Equal(sourceFarmId, bird.BreedingFarmId);
            Assert.Equal(BirdStatus.Active, bird.Status);
        }
    }

    [Fact]
    public async Task ConcurrentDestinationAcceptsHaveOneWinnerAndLeaveNoPartialState()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var sourceClient = CreateClient(factory);
        using var destinationClient = CreateClient(factory);
        using var racingClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, sourceClient, "transfer-accept-race-source@example.com");
        var sourceFarmId = await CreateFarmAsync(sourceClient, "Origem Aceite Race", "Responsável Origem", "SRC-011");
        await SelectFarmAsync(sourceClient, sourceFarmId);
        await RegisterAndAuthenticateAsync(factory, destinationClient, "transfer-accept-race-destination@example.com");
        var destinationFarmId = await CreateFarmAsync(destinationClient, "Destino Aceite Race", "Responsável Destino");
        await SelectFarmAsync(destinationClient, destinationFarmId);
        await AuthenticateExistingUserAsync(racingClient, "transfer-accept-race-destination@example.com");
        await SelectFarmAsync(racingClient, destinationFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(sourceClient, speciesId, "Ave aceite race", "778899");
        using var created = await RequestTransferAsync(sourceClient, birdId, destinationFarmId, confirmed: true);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStreamAsync());
        var transferRequestId = createdBody.RootElement.GetProperty("transferRequestId").GetGuid();

        var firstRequest = CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/internal-transfers/{transferRequestId}/accept",
            await GetAntiforgeryTokenAsync(destinationClient));
        var secondRequest = CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/internal-transfers/{transferRequestId}/accept",
            await GetAntiforgeryTokenAsync(racingClient));
        var responses = await Task.WhenAll(
            destinationClient.SendAsync(firstRequest),
            racingClient.SendAsync(secondRequest));
        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
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
        var bird = await dbContext.Birds.SingleAsync(candidate => candidate.Id == birdId);
        var transfer = await dbContext.InternalTransferRequests.SingleAsync(candidate => candidate.Id == transferRequestId);
        Assert.Equal(destinationFarmId, bird.BreedingFarmId);
        Assert.Equal(BirdStatus.Active, bird.Status);
        Assert.Equal(InternalTransferRequestStatus.Accepted, transfer.Status);
        Assert.Equal("778899", bird.RingNumber);
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

    [Fact]
    public async Task OwnerCanCompleteExternalTransferAndBirdRemainsInSourceFarm()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "external-transfer-owner@example.com");
        var sourceFarmId = await CreateFarmAsync(client, "Origem Externa", "Responsável Origem", "EXT-001");
        await SelectFarmAsync(client, sourceFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(client, speciesId, "Ave externa", "123458");
        var missingRingBirdId = await CreateBirdAsync(client, speciesId, "Ave sem anilha externa", null);

        using var unconfirmed = await CompleteExternalTransferAsync(
            client,
            birdId,
            "Recebedor externo",
            "Entrega agendada",
            confirmed: false);
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        Assert.Contains("confirmation", await unconfirmed.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using var missingRing = await CompleteExternalTransferAsync(
            client,
            missingRingBirdId,
            "Recebedor externo",
            null,
            confirmed: true);
        Assert.Equal(HttpStatusCode.BadRequest, missingRing.StatusCode);
        Assert.Contains("ring", await missingRing.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using var completed = await CompleteExternalTransferAsync(
            client,
            birdId,
            "  Recebedor externo  ",
            "  Entrega agendada  ",
            confirmed: true);
        Assert.Equal(HttpStatusCode.Created, completed.StatusCode);
        using var completedBody = JsonDocument.Parse(await completed.Content.ReadAsStreamAsync());
        var completedRoot = completedBody.RootElement;
        Assert.Equal(birdId, completedRoot.GetProperty("birdId").GetGuid());
        Assert.Equal(sourceFarmId, completedRoot.GetProperty("breedingFarmId").GetGuid());
        Assert.Equal("Recebedor externo", completedRoot.GetProperty("recipientName").GetString());
        Assert.Equal("Entrega agendada", completedRoot.GetProperty("notes").GetString());
        Assert.Equal("Transferred", completedRoot.GetProperty("status").GetString());

        using var details = await client.GetAsync($"/api/birds/{birdId}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        using var detailsBody = JsonDocument.Parse(await details.Content.ReadAsStreamAsync());
        Assert.Equal(sourceFarmId, detailsBody.RootElement.GetProperty("breedingFarmId").GetGuid());
        Assert.Equal("Transferred", detailsBody.RootElement.GetProperty("status").GetString());

        using var replay = await CompleteExternalTransferAsync(
            client,
            birdId,
            "Outro recebedor",
            null,
            confirmed: true);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var transfer = await dbContext.ExternalTransfers.SingleAsync(candidate => candidate.BirdId == birdId);
        Assert.Equal("Recebedor externo", transfer.RecipientName);
        Assert.Equal("Entrega agendada", transfer.Notes);
        Assert.Equal(BirdStatus.Transferred, (await dbContext.Birds.SingleAsync(candidate => candidate.Id == birdId)).Status);
    }

    [Fact]
    public async Task ExternalTransferRequiresSourceOwnerAndDoesNotCrossTenants()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        using var unauthenticatedClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "external-transfer-source@example.com");
        var sourceFarmId = await CreateFarmAsync(ownerClient, "Origem Protegida", "Responsável Origem", "EXT-002");
        await SelectFarmAsync(ownerClient, sourceFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(ownerClient, speciesId, "Ave protegida", "123459");

        await RegisterAndAuthenticateAsync(factory, otherClient, "external-transfer-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient, "Outro Criatório", "Outro Responsável", "EXT-003");
        await SelectFarmAsync(otherClient, otherFarmId);

        using var unauthenticated = await CompleteExternalTransferAsync(
            unauthenticatedClient,
            birdId,
            "Recebedor externo",
            null,
            confirmed: true);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        using var crossTenant = await CompleteExternalTransferAsync(
            otherClient,
            birdId,
            "Recebedor externo",
            null,
            confirmed: true);
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);

        using var sourceCompletion = await CompleteExternalTransferAsync(
            ownerClient,
            birdId,
            "Recebedor externo",
            null,
            confirmed: true);
        Assert.Equal(HttpStatusCode.Created, sourceCompletion.StatusCode);
    }

    [Fact]
    public async Task PendingInternalTransferBlocksExternalCompletion()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var sourceClient = CreateClient(factory);
        using var destinationClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, sourceClient, "external-transfer-pending-source@example.com");
        var sourceFarmId = await CreateFarmAsync(sourceClient, "Origem Pendente", "Responsável Origem", "EXT-004");
        await SelectFarmAsync(sourceClient, sourceFarmId);
        await RegisterAndAuthenticateAsync(factory, destinationClient, "external-transfer-pending-destination@example.com");
        var destinationFarmId = await CreateFarmAsync(destinationClient, "Destino Pendente", "Responsável Destino", "EXT-005");
        await SelectFarmAsync(destinationClient, destinationFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(sourceClient, speciesId, "Ave pendente", "123460");

        using var request = await RequestTransferAsync(sourceClient, birdId, destinationFarmId, confirmed: true);
        Assert.Equal(HttpStatusCode.Created, request.StatusCode);

        using var external = await CompleteExternalTransferAsync(
            sourceClient,
            birdId,
            "Recebedor externo",
            null,
            confirmed: true);
        Assert.Equal(HttpStatusCode.Conflict, external.StatusCode);
        Assert.Contains("pending internal transfer", await external.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Empty(await dbContext.ExternalTransfers.Where(candidate => candidate.BirdId == birdId).ToArrayAsync());
        Assert.Equal(BirdStatus.Transferred, (await dbContext.Birds.SingleAsync(candidate => candidate.Id == birdId)).Status);
    }

    [Fact]
    public async Task ConcurrentExternalCompletionsHaveOneWinner()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var firstClient = CreateClient(factory);
        using var secondClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, firstClient, "external-transfer-race@example.com");
        var sourceFarmId = await CreateFarmAsync(firstClient, "Origem Race Externa", "Responsável Race", "EXT-006");
        await SelectFarmAsync(firstClient, sourceFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(firstClient, speciesId, "Ave race externa", "123461");
        await AuthenticateExistingUserAsync(secondClient, "external-transfer-race@example.com");

        var firstRequest = CreateBrowserRequest(
            HttpMethod.Post,
            "/api/external-transfers",
            await GetAntiforgeryTokenAsync(firstClient),
            new
            {
                birdId,
                recipientName = "Recebedor A",
                notes = "Primeira confirmação",
                confirmed = true
            });
        var secondRequest = CreateBrowserRequest(
            HttpMethod.Post,
            "/api/external-transfers",
            await GetAntiforgeryTokenAsync(secondClient),
            new
            {
                birdId,
                recipientName = "Recebedor B",
                notes = "Segunda confirmação",
                confirmed = true
            });

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
        Assert.Single(await dbContext.ExternalTransfers.Where(candidate => candidate.BirdId == birdId).ToArrayAsync());
        Assert.Equal(BirdStatus.Transferred, (await dbContext.Birds.SingleAsync(candidate => candidate.Id == birdId)).Status);
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

    private static async Task<HttpResponseMessage> CompleteExternalTransferAsync(
        HttpClient client,
        Guid birdId,
        string recipientName,
        string? notes,
        bool confirmed) =>
        await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/external-transfers",
            await GetAntiforgeryTokenAsync(client),
            new { birdId, recipientName, notes, confirmed }));

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
                startDate = "2026-09-01",
                notes = "Histórico da reprodução"
            }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("reproductionId").GetGuid();
    }

    private static async Task<HttpResponseMessage> CreateReproductionRequestAsync(
        HttpClient client,
        Guid maleBirdId,
        Guid femaleBirdId)
    {
        return await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/reproductions",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                maleBirdId,
                femaleBirdId,
                startDate = "2026-09-01",
                notes = "Histórico da reprodução"
            }));
    }

    private static async Task<HttpResponseMessage> UpdateGenealogyAsync(
        HttpClient client,
        Guid birdId,
        Guid? fatherBirdId,
        Guid? motherBirdId) =>
        await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            $"/api/birds/{birdId}/genealogy",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                fatherBirdId,
                externalFatherName = (string?)null,
                externalFatherSex = (string?)null,
                motherBirdId,
                externalMotherName = (string?)null,
                externalMotherSex = (string?)null
            }));

    private static async Task<HttpResponseMessage> AcceptTransferAsync(
        HttpClient client,
        Guid transferRequestId) =>
        await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/internal-transfers/{transferRequestId}/accept",
            await GetAntiforgeryTokenAsync(client)));

    private static async Task<HttpResponseMessage> RejectTransferAsync(
        HttpClient client,
        Guid transferRequestId) =>
        await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/internal-transfers/{transferRequestId}/reject",
            await GetAntiforgeryTokenAsync(client)));

    private static async Task<HttpResponseMessage> CancelTransferAsync(
        HttpClient client,
        Guid transferRequestId) =>
        await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/internal-transfers/{transferRequestId}/cancel",
            await GetAntiforgeryTokenAsync(client)));

    private static async Task<Guid> CreateBirdAsync(
        HttpClient client,
        Guid speciesId,
        string name,
        string? ringNumber,
        string sex = "Female")
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/birds",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                name,
                sex,
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
