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

namespace CriatorioVirtual.IntegrationTests.BreedingFarms;

public sealed class BreedingFarmSelectionEndpointTests
{
    [Fact]
    public async Task OwnerCanListFarmsSelectOneAndResumeSelectionAfterLoggingInAgain()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "owner@example.com");

        var firstFarmId = await CreateFarmAsync(client, "Sítio Aurora");
        var secondFarmId = await CreateFarmAsync(client, "Sítio Boreal");

        using var initial = await client.GetAsync("/api/breeding-farms");
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        using var initialDocument = JsonDocument.Parse(await initial.Content.ReadAsStreamAsync());
        Assert.Equal(JsonValueKind.Null, initialDocument.RootElement.GetProperty("selectedBreedingFarmId").ValueKind);
        var initialFarms = initialDocument.RootElement.GetProperty("breedingFarms").EnumerateArray().ToArray();
        Assert.Equal(2, initialFarms.Length);
        Assert.Equal("Sítio Aurora", initialFarms[0].GetProperty("name").GetString());
        Assert.Equal("Sítio Boreal", initialFarms[1].GetProperty("name").GetString());
        Assert.All(initialFarms, farm => Assert.False(farm.GetProperty("isSelected").GetBoolean()));

        using var select = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            "/api/breeding-farms/selection",
            await GetAntiforgeryTokenAsync(client),
            new { breedingFarmId = secondFarmId }));
        Assert.Equal(HttpStatusCode.OK, select.StatusCode);
        using var selectDocument = JsonDocument.Parse(await select.Content.ReadAsStreamAsync());
        Assert.Equal(secondFarmId, selectDocument.RootElement.GetProperty("selectedBreedingFarmId").GetGuid());
        var selectedFarms = selectDocument.RootElement.GetProperty("breedingFarms").EnumerateArray().ToArray();
        Assert.False(selectedFarms.Single(farm => farm.GetProperty("breedingFarmId").GetGuid() == firstFarmId).GetProperty("isSelected").GetBoolean());
        Assert.True(selectedFarms.Single(farm => farm.GetProperty("breedingFarmId").GetGuid() == secondFarmId).GetProperty("isSelected").GetBoolean());

        using var resumedClient = CreateClient(factory);
        await AuthenticateExistingUserAsync(resumedClient, "owner@example.com");
        using var resumed = await resumedClient.GetAsync("/api/breeding-farms");
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        using var resumedDocument = JsonDocument.Parse(await resumed.Content.ReadAsStreamAsync());
        Assert.Equal(secondFarmId, resumedDocument.RootElement.GetProperty("selectedBreedingFarmId").GetGuid());
    }

    [Fact]
    public async Task UserCannotListOrSelectAnotherUsersFarm()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "owner@example.com");
        await RegisterAndAuthenticateAsync(factory, otherClient, "other@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient, "Sítio Aurora");
        var otherFarmId = await CreateFarmAsync(otherClient, "Sítio Boreal");

        using var ownerSelection = await ownerClient.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            "/api/breeding-farms/selection",
            await GetAntiforgeryTokenAsync(ownerClient),
            new { breedingFarmId = ownerFarmId }));
        Assert.Equal(HttpStatusCode.OK, ownerSelection.StatusCode);

        using var unauthorizedSelection = await otherClient.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            "/api/breeding-farms/selection",
            await GetAntiforgeryTokenAsync(otherClient),
            new { breedingFarmId = ownerFarmId }));
        Assert.Equal(HttpStatusCode.NotFound, unauthorizedSelection.StatusCode);

        using var otherFarms = await otherClient.GetAsync("/api/breeding-farms");
        Assert.Equal(HttpStatusCode.OK, otherFarms.StatusCode);
        using var otherFarmsDocument = JsonDocument.Parse(await otherFarms.Content.ReadAsStreamAsync());
        var visibleFarms = otherFarmsDocument.RootElement.GetProperty("breedingFarms").EnumerateArray().ToArray();
        Assert.Single(visibleFarms);
        Assert.Equal(otherFarmId, visibleFarms[0].GetProperty("breedingFarmId").GetGuid());
        Assert.DoesNotContain(ownerFarmId, visibleFarms.Select(farm => farm.GetProperty("breedingFarmId").GetGuid()));
        Assert.Equal(JsonValueKind.Null, otherFarmsDocument.RootElement.GetProperty("selectedBreedingFarmId").ValueKind);
    }

    [Fact]
    public async Task SelectionRequiresAuthenticationAndAValidFarmIdentifier()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        using var unauthenticated = await client.GetAsync("/api/breeding-farms");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await RegisterAndAuthenticateAsync(factory, client, "owner@example.com");
        using var invalid = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            "/api/breeding-farms/selection",
            await GetAntiforgeryTokenAsync(client),
            new { breedingFarmId = Guid.Empty }));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
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
            new { email, password = "StrongPassword!123" }));
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

    private static async Task<Guid> CreateFarmAsync(HttpClient client, string name)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/breeding-farms",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                name,
                responsibleName = "Owner Principal",
                contactEmail = "owner@example.com"
            }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("breedingFarmId").GetGuid();
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
