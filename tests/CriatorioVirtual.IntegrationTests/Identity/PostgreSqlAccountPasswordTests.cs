using System.Net;
using System.Net.Http.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Infrastructure.Identity;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Identity;

public sealed class PostgreSqlAccountPasswordTests
{
    [Fact]
    public async Task PasswordRecovery_IsGeneric_ResetsPasswordAndRejectsTamperingAndReuse()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        var inbox = factory.Services.GetRequiredService<IAuthenticationEmailInbox>();
        using var client = CreateClient(factory);

        await RegisterAndConfirmAsync(factory, client, "reset@example.com", "InitialStrongPassword!123");

        var beforeUnknownRequest = inbox.Messages.Count;
        using var unknownRequest = await client.SendAsync(CreateForgotPasswordRequest(
            "unknown@example.com",
            await GetAntiforgeryTokenAsync(client)));
        Assert.Equal(HttpStatusCode.NoContent, unknownRequest.StatusCode);
        Assert.Equal(beforeUnknownRequest, inbox.Messages.Count);

        using var request = await client.SendAsync(CreateForgotPasswordRequest(
            "reset@example.com",
            await GetAntiforgeryTokenAsync(client)));
        Assert.Equal(HttpStatusCode.NoContent, request.StatusCode);

        var resetMessage = inbox.Messages.Last();
        var resetToken = ParseActionToken(resetMessage.ActionUrl, out var userId);

        using var tamperedReset = await client.SendAsync(CreateResetPasswordRequest(
            userId,
            resetToken + "tampered",
            "ResetStrongPassword!123",
            "ResetStrongPassword!123",
            await GetAntiforgeryTokenAsync(client)));
        Assert.Equal(HttpStatusCode.BadRequest, tamperedReset.StatusCode);

        using var reset = await client.SendAsync(CreateResetPasswordRequest(
            userId,
            resetToken,
            "ResetStrongPassword!123",
            "ResetStrongPassword!123",
            await GetAntiforgeryTokenAsync(client)));
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        using var reusedReset = await client.SendAsync(CreateResetPasswordRequest(
            userId,
            resetToken,
            "AnotherStrongPassword!123",
            "AnotherStrongPassword!123",
            await GetAntiforgeryTokenAsync(client)));
        Assert.Equal(HttpStatusCode.BadRequest, reusedReset.StatusCode);

        using var oldPasswordLogin = await client.SendAsync(CreateLoginRequest(
            "reset@example.com",
            "InitialStrongPassword!123",
            await GetAntiforgeryTokenAsync(client)));
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);

        using var newPasswordLogin = await client.SendAsync(CreateLoginRequest(
            "reset@example.com",
            "ResetStrongPassword!123",
            await GetAntiforgeryTokenAsync(client)));
        Assert.Equal(HttpStatusCode.NoContent, newPasswordLogin.StatusCode);

        using var secondRequest = await client.SendAsync(CreateForgotPasswordRequest(
            "reset@example.com",
            await GetAntiforgeryTokenAsync(client)));
        Assert.Equal(HttpStatusCode.NoContent, secondRequest.StatusCode);
        var secondMessage = inbox.Messages.Last();
        var secondToken = ParseActionToken(secondMessage.ActionUrl, out _);
        factory.Services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan =
            TimeSpan.FromMilliseconds(1);
        await Task.Delay(50);

        using var expiredReset = await client.SendAsync(CreateResetPasswordRequest(
            userId,
            secondToken,
            "ExpiredStrongPassword!123",
            "ExpiredStrongPassword!123",
            await GetAntiforgeryTokenAsync(client)));
        Assert.Equal(HttpStatusCode.BadRequest, expiredReset.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_RequiresCurrentPasswordAndUsesNewPasswordOnNextLogin()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        await RegisterAndConfirmAsync(factory, client, "change@example.com", "InitialStrongPassword!123");

        using var login = await client.SendAsync(CreateLoginRequest(
            "change@example.com",
            "InitialStrongPassword!123",
            await GetAntiforgeryTokenAsync(client)));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        using var wrongCurrent = await client.SendAsync(CreateChangePasswordRequest(
            "WrongCurrentPassword!123",
            "ChangedStrongPassword!123",
            "ChangedStrongPassword!123",
            await GetAntiforgeryTokenAsync(client)));
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);

        using var mismatch = await client.SendAsync(CreateChangePasswordRequest(
            "InitialStrongPassword!123",
            "ChangedStrongPassword!123",
            "DifferentStrongPassword!123",
            await GetAntiforgeryTokenAsync(client)));
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);

        using var change = await client.SendAsync(CreateChangePasswordRequest(
            "InitialStrongPassword!123",
            "ChangedStrongPassword!123",
            "ChangedStrongPassword!123",
            await GetAntiforgeryTokenAsync(client)));
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        using var oldPasswordClient = CreateClient(factory);
        using var oldPasswordLogin = await oldPasswordClient.SendAsync(CreateLoginRequest(
            "change@example.com",
            "InitialStrongPassword!123",
            await GetAntiforgeryTokenAsync(oldPasswordClient)));
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);

        using var newPasswordClient = CreateClient(factory);
        using var newPasswordLogin = await newPasswordClient.SendAsync(CreateLoginRequest(
            "change@example.com",
            "ChangedStrongPassword!123",
            await GetAntiforgeryTokenAsync(newPasswordClient)));
        Assert.Equal(HttpStatusCode.NoContent, newPasswordLogin.StatusCode);
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

    private static HttpRequestMessage CreateForgotPasswordRequest(string email, string antiforgeryToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/forgot-password")
        {
            Content = JsonContent.Create(new { email })
        };
        AddBrowserHeaders(request, antiforgeryToken);
        return request;
    }

    private static HttpRequestMessage CreateResetPasswordRequest(
        string userId,
        string token,
        string newPassword,
        string confirmPassword,
        string antiforgeryToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reset-password")
        {
            Content = JsonContent.Create(new { userId, token, newPassword, confirmPassword })
        };
        AddBrowserHeaders(request, antiforgeryToken);
        return request;
    }

    private static HttpRequestMessage CreateChangePasswordRequest(
        string currentPassword,
        string newPassword,
        string confirmPassword,
        string antiforgeryToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword, newPassword, confirmPassword })
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
}
