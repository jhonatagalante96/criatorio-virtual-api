using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Application.Identity;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Infrastructure.Billing;
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

namespace CriatorioVirtual.IntegrationTests.Billing;

public sealed class HomologationBillingEndpointTests
{
    [Fact]
    public async Task ProductionEnvironment_ReturnsNotFound_AndDisallowsSimulation()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, "Production");
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        await RegisterAndAuthenticateAsync(factory, client, "prod-user@example.com");
        var farmId = await CreateFarmAsync(client, "Production Farm");
        await SelectFarmAsync(client, farmId);

        using var simulateResponse = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/homologation/billing/simulation",
            await GetAntiforgeryTokenAsync(client),
            new { state = "active" }));

        Assert.Equal(HttpStatusCode.NotFound, simulateResponse.StatusCode);
    }

    [Fact]
    public async Task ActiveState_EnablesFunctionalAccess_AndReturns201()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, AsaasOptions.HomologationEnvironmentName);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        await RegisterAndAuthenticateAsync(factory, client, "homolog-active@example.com");
        var farmId = await CreateFarmAsync(client, "Homolog Active Farm");
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);

        // Before simulation: farm has no subscription => blocked (403)
        using var initialBirdResponse = await TryCreateBirdAsync(client, speciesId, "Bird Before");
        Assert.Equal(HttpStatusCode.Forbidden, initialBirdResponse.StatusCode);

        // Simulate Active
        using var simulateResponse = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/homologation/billing/simulation",
            await GetAntiforgeryTokenAsync(client),
            new { state = "active" }));
        Assert.Equal(HttpStatusCode.OK, simulateResponse.StatusCode);

        // After simulation: farm is Active => allowed (201)
        using var allowedBirdResponse = await TryCreateBirdAsync(client, speciesId, "Bird Active");
        Assert.Equal(HttpStatusCode.Created, allowedBirdResponse.StatusCode);
    }

    [Fact]
    public async Task TrialState_EnablesFunctionalAccess_AndReturns201()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, AsaasOptions.HomologationEnvironmentName);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        await RegisterAndAuthenticateAsync(factory, client, "homolog-trial@example.com");
        var farmId = await CreateFarmAsync(client, "Homolog Trial Farm");
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);

        // Simulate Trial
        using var simulateResponse = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/homologation/billing/simulation",
            await GetAntiforgeryTokenAsync(client),
            new { state = "trial" }));
        Assert.Equal(HttpStatusCode.OK, simulateResponse.StatusCode);

        // Functional access allowed (201)
        using var birdResponse = await TryCreateBirdAsync(client, speciesId, "Bird Trial");
        Assert.Equal(HttpStatusCode.Created, birdResponse.StatusCode);
    }

    [Fact]
    public async Task GracePeriodState_EnablesFunctionalAccess_AndReturns201()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, AsaasOptions.HomologationEnvironmentName);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        await RegisterAndAuthenticateAsync(factory, client, "homolog-grace@example.com");
        var farmId = await CreateFarmAsync(client, "Homolog Grace Farm");
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);

        // Simulate GracePeriod
        using var simulateResponse = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/homologation/billing/simulation",
            await GetAntiforgeryTokenAsync(client),
            new { state = "gracePeriod" }));
        Assert.Equal(HttpStatusCode.OK, simulateResponse.StatusCode);

        // Functional access allowed (201)
        using var birdResponse = await TryCreateBirdAsync(client, speciesId, "Bird Grace");
        Assert.Equal(HttpStatusCode.Created, birdResponse.StatusCode);
    }

    [Fact]
    public async Task PendingSubscriptionState_BlocksFunctionalAccess_AndReturns403()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, AsaasOptions.HomologationEnvironmentName);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        await RegisterAndAuthenticateAsync(factory, client, "homolog-pending@example.com");
        var farmId = await CreateFarmAsync(client, "Homolog Pending Farm");
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);

        // Simulate PendingSubscription
        using var simulateResponse = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/homologation/billing/simulation",
            await GetAntiforgeryTokenAsync(client),
            new { state = "pendingSubscription" }));
        Assert.Equal(HttpStatusCode.OK, simulateResponse.StatusCode);

        // Functional access blocked (403)
        using var birdResponse = await TryCreateBirdAsync(client, speciesId, "Bird Blocked");
        Assert.Equal(HttpStatusCode.Forbidden, birdResponse.StatusCode);

        using var jsonDoc = JsonDocument.Parse(await birdResponse.Content.ReadAsStreamAsync());
        Assert.Equal("functional_access_blocked", jsonDoc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BlockedState_BlocksFunctionalAccess_AndReturns403()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, AsaasOptions.HomologationEnvironmentName);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        await RegisterAndAuthenticateAsync(factory, client, "homolog-blocked@example.com");
        var farmId = await CreateFarmAsync(client, "Homolog Blocked Farm");
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);

        // Simulate Blocked
        using var simulateResponse = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/homologation/billing/simulation",
            await GetAntiforgeryTokenAsync(client),
            new { state = "blocked" }));
        Assert.Equal(HttpStatusCode.OK, simulateResponse.StatusCode);

        // Functional access blocked (403)
        using var birdResponse = await TryCreateBirdAsync(client, speciesId, "Bird Blocked 2");
        Assert.Equal(HttpStatusCode.Forbidden, birdResponse.StatusCode);

        using var jsonDoc = JsonDocument.Parse(await birdResponse.Content.ReadAsStreamAsync());
        Assert.Equal("functional_access_blocked", jsonDoc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CancelledState_BlocksFunctionalAccess_AndReturns403()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, AsaasOptions.HomologationEnvironmentName);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        await RegisterAndAuthenticateAsync(factory, client, "homolog-cancelled@example.com");
        var farmId = await CreateFarmAsync(client, "Homolog Cancelled Farm");
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);

        // Simulate Cancelled
        using var simulateResponse = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/homologation/billing/simulation",
            await GetAntiforgeryTokenAsync(client),
            new { state = "cancelled" }));
        Assert.Equal(HttpStatusCode.OK, simulateResponse.StatusCode);

        // Functional access blocked (403)
        using var birdResponse = await TryCreateBirdAsync(client, speciesId, "Bird Cancelled");
        Assert.Equal(HttpStatusCode.Forbidden, birdResponse.StatusCode);

        using var jsonDoc = JsonDocument.Parse(await birdResponse.Content.ReadAsStreamAsync());
        Assert.Equal("functional_access_blocked", jsonDoc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task NoneState_BlocksFunctionalAccess_AndReturns403()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, AsaasOptions.HomologationEnvironmentName);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        await RegisterAndAuthenticateAsync(factory, client, "homolog-none@example.com");
        var farmId = await CreateFarmAsync(client, "Homolog None Farm");
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);

        // First activate
        using var activateResponse = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/homologation/billing/simulation",
            await GetAntiforgeryTokenAsync(client),
            new { state = "active" }));
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);

        // Confirm active works
        using var birdOk = await TryCreateBirdAsync(client, speciesId, "Bird Ok");
        Assert.Equal(HttpStatusCode.Created, birdOk.StatusCode);

        // Now simulate None (remove subscription)
        using var noneResponse = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/homologation/billing/simulation",
            await GetAntiforgeryTokenAsync(client),
            new { state = "none" }));
        Assert.Equal(HttpStatusCode.OK, noneResponse.StatusCode);

        // Now functional access blocked (403)
        using var birdBlocked = await TryCreateBirdAsync(client, speciesId, "Bird No Sub");
        Assert.Equal(HttpStatusCode.Forbidden, birdBlocked.StatusCode);

        using var jsonDoc = JsonDocument.Parse(await birdBlocked.Content.ReadAsStreamAsync());
        Assert.Equal("functional_access_blocked", jsonDoc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AccessContext_ReflectsSimulatedStateInHomologation()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, AsaasOptions.HomologationEnvironmentName);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        await RegisterAndAuthenticateAsync(factory, client, "homolog-access-ctx@example.com");
        var farmId = await CreateFarmAsync(client, "Homolog Access Context Farm");
        await SelectFarmAsync(client, farmId);

        // Simulate Active
        using var simActive = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/homologation/billing/simulation",
            await GetAntiforgeryTokenAsync(client),
            new { state = "active" }));
        Assert.Equal(HttpStatusCode.OK, simActive.StatusCode);

        // Query /api/me/access-context
        using var activeContext = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Get,
            "/api/me/access-context",
            await GetAntiforgeryTokenAsync(client)));
        Assert.Equal(HttpStatusCode.OK, activeContext.StatusCode);

        using var activeDoc = JsonDocument.Parse(await activeContext.Content.ReadAsStreamAsync());
        var accessElement = activeDoc.RootElement.GetProperty("access");
        Assert.True(accessElement.GetProperty("canAccessApp").GetBoolean());
        Assert.Equal("Active", accessElement.GetProperty("status").GetString());
    }

    private const string ApplicationOrigin = "https://app.example.com";
    private const string ApiOrigin = "https://api.example.com";

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        X509Certificate2 certificate,
        string environment) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            var certificateBytes = certificate.Export(X509ContentType.Pkcs12, TestCertificate.Password);
            var certificateBase64 = Convert.ToBase64String(certificateBytes);

            builder.UseSetting("ConnectionStrings:CriatorioVirtual", connectionString);
            builder.UseSetting("Logging:EventLog:LogLevel:Default", "None");
            builder.UseSetting("Security:DataProtection:CertificateBase64", certificateBase64);
            builder.UseSetting("Security:DataProtection:CertificatePassword", TestCertificate.Password);
            builder.UseSetting("Billing:Asaas:ApiKey", "sandbox-test-key");
            builder.UseSetting("Billing:Asaas:WebhookToken", "sandbox-webhook-secret-0123456789");
            builder.UseSetting("Security:Email:ClientBaseUrl", ApplicationOrigin);
            builder.UseSetting("Security:Email:Provider", "Resend");
            builder.UseSetting("Security:Email:ResendApiKey", "re_test_key_12345678");
            builder.UseSetting("Security:Email:SenderAddress", "no-reply@app.example.com");
            builder.UseSetting("Security:AllowedOrigins:0", ApplicationOrigin);
            builder.UseSetting("Security:AllowedOrigins:1", ApiOrigin);
            builder.UseSetting("Security:Passkeys:ServerDomain", "example.com");
            builder.UseSetting("Storage:Provider", "S3");
            builder.UseSetting("Storage:S3:Endpoint", "https://s3.us-east-1.amazonaws.com");
            builder.UseSetting("Storage:S3:Bucket", "test-bucket");
            builder.UseSetting("Storage:S3:Region", "us-east-1");
            builder.UseSetting("Storage:S3:AccessKeyId", "test-key-id");
            builder.UseSetting("Storage:S3:SecretAccessKey", "test-secret-key");
            builder.UseSetting("Storage:SpeciesDefaultImagesRootPath", Path.GetFullPath(Path.GetTempPath()));
            builder.ConfigureServices(services =>
            {
                services.AddInfrastructurePersistence(connectionString, certificate);
                services.AddSingleton<IAuthenticationEmailSender, TestAuthenticationEmailSender>();
            });
        });

    private sealed class TestAuthenticationEmailSender : IAuthenticationEmailSender
    {
        public Task<AuthenticationEmailDeliveryResult> SendAsync(
            AuthenticationEmailMessage message,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthenticationEmailDeliveryResult.Delivered());
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(ApiOrigin),
            HandleCookies = true,
            AllowAutoRedirect = false
        });

    private static async Task<HttpResponseMessage> TryCreateBirdAsync(
        HttpClient client,
        Guid speciesId,
        string name)
    {
        return await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/birds",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                speciesId,
                name,
                birthDate = "2024-01-01",
                ringNumber = Random.Shared.Next(100000, 999999).ToString(),
                sex = "Male"
            }));
    }

    private static async Task<Guid> RegisterAndAuthenticateAsync(
        WebApplicationFactory<Program> factory,
        HttpClient client,
        string email)
    {
        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);
        using var registration = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/register",
            antiforgeryToken,
            new { email, password = "StrongPassword!123", confirmPassword = "StrongPassword!123" }));
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
            antiforgeryToken,
            new { email, password = "StrongPassword!123" }));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return userId;
    }

    private static async Task<Guid> CreateFarmAsync(HttpClient client, string name)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/breeding-farms",
            await GetAntiforgeryTokenAsync(client),
            new { name, responsibleName = "Owner Principal", contactEmail = "owner@example.com" }));
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
        request.Headers.Add("Origin", ApplicationOrigin);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return response.Headers.GetValues(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName).Single();
    }

    private static HttpRequestMessage CreateBrowserRequest(
        HttpMethod method,
        string path,
        string antiforgeryToken,
        object? payload = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Origin", ApplicationOrigin);
        request.Headers.Add("Referer", $"{ApplicationOrigin}/");
        request.Headers.Add(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName, antiforgeryToken);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }

        return request;
    }
}
