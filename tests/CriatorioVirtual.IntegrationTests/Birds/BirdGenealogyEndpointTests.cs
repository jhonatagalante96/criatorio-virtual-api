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

    private static async Task<HttpResponseMessage> UpdateGenealogyAsync(
        HttpClient client,
        Guid birdId,
        Guid? fatherBirdId,
        Guid? motherBirdId) =>
        await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            $"/api/birds/{birdId}/genealogy",
            await GetAntiforgeryTokenAsync(client),
            new { fatherBirdId, motherBirdId }));

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
