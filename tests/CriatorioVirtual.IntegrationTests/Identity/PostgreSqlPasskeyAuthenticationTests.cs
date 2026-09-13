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

namespace CriatorioVirtual.IntegrationTests.Identity;

public sealed class PostgreSqlPasskeyAuthenticationTests
{
    [Fact]
    public async Task PasskeyLoginOptionsAndInvalidAssertions_AreAnonymousAntiforgeryProtectedAndGeneric()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        using var missingAntiforgery = new HttpRequestMessage(HttpMethod.Post, "/api/auth/passkeys/login/options");
        missingAntiforgery.Headers.Add("Origin", "http://localhost:3000");
        using var missingAntiforgeryResponse = await client.SendAsync(missingAntiforgery);
        Assert.Equal(HttpStatusCode.BadRequest, missingAntiforgeryResponse.StatusCode);
        Assert.True(missingAntiforgeryResponse.Headers.CacheControl?.NoStore == true);

        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);
        using var optionsResponse = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/passkeys/login/options",
            antiforgeryToken));

        Assert.Equal(HttpStatusCode.OK, optionsResponse.StatusCode);
        Assert.True(optionsResponse.Headers.CacheControl?.NoStore == true);
        using var optionsDocument = JsonDocument.Parse(await optionsResponse.Content.ReadAsStreamAsync());
        Assert.False(string.IsNullOrWhiteSpace(optionsDocument.RootElement.GetProperty("challenge").GetString()));
        Assert.Equal("localhost", optionsDocument.RootElement.GetProperty("rpId").GetString());
        Assert.Equal("required", optionsDocument.RootElement.GetProperty("userVerification").GetString());
        if (optionsDocument.RootElement.TryGetProperty("allowCredentials", out var allowCredentials))
        {
            Assert.True(allowCredentials.ValueKind is JsonValueKind.Null or JsonValueKind.Array);
            if (allowCredentials.ValueKind == JsonValueKind.Array)
            {
                Assert.Empty(allowCredentials.EnumerateArray());
            }
        }

        using var invalidAssertion = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/passkeys/login/verify",
            await GetAntiforgeryTokenAsync(client),
            new { credentialJson = "{}" }));

        Assert.Equal(HttpStatusCode.Unauthorized, invalidAssertion.StatusCode);
        Assert.True(invalidAssertion.Headers.CacheControl?.NoStore == true);
        using var invalidDocument = JsonDocument.Parse(await invalidAssertion.Content.ReadAsStreamAsync());
        Assert.Equal("Invalid passkey.", invalidDocument.RootElement.GetProperty("title").GetString());
        Assert.DoesNotContain("owner@example.com", invalidDocument.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);

        using var session = await client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
    }

    [Fact]
    public async Task PasskeyLogin_IsRateLimitedPerClientIp()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var invalidAssertion = await client.SendAsync(CreateBrowserRequest(
                HttpMethod.Post,
                "/api/auth/passkeys/login/verify",
                antiforgeryToken,
                new { credentialJson = "{}" }));

            Assert.Equal(HttpStatusCode.Unauthorized, invalidAssertion.StatusCode);
        }

        using var rateLimited = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/passkeys/login/verify",
            antiforgeryToken,
            new { credentialJson = "{}" }));

        Assert.Equal(HttpStatusCode.TooManyRequests, rateLimited.StatusCode);
        Assert.True(rateLimited.Headers.CacheControl?.NoStore == true);
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

    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        await dbContext.Database.MigrateAsync();
    }
}
