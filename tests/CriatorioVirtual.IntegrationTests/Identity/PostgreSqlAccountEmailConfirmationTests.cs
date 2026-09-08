using System.Net;
using System.Net.Http.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Infrastructure.Identity;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Identity;

public sealed class PostgreSqlAccountEmailConfirmationTests
{
    [Fact]
    public async Task Confirmation_RejectsTampering_IsIdempotent_AndResendIsRateLimited()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        var inbox = factory.Services.GetRequiredService<IAuthenticationEmailInbox>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);

        using var registration = await client.SendAsync(CreateRegistrationRequest(
            "owner@example.com",
            "StrongPassword!123",
            antiforgeryToken));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        var initialMessage = Assert.Single(inbox.Messages);
        var initialToken = ParseActionToken(initialMessage.ActionUrl, out var userId);

        using var confirmation = await client.SendAsync(CreateConfirmationRequest(
            userId,
            initialToken,
            antiforgeryToken));
        Assert.Equal(HttpStatusCode.NoContent, confirmation.StatusCode);
        Assert.True(await IsEmailConfirmedAsync(factory, "OWNER@EXAMPLE.COM"));

        using var repeatedConfirmation = await client.SendAsync(CreateConfirmationRequest(
            userId,
            initialToken,
            antiforgeryToken));
        Assert.Equal(HttpStatusCode.NoContent, repeatedConfirmation.StatusCode);

        using var tamperedConfirmation = await client.SendAsync(CreateConfirmationRequest(
            userId,
            initialToken + "tampered",
            antiforgeryToken));
        var tamperedBody = await tamperedConfirmation.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, tamperedConfirmation.StatusCode);
        Assert.DoesNotContain(initialToken, tamperedBody, StringComparison.Ordinal);

        using var unknownConfirmation = await client.SendAsync(CreateConfirmationRequest(
            Guid.NewGuid().ToString("D"),
            initialToken,
            antiforgeryToken));
        Assert.Equal(HttpStatusCode.BadRequest, unknownConfirmation.StatusCode);

        using var resendRegistration = await client.SendAsync(CreateRegistrationRequest(
            "resend@example.com",
            "StrongPassword!123",
            antiforgeryToken));
        Assert.Equal(HttpStatusCode.Created, resendRegistration.StatusCode);
        var messageCountBeforeResend = inbox.Messages.Count;

        using var resend = await client.SendAsync(CreateResendRequest("resend@example.com", antiforgeryToken));
        Assert.Equal(HttpStatusCode.NoContent, resend.StatusCode);
        Assert.Equal(messageCountBeforeResend + 1, inbox.Messages.Count);

        using var limitedResend = await client.SendAsync(CreateResendRequest("resend@example.com", antiforgeryToken));
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedResend.StatusCode);

        using var unknownResend = await client.SendAsync(CreateResendRequest("unknown@example.com", antiforgeryToken));
        Assert.Equal(HttpStatusCode.NoContent, unknownResend.StatusCode);
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

    private static HttpRequestMessage CreateConfirmationRequest(
        string userId,
        string token,
        string antiforgeryToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/confirm-email")
        {
            Content = JsonContent.Create(new { userId, token })
        };
        AddBrowserHeaders(request, antiforgeryToken);
        return request;
    }

    private static HttpRequestMessage CreateResendRequest(string email, string antiforgeryToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/confirm-email/resend")
        {
            Content = JsonContent.Create(new { email })
        };
        AddBrowserHeaders(request, antiforgeryToken);
        return request;
    }

    private static void AddBrowserHeaders(HttpRequestMessage request, string antiforgeryToken)
    {
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName, antiforgeryToken);
    }

    private static string ParseActionToken(Uri actionUrl, out string userId)
    {
        var query = QueryHelpers.ParseQuery(actionUrl.Query);
        userId = query["userId"].ToString();
        var token = query["token"].ToString();
        Assert.False(string.IsNullOrWhiteSpace(userId));
        Assert.False(string.IsNullOrWhiteSpace(token));
        return token;
    }

    private static async Task<bool> IsEmailConfirmedAsync(
        WebApplicationFactory<Program> factory,
        string normalizedEmail)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        return await dbContext.Users
            .Where(user => user.NormalizedEmail == normalizedEmail)
            .Select(user => user.EmailConfirmed)
            .SingleAsync();
    }
}
