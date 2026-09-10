using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Application.Identity;
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

public sealed class PostgreSqlAccountRegistrationTests
{
    [Fact]
    public async Task Register_PersistsValidIdentityAndRejectsInvalidOrConcurrentDuplicateRequests()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        var emailInbox = factory.Services.GetRequiredService<IAuthenticationEmailInbox>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);

        using var validResponse = await client.SendAsync(CreateRegistrationRequest(
            "owner@example.com",
            "StrongPassword!123",
            antiforgeryToken));

        Assert.Equal(HttpStatusCode.Created, validResponse.StatusCode);
        await AssertSingleUserAsync(factory, "OWNER@EXAMPLE.COM");
        using var unconfirmedDuplicateResponse = await client.SendAsync(CreateRegistrationRequest(
            "owner@example.com",
            "StrongPassword!123",
            antiforgeryToken));
        Assert.Equal(HttpStatusCode.Conflict, unconfirmedDuplicateResponse.StatusCode);
        await AssertProblemCodeAsync(unconfirmedDuplicateResponse, "email_confirmation_required");

        var confirmation = Assert.Single(emailInbox.Messages, message =>
            message.Kind == AuthenticationEmailKind.Confirmation &&
            message.Recipient == "owner@example.com");
        Assert.Equal("localhost", confirmation.ActionUrl.Host);
        Assert.DoesNotContain("production", confirmation.ActionUrl.AbsoluteUri, StringComparison.OrdinalIgnoreCase);

        using var invalidPasswordResponse = await client.SendAsync(CreateRegistrationRequest(
            "invalid-password@example.com",
            "short",
            antiforgeryToken));

        Assert.Equal(HttpStatusCode.BadRequest, invalidPasswordResponse.StatusCode);
        await AssertNoUserAsync(factory, "INVALID-PASSWORD@EXAMPLE.COM");

        var concurrentRequests = Enumerable.Range(0, 2)
            .Select(_ => CreateRegistrationRequest(
                "duplicate@example.com",
                "StrongPassword!123",
                antiforgeryToken))
            .ToArray();
        var concurrentResponses = await Task.WhenAll(concurrentRequests.Select(client.SendAsync));
        try
        {
            Assert.Equal(1, concurrentResponses.Count(response => response.StatusCode == HttpStatusCode.Created));
            Assert.Equal(1, concurrentResponses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        }
        finally
        {
            foreach (var response in concurrentResponses)
            {
                response.Dispose();
            }

            foreach (var request in concurrentRequests)
            {
                request.Dispose();
            }
        }

        await AssertSingleUserAsync(factory, "DUPLICATE@EXAMPLE.COM");
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

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/antiforgery/token");
        request.Headers.Add("Origin", "http://localhost:3000");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return response.Headers.GetValues(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName).Single();
    }

    private static HttpRequestMessage CreateRegistrationRequest(string email, string password, string antiforgeryToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new { email, password, confirmPassword = password })
        };
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName, antiforgeryToken);
        return request;
    }

    private static async Task AssertSingleUserAsync(WebApplicationFactory<Program> factory, string normalizedEmail)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var user = await dbContext.Users.SingleAsync(candidate => candidate.NormalizedEmail == normalizedEmail);

        Assert.False(user.EmailConfirmed);
        Assert.True(user.LockoutEnabled);
        Assert.False(string.IsNullOrWhiteSpace(user.PasswordHash));
        Assert.NotEqual("StrongPassword!123", user.PasswordHash);
    }

    private static async Task AssertNoUserAsync(WebApplicationFactory<Program> factory, string normalizedEmail)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();

        Assert.False(await dbContext.Users.AnyAsync(candidate => candidate.NormalizedEmail == normalizedEmail));
    }

    private static async Task AssertProblemCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedCode, problem.GetProperty("code").GetString());
    }
}
