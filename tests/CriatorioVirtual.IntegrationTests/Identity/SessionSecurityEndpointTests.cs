using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Application.Identity;
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

public sealed class SessionSecurityEndpointTests
{
    private const string ApplicationOrigin = "https://app.example.com";
    private const string ApiOrigin = "https://api.example.com";
    private const string AuthCookieName = "__Host-CriatorioVirtual-Auth";

    [Fact]
    public async Task PasswordLogin_UsesConfiguredDomainAndSecureCookie()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateDeploymentFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        await RegisterAndConfirmAsync(factory, client, "domain-owner@example.com", "StrongPassword!123");

        using var missingAntiforgery = CreateLoginRequest(
            "domain-owner@example.com",
            "StrongPassword!123",
            antiforgeryToken: null);
        using var missingAntiforgeryResponse = await client.SendAsync(missingAntiforgery);
        Assert.Equal(HttpStatusCode.BadRequest, missingAntiforgeryResponse.StatusCode);

        using var unauthenticatedSession = await SendOriginRequestAsync(client, HttpMethod.Get, "/api/auth/session");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedSession.StatusCode);

        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);
        using var login = await client.SendAsync(CreateLoginRequest(
            "domain-owner@example.com",
            "StrongPassword!123",
            antiforgeryToken));

        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        Assert.Equal(ApplicationOrigin, login.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", login.Headers.GetValues("Access-Control-Allow-Credentials").Single());

        var authCookie = GetCookie(login, AuthCookieName);
        Assert.Contains("secure", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=none", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", authCookie, StringComparison.OrdinalIgnoreCase);

        using var session = await SendOriginRequestAsync(client, HttpMethod.Get, "/api/auth/session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        using var sessionDocument = JsonDocument.Parse(await session.Content.ReadAsStreamAsync());
        Assert.Equal("domain-owner@example.com", sessionDocument.RootElement.GetProperty("email").GetString());
    }

    [Fact]
    public async Task GoogleLoginStart_UsesTheConfiguredHttpsApiDomain()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Security:Google:ClientId", "test-client-id");
            builder.UseSetting("Security:Google:ClientSecret", "test-client-secret");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:AllowedOrigins:0"] = ApplicationOrigin,
                ["Security:Google:ClientId"] = "test-client-id",
                ["Security:Google:ClientSecret"] = "test-client-secret",
                ["Logging:EventLog:LogLevel:Default"] = "None"
            }));
            builder.ConfigureServices(services => services.AddSingleton<IGoogleAccountAuthenticationService, StubGoogleAccountAuthenticationService>());
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(ApiOrigin),
            AllowAutoRedirect = false
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/google");
        request.Headers.Add("Origin", ApplicationOrigin);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location;
        Assert.NotNull(location);
        Assert.Contains(
            "redirect_uri=" + Uri.EscapeDataString(ApiOrigin + "/signin-google"),
            location.Query,
            StringComparison.Ordinal);
        Assert.Contains("state=", location.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionCookie_RemainsValidInASecondDeploymentWithThePersistedKeyRing()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var firstFactory = CreateDeploymentFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(firstFactory);
        using var firstClient = CreateClient(firstFactory);

        await RegisterAndConfirmAsync(firstFactory, firstClient, "surviving-session@example.com", "StrongPassword!123");
        var antiforgeryToken = await GetAntiforgeryTokenAsync(firstClient);
        using var login = await firstClient.SendAsync(CreateLoginRequest(
            "surviving-session@example.com",
            "StrongPassword!123",
            antiforgeryToken));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var authCookie = GetCookie(login, AuthCookieName).Split(';', 2)[0];

        using var secondFactory = CreateDeploymentFactory(database.GetConnectionString(), certificate);
        using var secondClient = CreateClient(secondFactory, handleCookies: false);
        using var sessionRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/session");
        sessionRequest.Headers.Add("Origin", ApplicationOrigin);
        sessionRequest.Headers.TryAddWithoutValidation("Cookie", authCookie);

        using var session = await secondClient.SendAsync(sessionRequest);

        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        using var sessionDocument = JsonDocument.Parse(await session.Content.ReadAsStreamAsync());
        Assert.Equal(
            "surviving-session@example.com",
            sessionDocument.RootElement.GetProperty("email").GetString());
    }

    private static WebApplicationFactory<Program> CreateDeploymentFactory(
        string connectionString,
        X509Certificate2 certificate) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:CriatorioVirtual", connectionString);
            builder.UseSetting(
                DataProtectionCertificateLoader.CertificateBase64ConfigurationKey,
                Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12, TestCertificate.Password)));
            builder.UseSetting(
                DataProtectionCertificateLoader.CertificatePasswordConfigurationKey,
                TestCertificate.Password);
            builder.UseSetting("Security:AllowedOrigins:0", ApplicationOrigin);
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CriatorioVirtual"] = connectionString,
                [DataProtectionCertificateLoader.CertificateBase64ConfigurationKey] = Convert.ToBase64String(
                    certificate.Export(X509ContentType.Pkcs12, TestCertificate.Password)),
                [DataProtectionCertificateLoader.CertificatePasswordConfigurationKey] = TestCertificate.Password,
                ["Security:AllowedOrigins:0"] = ApplicationOrigin,
                ["Logging:EventLog:LogLevel:Default"] = "None"
            }));
        });

    private static HttpClient CreateClient(
        WebApplicationFactory<Program> factory,
        bool handleCookies = true) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(ApiOrigin),
            HandleCookies = handleCookies,
            AllowAutoRedirect = false
        });

    private static async Task RegisterAndConfirmAsync(
        WebApplicationFactory<Program> factory,
        HttpClient client,
        string email,
        string password)
    {
        using var registration = await client.SendAsync(CreateRegistrationRequest(
            email,
            password,
            await GetAntiforgeryTokenAsync(client)));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var user = await dbContext.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant());
        user.EmailConfirmed = true;
        await dbContext.SaveChangesAsync();
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
        Assert.Equal(ApplicationOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        return response.Headers.GetValues(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName).Single();
    }

    private static async Task<HttpResponseMessage> SendOriginRequestAsync(
        HttpClient client,
        HttpMethod method,
        string path)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Origin", ApplicationOrigin);
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage CreateRegistrationRequest(
        string email,
        string password,
        string antiforgeryToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new { email, password, confirmPassword = password })
        };
        AddBrowserHeaders(request, antiforgeryToken);
        return request;
    }

    private static HttpRequestMessage CreateLoginRequest(
        string email,
        string password,
        string? antiforgeryToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password })
        };
        AddBrowserHeaders(request, antiforgeryToken);
        return request;
    }

    private static void AddBrowserHeaders(HttpRequestMessage request, string? antiforgeryToken)
    {
        request.Headers.Add("Origin", ApplicationOrigin);
        if (!string.IsNullOrWhiteSpace(antiforgeryToken))
        {
            request.Headers.Add(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName, antiforgeryToken);
        }
    }

    private static string GetCookie(HttpResponseMessage response, string name)
    {
        var cookie = response.Headers
            .GetValues("Set-Cookie")
            .SingleOrDefault(value => value.StartsWith(name + "=", StringComparison.Ordinal));
        Assert.NotNull(cookie);
        return cookie!;
    }

    private sealed class StubGoogleAccountAuthenticationService : IGoogleAccountAuthenticationService
    {
        public Task<GoogleAuthenticationResult> CompleteAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(GoogleAuthenticationResult.Invalid());
    }
}
