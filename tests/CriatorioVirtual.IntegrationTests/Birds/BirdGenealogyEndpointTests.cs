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

public sealed class BirdGenealogyEndpointTests
{
    [Fact]
    public async Task GenealogyReturnsBoundedSnapshotGraphAndMarksTruncatedBranch()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "genealogy-tree-owner@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);

        var fatherFatherId = await CreateBirdAsync(client, new
        {
            name = "Avô Azul",
            sex = "Male",
            speciesId,
            birthDate = "2015-01-01",
            ringNumber = "936001"
        });
        var fatherMotherId = await CreateBirdAsync(client, new
        {
            name = "Avó Azul",
            sex = "Female",
            speciesId,
            birthDate = "2015-02-01",
            ringNumber = "936002"
        });
        var fatherId = await CreateBirdAsync(client, new
        {
            name = "Pai Azul",
            sex = "Male",
            speciesId,
            birthDate = "2018-06-01",
            ringNumber = "936003",
            fatherBirdId = fatherFatherId,
            motherBirdId = fatherMotherId
        });
        var motherFatherId = await CreateBirdAsync(client, new
        {
            name = "Avô Rubi",
            sex = "Male",
            speciesId,
            birthDate = "2015-03-01",
            ringNumber = "936004"
        });
        var motherMotherId = await CreateBirdAsync(client, new
        {
            name = "Avó Rubi",
            sex = "Female",
            speciesId,
            birthDate = "2015-04-01",
            ringNumber = "936005"
        });
        var motherId = await CreateBirdAsync(client, new
        {
            name = "Mãe Rubi",
            sex = "Female",
            speciesId,
            birthDate = "2019-07-01",
            ringNumber = "936006",
            fatherBirdId = motherFatherId,
            motherBirdId = motherMotherId
        });
        var childId = await CreateBirdAsync(client, new
        {
            name = "Filhote Aurora",
            sex = "Female",
            speciesId,
            birthDate = "2020-09-07",
            ringNumber = "936007",
            fatherBirdId = fatherId,
            motherBirdId = motherId
        });

        using var boundedResponse = await client.GetAsync($"/api/birds/{childId}/genealogy?maxGenerations=1");

        Assert.Equal(HttpStatusCode.OK, boundedResponse.StatusCode);
        using var boundedDocument = JsonDocument.Parse(await boundedResponse.Content.ReadAsStreamAsync());
        var bounded = boundedDocument.RootElement;
        Assert.Equal(farmId, bounded.GetProperty("breedingFarmId").GetGuid());
        Assert.Equal(childId, bounded.GetProperty("rootBirdId").GetGuid());
        Assert.Equal(1, bounded.GetProperty("maxGenerations").GetInt32());
        Assert.True(bounded.GetProperty("isTruncated").GetBoolean());
        Assert.Equal(3, bounded.GetProperty("nodes").GetArrayLength());
        Assert.Equal(2, bounded.GetProperty("edges").GetArrayLength());
        Assert.Equal(
            2,
            bounded.GetProperty("nodes")
                .EnumerateArray()
                .Count(node => node.GetProperty("generation").GetInt32() == 1));

        using var completeResponse = await client.GetAsync($"/api/birds/{childId}/genealogy?maxGenerations=2");

        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        using var completeDocument = JsonDocument.Parse(await completeResponse.Content.ReadAsStreamAsync());
        var complete = completeDocument.RootElement;
        Assert.False(complete.GetProperty("isTruncated").GetBoolean());
        Assert.Equal(7, complete.GetProperty("nodes").GetArrayLength());
        Assert.Equal(6, complete.GetProperty("edges").GetArrayLength());
        Assert.Equal(
            4,
            complete.GetProperty("nodes")
                .EnumerateArray()
                .Count(node => node.GetProperty("generation").GetInt32() == 2));
        Assert.All(
            complete.GetProperty("nodes").EnumerateArray(),
            node =>
            {
                Assert.True(node.GetProperty("isAccessible").GetBoolean());
                Assert.True(node.GetProperty("canNavigate").GetBoolean());
                Assert.NotEqual(JsonValueKind.Null, node.GetProperty("birdId").ValueKind);
            });
    }

    [Fact]
    public async Task GenealogyPreservesExternalAncestorWithoutPrivateNavigation()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "genealogy-tree-external@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var childId = await CreateBirdAsync(client, new
        {
            name = "Filhote externo",
            sex = "Unknown",
            speciesId,
            birthDate = "2020-09-07",
            ringNumber = "937001",
            externalFatherName = "Pai sem cadastro",
            externalFatherSex = "Male",
            externalMotherName = "Mãe sem cadastro",
            externalMotherSex = "Female"
        });

        using var response = await client.GetAsync($"/api/birds/{childId}/genealogy?maxGenerations=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        Assert.Equal(farmId, root.GetProperty("breedingFarmId").GetGuid());
        Assert.False(root.GetProperty("isTruncated").GetBoolean());
        Assert.Equal(3, root.GetProperty("nodes").GetArrayLength());
        Assert.All(
            root.GetProperty("nodes").EnumerateArray().Where(node => node.GetProperty("generation").GetInt32() == 1),
            node =>
            {
                Assert.Equal(JsonValueKind.Null, node.GetProperty("birdId").ValueKind);
                Assert.Equal("External", node.GetProperty("source").GetString());
                Assert.True(node.GetProperty("isSnapshot").GetBoolean());
                Assert.False(node.GetProperty("isAccessible").GetBoolean());
                Assert.False(node.GetProperty("canNavigate").GetBoolean());
            });
        Assert.Contains(
            root.GetProperty("nodes").EnumerateArray(),
            node => node.GetProperty("name").GetString() == "Pai sem cadastro");
        Assert.Contains(
            root.GetProperty("nodes").EnumerateArray(),
            node => node.GetProperty("name").GetString() == "Mãe sem cadastro");
    }

    [Fact]
    public async Task GenealogyRequiresAuthenticationSelectionAndTenantAccess()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var unauthenticatedClient = CreateClient(factory);
        var missingBirdId = Guid.NewGuid();

        using var unauthenticated = await unauthenticatedClient.GetAsync($"/api/birds/{missingBirdId}/genealogy");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "genealogy-tree-tenant-owner@example.com");
        using var withoutFarm = await ownerClient.GetAsync($"/api/birds/{missingBirdId}/genealogy");
        Assert.Equal(HttpStatusCode.Conflict, withoutFarm.StatusCode);

        var ownerFarmId = await CreateFarmAsync(ownerClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);
        await RegisterAndAuthenticateAsync(factory, otherClient, "genealogy-tree-tenant-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient);
        await SelectFarmAsync(otherClient, otherFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var ownerBirdId = await CreateBirdAsync(ownerClient, new
        {
            name = "Ave privada",
            sex = "Unknown",
            speciesId,
            birthDate = "2020-09-07",
            ringNumber = "937002"
        });

        using var foreignBird = await otherClient.GetAsync($"/api/birds/{ownerBirdId}/genealogy");
        Assert.Equal(HttpStatusCode.NotFound, foreignBird.StatusCode);

        using var invalidDepth = await ownerClient.GetAsync($"/api/birds/{ownerBirdId}/genealogy?maxGenerations=7");
        Assert.Equal(HttpStatusCode.BadRequest, invalidDepth.StatusCode);
    }

    [Fact]
    public async Task LinkCopiesParentSnapshotSearchesByNameOrRingAndIsIdempotent()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "genealogy-snapshot@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);

        var fatherId = await CreateBirdAsync(client, new
        {
            name = "Pai Azul",
            sex = "Male",
            speciesId,
            birthDate = "2018-06-01",
            ringNumber = "930001"
        });
        var motherId = await CreateBirdAsync(client, new
        {
            name = "Mãe Rubi",
            sex = "Female",
            speciesId,
            birthDate = "2019-07-01",
            ringNumber = "930002"
        });
        var childId = await CreateBirdAsync(client, new
        {
            name = "Filhote Aurora",
            sex = "Female",
            speciesId,
            birthDate = "2020-09-07",
            ringNumber = "930003"
        });

        using var nameSearch = await client.GetAsync("/api/birds/parent-options?search=azul&sex=Male&limit=5");
        Assert.Equal(HttpStatusCode.OK, nameSearch.StatusCode);
        using var nameSearchDocument = JsonDocument.Parse(await nameSearch.Content.ReadAsStreamAsync());
        Assert.Equal(fatherId, nameSearchDocument.RootElement.GetProperty("items")[0].GetProperty("birdId").GetGuid());

        using var ringSearch = await client.GetAsync("/api/birds/parent-options?search=930002&sex=Female&limit=5");
        Assert.Equal(HttpStatusCode.OK, ringSearch.StatusCode);
        using var ringSearchDocument = JsonDocument.Parse(await ringSearch.Content.ReadAsStreamAsync());
        Assert.Equal(motherId, ringSearchDocument.RootElement.GetProperty("items")[0].GetProperty("birdId").GetGuid());

        using var linkResponse = await UpdateGenealogyAsync(client, childId, fatherId, motherId);
        Assert.Equal(HttpStatusCode.OK, linkResponse.StatusCode);

        using var detailsResponse = await client.GetAsync($"/api/birds/{childId}");
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        using var details = JsonDocument.Parse(await detailsResponse.Content.ReadAsStreamAsync());
        Assert.Equal("Pai Azul", details.RootElement.GetProperty("father").GetProperty("name").GetString());
        Assert.Equal("Mãe Rubi", details.RootElement.GetProperty("mother").GetProperty("name").GetString());
        var firstUpdatedAt = details.RootElement.GetProperty("updatedAtUtc").GetDateTimeOffset();

        using var repeatResponse = await UpdateGenealogyAsync(client, childId, fatherId, motherId);
        Assert.Equal(HttpStatusCode.OK, repeatResponse.StatusCode);

        using var parentUpdate = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            $"/api/birds/{fatherId}",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                name = "Pai Azul Atualizado",
                sex = "Male",
                speciesId,
                birthDate = "2018-06-01",
                ringNumber = "930004"
            }));
        Assert.Equal(HttpStatusCode.OK, parentUpdate.StatusCode);

        using var snapshotResponse = await client.GetAsync($"/api/birds/{childId}");
        Assert.Equal(HttpStatusCode.OK, snapshotResponse.StatusCode);
        using var snapshot = JsonDocument.Parse(await snapshotResponse.Content.ReadAsStreamAsync());
        Assert.Equal("Pai Azul", snapshot.RootElement.GetProperty("father").GetProperty("name").GetString());
        Assert.Equal("930001", snapshot.RootElement.GetProperty("father").GetProperty("ringNumber").GetString());
        Assert.Equal(firstUpdatedAt, snapshot.RootElement.GetProperty("updatedAtUtc").GetDateTimeOffset());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var rootId = await dbContext.GenealogyNodes
            .Where(node => node.BirdId == childId && node.IsRoot)
            .Select(node => node.Id)
            .SingleAsync();
        var nodes = await dbContext.GenealogyNodes
            .Where(node => node.GenealogyRootId == rootId && !node.IsRoot)
            .ToArrayAsync();
        Assert.Equal(2, nodes.Length);
        Assert.Contains(nodes, node => node.Position == "father" && node.LinkedBirdId == fatherId && node.SnapshotName == "Pai Azul");
        Assert.Contains(nodes, node => node.Position == "mother" && node.LinkedBirdId == motherId && node.SnapshotName == "Mãe Rubi");
        Assert.All(nodes, node => Assert.Equal(farmId, node.BreedingFarmId));
    }

    [Fact]
    public async Task LinkRejectsCrossTenantWrongSexAndIndirectCycleAndTransferPending()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "genealogy-owner@example.com");
        await RegisterAndAuthenticateAsync(factory, otherClient, "genealogy-other@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient);
        var otherFarmId = await CreateFarmAsync(otherClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);
        await SelectFarmAsync(otherClient, otherFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);

        var ancestorId = await CreateBirdAsync(ownerClient, new
        {
            name = "Ancestral",
            sex = "Male",
            speciesId,
            birthDate = "2017-01-01",
            ringNumber = "931001"
        });
        var middleId = await CreateBirdAsync(ownerClient, new
        {
            name = "Intermediária",
            sex = "Male",
            speciesId,
            birthDate = "2018-01-01",
            ringNumber = "931002"
        });
        var targetId = await CreateBirdAsync(ownerClient, new
        {
            name = "Alvo",
            sex = "Male",
            speciesId,
            birthDate = "2019-01-01",
            ringNumber = "931003"
        });
        var femaleId = await CreateBirdAsync(ownerClient, new
        {
            name = "Fêmea",
            sex = "Female",
            speciesId,
            birthDate = "2019-02-01",
            ringNumber = "931004"
        });
        var foreignId = await CreateBirdAsync(otherClient, new
        {
            name = "Estrangeiro",
            sex = "Male",
            speciesId,
            birthDate = "2016-01-01",
            ringNumber = "931005"
        });

        using var wrongSex = await UpdateGenealogyAsync(ownerClient, targetId, femaleId, null);
        Assert.Equal(HttpStatusCode.BadRequest, wrongSex.StatusCode);

        using var crossTenant = await UpdateGenealogyAsync(ownerClient, targetId, foreignId, null);
        Assert.Equal(HttpStatusCode.BadRequest, crossTenant.StatusCode);

        using var middleLink = await UpdateGenealogyAsync(ownerClient, middleId, ancestorId, null);
        Assert.Equal(HttpStatusCode.OK, middleLink.StatusCode);
        using var cycle = await UpdateGenealogyAsync(ownerClient, ancestorId, middleId, null);
        Assert.Equal(HttpStatusCode.BadRequest, cycle.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var target = await dbContext.Birds.SingleAsync(bird => bird.Id == targetId);
            dbContext.Entry(target).Property(bird => bird.Status).CurrentValue = BirdStatus.Transferred;
            await dbContext.SaveChangesAsync();
        }

        using var transferPending = await UpdateGenealogyAsync(ownerClient, targetId, null, femaleId);
        Assert.Equal(HttpStatusCode.Conflict, transferPending.StatusCode);
    }

    [Fact]
    public async Task DatabaseRejectsCrossTreeParentAndDuplicateGenealogyRoot()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "genealogy-integrity-owner@example.com");
        await RegisterAndAuthenticateAsync(factory, otherClient, "genealogy-integrity-other@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient);
        var otherFarmId = await CreateFarmAsync(otherClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);
        await SelectFarmAsync(otherClient, otherFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);

        var ownerChildId = await CreateBirdAsync(ownerClient, new
        {
            name = "Filhote protegido",
            sex = "Female",
            speciesId,
            birthDate = "2020-09-07",
            ringNumber = "934001"
        });
        var foreignParentId = await CreateBirdAsync(otherClient, new
        {
            name = "Pai de outra farm",
            sex = "Male",
            speciesId,
            birthDate = "2018-06-01",
            ringNumber = "934002"
        });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var ownerChild = await dbContext.Birds.SingleAsync(bird => bird.Id == ownerChildId);
            ownerChild.UpdateParents(
                foreignParentId,
                null,
                null,
                null,
                null,
                null,
                DateTimeOffset.UtcNow);

            await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            await dbContext.GenealogyNodes
                .SingleAsync(node => node.BirdId == ownerChildId && node.IsRoot);
            dbContext.GenealogyNodes.Add(new GenealogyNode(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                ownerFarmId,
                ownerChildId));

            await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task LinkAllowsTheSameAncestorThroughDifferentBranches()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "genealogy-repeated-ancestor@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);

        var sharedAncestorId = await CreateBirdAsync(client, new
        {
            name = "Ancestral compartilhado",
            sex = "Male",
            speciesId,
            birthDate = "2015-01-01",
            ringNumber = "935001"
        });
        var fatherId = await CreateBirdAsync(client, new
        {
            name = "Pai da linhagem",
            sex = "Male",
            speciesId,
            birthDate = "2018-01-01",
            ringNumber = "935002"
        });
        var motherId = await CreateBirdAsync(client, new
        {
            name = "Mãe da linhagem",
            sex = "Female",
            speciesId,
            birthDate = "2018-02-01",
            ringNumber = "935003"
        });
        var childId = await CreateBirdAsync(client, new
        {
            name = "Descendente",
            sex = "Female",
            speciesId,
            birthDate = "2020-09-07",
            ringNumber = "935004"
        });

        using var fatherLink = await UpdateGenealogyAsync(client, fatherId, sharedAncestorId, null);
        Assert.Equal(HttpStatusCode.OK, fatherLink.StatusCode);
        using var motherLink = await UpdateGenealogyAsync(client, motherId, sharedAncestorId, null);
        Assert.Equal(HttpStatusCode.OK, motherLink.StatusCode);

        using var childLink = await UpdateGenealogyAsync(client, childId, fatherId, motherId);
        Assert.Equal(HttpStatusCode.OK, childLink.StatusCode);
    }

    [Fact]
    public async Task ParentOptionsRequireSelectionAndNeverReturnAnotherTenant()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        var speciesId = await GetSpeciesIdAsync(factory);

        using var unauthenticated = await ownerClient.GetAsync("/api/birds/parent-options?search=Pai");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await RegisterAndAuthenticateAsync(factory, ownerClient, "genealogy-options-owner@example.com");
        await RegisterAndAuthenticateAsync(factory, otherClient, "genealogy-options-other@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient);
        var otherFarmId = await CreateFarmAsync(otherClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);
        await SelectFarmAsync(otherClient, otherFarmId);
        await CreateBirdAsync(otherClient, new
        {
            name = "Pai de outro tenant",
            sex = "Male",
            speciesId,
            birthDate = "2018-01-01",
            ringNumber = "932001"
        });

        using var options = await ownerClient.GetAsync("/api/birds/parent-options?search=Pai&sex=Male");
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        using var document = JsonDocument.Parse(await options.Content.ReadAsStreamAsync());
        Assert.Empty(document.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task ExternalAncestorsAreSavedWithSexWithoutCreatingPlantelBirds()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "external-ancestor@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var childId = await CreateBirdAsync(client, new
        {
            name = "Filhote externo",
            sex = "Unknown",
            speciesId,
            birthDate = "2020-09-07",
            ringNumber = (string?)null
        });

        using var update = await UpdateGenealogyAsync(
            client,
            childId,
            null,
            null,
            "Pai sem cadastro",
            "Male",
            "Mãe sem cadastro",
            "Female");
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var updated = JsonDocument.Parse(await update.Content.ReadAsStreamAsync());
        Assert.Equal("Pai sem cadastro", updated.RootElement.GetProperty("externalFatherName").GetString());
        Assert.Equal("Male", updated.RootElement.GetProperty("externalFatherSex").GetString());
        Assert.Equal("Mãe sem cadastro", updated.RootElement.GetProperty("externalMotherName").GetString());
        Assert.Equal("Female", updated.RootElement.GetProperty("externalMotherSex").GetString());
        Assert.Equal(JsonValueKind.Null, updated.RootElement.GetProperty("fatherBirdId").ValueKind);
        Assert.Equal(JsonValueKind.Null, updated.RootElement.GetProperty("motherBirdId").ValueKind);

        using var detailsResponse = await client.GetAsync($"/api/birds/{childId}");
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        using var details = JsonDocument.Parse(await detailsResponse.Content.ReadAsStreamAsync());
        Assert.Equal("Male", details.RootElement.GetProperty("externalFatherSex").GetString());
        Assert.Equal("Female", details.RootElement.GetProperty("externalMotherSex").GetString());

        using var plantel = await client.GetAsync("/api/birds?search=sem%20cadastro");
        Assert.Equal(HttpStatusCode.OK, plantel.StatusCode);
        using var plantelDocument = JsonDocument.Parse(await plantel.Content.ReadAsStreamAsync());
        Assert.Empty(plantelDocument.RootElement.GetProperty("items").EnumerateArray());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var storedBird = await dbContext.Birds.SingleAsync(bird => bird.Id == childId);
        Assert.Equal("Pai sem cadastro", storedBird.ExternalFatherName);
        Assert.Equal(BirdSex.Male, storedBird.ExternalFatherSex);
        Assert.Equal("Mãe sem cadastro", storedBird.ExternalMotherName);
        Assert.Equal(BirdSex.Female, storedBird.ExternalMotherSex);
        Assert.DoesNotContain(
            await dbContext.Birds.Where(bird => bird.Name.Contains("sem cadastro")).ToArrayAsync(),
            bird => bird.Id != childId);
        Assert.Single(await dbContext.GenealogyNodes.Where(node => node.BirdId == childId).ToArrayAsync());
        var rootId = await dbContext.GenealogyNodes
            .Where(node => node.BirdId == childId && node.IsRoot)
            .Select(node => node.Id)
            .SingleAsync();
        var externalNodes = await dbContext.ExternalGenealogyNodes
            .Where(node => node.GenealogyRootId == rootId)
            .ToArrayAsync();
        var externalLinks = await dbContext.ExternalGenealogyParentLinks
            .Where(link => link.GenealogyRootId == rootId && link.ChildBirdId == childId)
            .ToArrayAsync();
        Assert.Equal(2, externalNodes.Length);
        Assert.Equal(2, externalLinks.Length);
        Assert.Contains(externalNodes, node => node.Name == "Pai sem cadastro" && node.Sex == BirdSex.Male);
        Assert.Contains(externalNodes, node => node.Name == "Mãe sem cadastro" && node.Sex == BirdSex.Female);
        Assert.All(externalLinks, link =>
        {
            Assert.Equal(childId, link.ChildBirdId);
            Assert.Null(link.ChildExternalNodeId);
            Assert.Null(link.ParentBirdId);
            Assert.NotNull(link.ParentExternalNodeId);
        });
    }

    [Fact]
    public async Task ExternalGenealogyNodesSupportThreeLevelsMixedBranchesAndUniquePositions()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "external-recursive-model@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var rootBirdId = await CreateBirdAsync(client, new
        {
            name = "Raiz recursiva",
            sex = "Female",
            speciesId,
            birthDate = "2020-09-07",
            ringNumber = "938001"
        });
        var mixedBirdId = await CreateBirdAsync(client, new
        {
            name = "Mãe snapshot",
            sex = "Female",
            speciesId,
            birthDate = "2018-09-07",
            ringNumber = "938002"
        });

        Guid rootId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            rootId = await dbContext.GenealogyNodes
                .Where(node => node.BirdId == rootBirdId && node.IsRoot)
                .Select(node => node.Id)
                .SingleAsync();
            var mixedBird = await dbContext.Birds.SingleAsync(candidate => candidate.Id == mixedBirdId);
            var now = DateTimeOffset.UtcNow;
            var externalFather = new ExternalGenealogyNode(
                Guid.NewGuid(), now, farmId, rootId, "Pai externo", BirdSex.Male);
            var externalGrandfather = new ExternalGenealogyNode(
                Guid.NewGuid(), now, farmId, rootId, "Avô externo", BirdSex.Male);
            var externalGreatGrandfather = new ExternalGenealogyNode(
                Guid.NewGuid(), now, farmId, rootId, "Bisavô externo", BirdSex.Male);
            dbContext.ExternalGenealogyNodes.AddRange(
                externalFather,
                externalGrandfather,
                externalGreatGrandfather);
            dbContext.ExternalGenealogyParentLinks.AddRange(
                new ExternalGenealogyParentLink(
                    Guid.NewGuid(), now, farmId, rootId, rootBirdId, null,
                    ExternalGenealogyParentLink.FatherPosition, null, externalFather.Id,
                    null, null, null, null, null, null),
                new ExternalGenealogyParentLink(
                    Guid.NewGuid(), now, farmId, rootId, null, externalFather.Id,
                    ExternalGenealogyParentLink.FatherPosition, null, externalGrandfather.Id,
                    null, null, null, null, null, null),
                new ExternalGenealogyParentLink(
                    Guid.NewGuid(), now, farmId, rootId, null, externalGrandfather.Id,
                    ExternalGenealogyParentLink.FatherPosition, null, externalGreatGrandfather.Id,
                    null, null, null, null, null, null),
                new ExternalGenealogyParentLink(
                    Guid.NewGuid(), now, farmId, rootId, null, externalGrandfather.Id,
                    ExternalGenealogyParentLink.MotherPosition, mixedBird.Id, null,
                    farmId, mixedBird.Name, mixedBird.Sex, mixedBird.BirthDate,
                    mixedBird.RingNumber, mixedBird.Status));
            await dbContext.SaveChangesAsync();

            var nodes = await dbContext.ExternalGenealogyNodes
                .Where(node => node.GenealogyRootId == rootId)
                .ToArrayAsync();
            var links = await dbContext.ExternalGenealogyParentLinks
                .Where(link => link.GenealogyRootId == rootId)
                .ToArrayAsync();
            Assert.Equal(3, nodes.Length);
            Assert.Equal(4, links.Length);
            Assert.Contains(links, link =>
                link.ChildExternalNodeId == externalGrandfather.Id &&
                link.ParentBirdId == mixedBird.Id &&
                link.Position == ExternalGenealogyParentLink.MotherPosition);

            dbContext.ExternalGenealogyParentLinks.Add(new ExternalGenealogyParentLink(
                Guid.NewGuid(), now, farmId, rootId, rootBirdId, null,
                ExternalGenealogyParentLink.FatherPosition, null, externalFather.Id,
                null, null, null, null, null, null));
            await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task ExternalAncestorSexMustMatchItsParentalPosition()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "external-ancestor-validation@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var childId = await CreateBirdAsync(client, new
        {
            name = "Filhote validado",
            sex = "Female",
            speciesId,
            birthDate = "2020-09-07",
            ringNumber = "933001"
        });

        using var response = await UpdateGenealogyAsync(
            client,
            childId,
            null,
            null,
            "Pai com sexo inválido",
            "Female",
            null,
            null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var bird = await dbContext.Birds.SingleAsync(candidate => candidate.Id == childId);
        Assert.Null(bird.ExternalFatherName);
        Assert.Null(bird.ExternalFatherSex);
    }

    private static async Task<HttpResponseMessage> UpdateGenealogyAsync(
        HttpClient client,
        Guid birdId,
        Guid? fatherBirdId,
        Guid? motherBirdId,
        string? externalFatherName = null,
        string? externalFatherSex = null,
        string? externalMotherName = null,
        string? externalMotherSex = null) =>
        await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            $"/api/birds/{birdId}/genealogy",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                fatherBirdId,
                externalFatherName,
                externalFatherSex,
                motherBirdId,
                externalMotherName,
                externalMotherSex
            }));

    private static async Task<Guid> CreateBirdAsync(HttpClient client, object request)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/birds",
            await GetAntiforgeryTokenAsync(client),
            request));
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
