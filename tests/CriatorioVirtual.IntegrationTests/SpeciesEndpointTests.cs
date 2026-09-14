using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Api.Controllers;
using SpeciesEntity = CriatorioVirtual.Domain.Species.Species;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests;

public sealed class SpeciesEndpointTests
{
    [Fact]
    public async Task AuthenticatedSearchIsCaseAndAccentInsensitiveAndReturnsSeededSpecies()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "species-search@example.com");

        using var response = await client.GetAsync("/api/species?search=S%C3%81BIA");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var species = await response.Content.ReadFromJsonAsync<SpeciesResponse[]>();
        Assert.NotNull(species);
        Assert.Contains(species, candidate =>
            candidate.ScientificName == "Turdus rufiventris" &&
            candidate.PopularName == "Sabiá-laranjeira" &&
            candidate.DefaultImageUrl == "/species-images/0047.jpg");
        Assert.All(species, candidate => Assert.Contains("Sabi", candidate.PopularName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SpeciesDefaultImageRouteServesProvisionedCatalogAssetAndRejectsUnknownFile()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        using var image = await client.GetAsync("/species-images/0001.jpg");

        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
        Assert.True((await image.Content.ReadAsByteArrayAsync()).Length > 0);

        using var unknown = await client.GetAsync("/species-images/not-a-species-image.jpg");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task SearchReturnsOnlyActiveSpeciesAndTheInitialCatalogHasSixtyEntries()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            Assert.Equal(60, await dbContext.Species.CountAsync());
            dbContext.Species.Add(new SpeciesEntity(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                "Species inactiveus",
                "Sabiá oculto",
                isActive: false));
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "species-active@example.com");

        using var response = await client.GetAsync("/api/species?search=sabia");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var species = await response.Content.ReadFromJsonAsync<SpeciesResponse[]>();
        Assert.NotNull(species);
        Assert.DoesNotContain(species, candidate => candidate.PopularName == "Sabiá oculto");
    }

    [Fact]
    public async Task SearchRequiresAuthenticationAndRejectsAnOverlongQuery()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        using var unauthenticated = await client.GetAsync("/api/species");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await RegisterAndAuthenticateAsync(factory, client, "species-validation@example.com");
        using var invalid = await client.GetAsync($"/api/species?search={new string('a', 101)}");

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var document = JsonDocument.Parse(await invalid.Content.ReadAsStreamAsync());
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("errors").GetProperty("search").ValueKind);
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
