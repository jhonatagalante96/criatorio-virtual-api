using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Infrastructure.Identity;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.WebUtilities;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Identity;

public sealed class PostgreSqlAccountPasskeyTests
{
    [Fact]
    public async Task PasskeyManagement_RequiresSessionAndAntiforgery_ProtectsMetadataAndIsolatesUsers()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);

        using var anonymousClient = CreateClient(factory);
        using var anonymousList = await anonymousClient.GetAsync("/api/auth/passkeys");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousList.StatusCode);

        using var ownerClient = CreateClient(factory);
        await RegisterConfirmAndLoginAsync(factory, ownerClient, "owner@example.com");

        var ownerAntiforgeryToken = await GetAntiforgeryTokenAsync(ownerClient);
        using var missingAntiforgery = new HttpRequestMessage(HttpMethod.Post, "/api/auth/passkeys/register/options");
        missingAntiforgery.Headers.Add("Origin", "http://localhost:3000");
        using var missingAntiforgeryResponse = await ownerClient.SendAsync(missingAntiforgery);
        Assert.Equal(HttpStatusCode.BadRequest, missingAntiforgeryResponse.StatusCode);

        using var optionsRequest = CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/passkeys/register/options",
            ownerAntiforgeryToken);
        using var optionsResponse = await ownerClient.SendAsync(optionsRequest);
        Assert.Equal(HttpStatusCode.OK, optionsResponse.StatusCode);
        Assert.True(optionsResponse.Headers.CacheControl?.NoStore == true);
        using var optionsDocument = JsonDocument.Parse(await optionsResponse.Content.ReadAsStreamAsync());
        Assert.False(string.IsNullOrWhiteSpace(optionsDocument.RootElement.GetProperty("challenge").GetString()));
        Assert.Equal("localhost", optionsDocument.RootElement.GetProperty("rp").GetProperty("id").GetString());
        Assert.Equal("required", optionsDocument.RootElement.GetProperty("authenticatorSelection").GetProperty("userVerification").GetString());

        using var invalidRegistration = CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/passkeys/register/verify",
            await GetAntiforgeryTokenAsync(ownerClient),
            new { credentialJson = "{}" });
        using var invalidRegistrationResponse = await ownerClient.SendAsync(invalidRegistration);
        Assert.Equal(HttpStatusCode.BadRequest, invalidRegistrationResponse.StatusCode);
        Assert.True(invalidRegistrationResponse.Headers.CacheControl?.NoStore == true);

        var firstCredentialId = new byte[] { 1, 2, 3 };
        var secondCredentialId = new byte[] { 4, 5, 6 };
        await SeedPasskeyAsync(factory, "OWNER@EXAMPLE.COM", firstCredentialId, "Laptop");
        await SeedPasskeyAsync(factory, "OWNER@EXAMPLE.COM", secondCredentialId, "Phone");

        var firstCredentialIdText = WebEncoders.Base64UrlEncode(firstCredentialId);
        var secondCredentialIdText = WebEncoders.Base64UrlEncode(secondCredentialId);

        using var listResponse = await ownerClient.GetAsync("/api/auth/passkeys");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.True(listResponse.Headers.CacheControl?.NoStore == true);
        using var listDocument = JsonDocument.Parse(await listResponse.Content.ReadAsStreamAsync());
        var passkeys = listDocument.RootElement.GetProperty("passkeys");
        Assert.Equal(2, passkeys.GetArrayLength());
        Assert.Contains(passkeys.EnumerateArray(), passkey =>
            passkey.GetProperty("credentialId").GetString() == firstCredentialIdText &&
            passkey.GetProperty("name").GetString() == "Laptop");
        Assert.DoesNotContain("publicKey", listDocument.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attestationObject", listDocument.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clientDataJson", listDocument.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);

        using var renameResponse = await ownerClient.SendAsync(CreateBrowserRequest(
            HttpMethod.Patch,
            $"/api/auth/passkeys/{firstCredentialIdText}",
            await GetAntiforgeryTokenAsync(ownerClient),
            new { name = "Personal laptop" }));
        Assert.Equal(HttpStatusCode.NoContent, renameResponse.StatusCode);
        Assert.True(renameResponse.Headers.CacheControl?.NoStore == true);

        using var secondClient = CreateClient(factory);
        await RegisterConfirmAndLoginAsync(factory, secondClient, "second@example.com");

        using var crossUserRename = await secondClient.SendAsync(CreateBrowserRequest(
            HttpMethod.Patch,
            $"/api/auth/passkeys/{firstCredentialIdText}",
            await GetAntiforgeryTokenAsync(secondClient),
            new { name = "Should not be visible" }));
        Assert.Equal(HttpStatusCode.NotFound, crossUserRename.StatusCode);

        using var crossUserRemoval = await secondClient.SendAsync(CreateBrowserRequest(
            HttpMethod.Delete,
            $"/api/auth/passkeys/{firstCredentialIdText}",
            await GetAntiforgeryTokenAsync(secondClient)));
        Assert.Equal(HttpStatusCode.NotFound, crossUserRemoval.StatusCode);

        using var secondUserList = await secondClient.GetAsync("/api/auth/passkeys");
        Assert.Equal(HttpStatusCode.OK, secondUserList.StatusCode);
        using var secondUserListDocument = JsonDocument.Parse(await secondUserList.Content.ReadAsStreamAsync());
        Assert.Empty(secondUserListDocument.RootElement.GetProperty("passkeys").EnumerateArray());

        using var malformedRemoval = await ownerClient.SendAsync(CreateBrowserRequest(
            HttpMethod.Delete,
            "/api/auth/passkeys/not-base64url",
            await GetAntiforgeryTokenAsync(ownerClient)));
        Assert.Equal(HttpStatusCode.BadRequest, malformedRemoval.StatusCode);

        using var removalResponse = await ownerClient.SendAsync(CreateBrowserRequest(
            HttpMethod.Delete,
            $"/api/auth/passkeys/{firstCredentialIdText}",
            await GetAntiforgeryTokenAsync(ownerClient)));
        Assert.Equal(HttpStatusCode.NoContent, removalResponse.StatusCode);

        using var finalList = await ownerClient.GetAsync("/api/auth/passkeys");
        Assert.Equal(HttpStatusCode.OK, finalList.StatusCode);
        using var finalListDocument = JsonDocument.Parse(await finalList.Content.ReadAsStreamAsync());
        var remainingPasskeys = finalListDocument.RootElement.GetProperty("passkeys").EnumerateArray().ToArray();
        Assert.Single(remainingPasskeys);
        Assert.Equal(secondCredentialIdText, remainingPasskeys[0].GetProperty("credentialId").GetString());
        Assert.Equal("Phone", remainingPasskeys[0].GetProperty("name").GetString());
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

    private static async Task RegisterConfirmAndLoginAsync(
        WebApplicationFactory<Program> factory,
        HttpClient client,
        string email)
    {
        const string password = "StrongPassword!123";
        using var registration = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/register",
            await GetAntiforgeryTokenAsync(client),
            new { email, password, confirmPassword = password }));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        await ConfirmUserAsync(factory, email);

        using var login = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/login",
            await GetAntiforgeryTokenAsync(client),
            new { email, password }));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    private static async Task ConfirmUserAsync(
        WebApplicationFactory<Program> factory,
        string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var user = await dbContext.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant());
        user.EmailConfirmed = true;
        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedPasskeyAsync(
        WebApplicationFactory<Program> factory,
        string normalizedEmail,
        byte[] credentialId,
        string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(normalizedEmail);
        Assert.NotNull(user);

        var passkey = new UserPasskeyInfo(
            credentialId,
            [9, 8, 7],
            DateTimeOffset.UtcNow,
            0,
            ["internal"],
            true,
            true,
            false,
            [1, 2, 3],
            [4, 5, 6])
        {
            Name = name
        };

        var result = await userManager.AddOrUpdatePasskeyAsync(user!, passkey);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
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

    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        await dbContext.Database.MigrateAsync();
    }
}
