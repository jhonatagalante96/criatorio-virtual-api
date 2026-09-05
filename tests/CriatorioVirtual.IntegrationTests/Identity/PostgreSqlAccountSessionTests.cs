using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Api;
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

namespace CriatorioVirtual.IntegrationTests.Identity;

public sealed class PostgreSqlAccountSessionTests
{
    [Fact]
    public async Task LoginSessionAndLogout_UseTheIdentityCookieAndKeepInvalidCredentialsGeneric()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);

        using var unauthenticatedSession = await client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedSession.StatusCode);

        using var registration = await client.SendAsync(CreateRegistrationRequest(
            "owner@example.com",
            "StrongPassword!123",
            antiforgeryToken));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        await ConfirmEmailAsync(factory, "OWNER@EXAMPLE.COM");

        using var unknownCredentials = await client.SendAsync(CreateLoginRequest(
            "unknown@example.com",
            "WrongPassword!123",
            antiforgeryToken));
        using var wrongPassword = await client.SendAsync(CreateLoginRequest(
            "owner@example.com",
            "WrongPassword!123",
            antiforgeryToken));

        Assert.Equal(HttpStatusCode.Unauthorized, unknownCredentials.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        await AssertSameGenericProblemAsync(unknownCredentials, wrongPassword);

        using var login = await client.SendAsync(CreateLoginRequest(
            "owner@example.com",
            "StrongPassword!123",
            antiforgeryToken));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        using var session = await client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        using var sessionDocument = JsonDocument.Parse(await session.Content.ReadAsStreamAsync());
        Assert.Equal("owner@example.com", sessionDocument.RootElement.GetProperty("email").GetString());
        Assert.True(sessionDocument.RootElement.GetProperty("emailConfirmed").GetBoolean());
        Assert.DoesNotContain("PasswordHash", sessionDocument.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);

        using var logout = await client.SendAsync(CreateLogoutRequest(antiforgeryToken));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var sessionAfterLogout = await client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.Unauthorized, sessionAfterLogout.StatusCode);
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

    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    private static async Task ConfirmEmailAsync(
        WebApplicationFactory<Program> factory,
        string normalizedEmail)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var user = await dbContext.Users.SingleAsync(candidate => candidate.NormalizedEmail == normalizedEmail);
        user.EmailConfirmed = true;
        await dbContext.SaveChangesAsync();
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/antiforgery/token");
        request.Headers.Add("Origin", "http://localhost:3000");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return response.Headers.GetValues(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName).Single();
    }

    private static HttpRequestMessage CreateRegistrationRequest(
        string email,
        string password,
        string antiforgeryToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new { email, password })
        };
        AddBrowserHeaders(request, antiforgeryToken);
        return request;
    }

    private static HttpRequestMessage CreateLoginRequest(
        string email,
        string password,
        string antiforgeryToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password })
        };
        AddBrowserHeaders(request, antiforgeryToken);
        return request;
    }

    private static HttpRequestMessage CreateLogoutRequest(string antiforgeryToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        AddBrowserHeaders(request, antiforgeryToken);
        return request;
    }

    private static void AddBrowserHeaders(HttpRequestMessage request, string antiforgeryToken)
    {
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName, antiforgeryToken);
    }

    private static async Task AssertSameGenericProblemAsync(
        HttpResponseMessage first,
        HttpResponseMessage second)
    {
        using var firstDocument = JsonDocument.Parse(await first.Content.ReadAsStreamAsync());
        using var secondDocument = JsonDocument.Parse(await second.Content.ReadAsStreamAsync());

        foreach (var property in new[] { "title", "status", "type" })
        {
            Assert.Equal(
                firstDocument.RootElement.GetProperty(property).ToString(),
                secondDocument.RootElement.GetProperty(property).ToString());
        }

        Assert.Equal("Invalid email or password.", firstDocument.RootElement.GetProperty("title").GetString());
        Assert.DoesNotContain("WrongPassword!123", firstDocument.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("unknown@example.com", firstDocument.RootElement.GetRawText(), StringComparison.Ordinal);
    }
}
