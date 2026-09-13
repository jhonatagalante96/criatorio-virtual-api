using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Infrastructure.Identity;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
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
        using var client = CreateClient(factory, "198.51.100.3");

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
        using var client = CreateClient(factory, "198.51.100.4");
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

    [Fact]
    public async Task PasskeyLogin_UsesNativeIdentityToCreateSessionRejectReplayAndRespectRemoval()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);

        const string email = "passkey-owner@example.com";
        var userId = await CreateConfirmedUserAsync(factory, email);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var credentialId = RandomNumberGenerator.GetBytes(32);
        await SeedCryptographicPasskeyAsync(factory, userId, credentialId, key);

        using var client = CreateClient(factory, "198.51.100.1");
        var optionsResponse = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/passkeys/login/options",
            await GetAntiforgeryTokenAsync(client)));

        Assert.Equal(HttpStatusCode.OK, optionsResponse.StatusCode);
        using var optionsDocument = JsonDocument.Parse(await optionsResponse.Content.ReadAsStreamAsync());
        var challenge = optionsDocument.RootElement.GetProperty("challenge").GetString();
        Assert.False(string.IsNullOrWhiteSpace(challenge));

        using var wrongOrigin = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/passkeys/login/verify",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                credentialJson = CreateAssertion(
                    challenge!,
                    credentialId,
                    userId,
                    key,
                    origin: "https://evil.example.com")
            }));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongOrigin.StatusCode);

        var wrongRpIdOptions = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/passkeys/login/options",
            await GetAntiforgeryTokenAsync(client)));
        Assert.Equal(HttpStatusCode.OK, wrongRpIdOptions.StatusCode);
        using var wrongRpIdOptionsDocument = JsonDocument.Parse(await wrongRpIdOptions.Content.ReadAsStreamAsync());
        var wrongRpIdChallenge = wrongRpIdOptionsDocument.RootElement.GetProperty("challenge").GetString();
        Assert.False(string.IsNullOrWhiteSpace(wrongRpIdChallenge));

        using var wrongRpId = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/passkeys/login/verify",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                credentialJson = CreateAssertion(
                    wrongRpIdChallenge!,
                    credentialId,
                    userId,
                    key,
                    rpId: "evil.example.com")
            }));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongRpId.StatusCode);

        var validOptions = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/passkeys/login/options",
            await GetAntiforgeryTokenAsync(client)));
        Assert.Equal(HttpStatusCode.OK, validOptions.StatusCode);
        using var validOptionsDocument = JsonDocument.Parse(await validOptions.Content.ReadAsStreamAsync());
        var validChallenge = validOptionsDocument.RootElement.GetProperty("challenge").GetString();
        Assert.False(string.IsNullOrWhiteSpace(validChallenge));

        var assertionJson = CreateAssertion(validChallenge!, credentialId, userId, key);
        using var login = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/passkeys/login/verify",
            await GetAntiforgeryTokenAsync(client),
            new { credentialJson = assertionJson }));

        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        Assert.True(login.Headers.CacheControl?.NoStore == true);

        using var session = await client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        using var sessionDocument = JsonDocument.Parse(await session.Content.ReadAsStreamAsync());
        Assert.Equal(email, sessionDocument.RootElement.GetProperty("email").GetString());

        using var logout = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/logout",
            await GetAntiforgeryTokenAsync(client)));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var sessionAfterLogout = await client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.Unauthorized, sessionAfterLogout.StatusCode);

        using var replay = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/passkeys/login/verify",
            await GetAntiforgeryTokenAsync(client),
            new { credentialJson = assertionJson }));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.True(replay.Headers.CacheControl?.NoStore == true);

        await RemovePasskeyAsync(factory, userId, credentialId);
        using var removedOptions = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/passkeys/login/options",
            await GetAntiforgeryTokenAsync(client)));
        Assert.Equal(HttpStatusCode.OK, removedOptions.StatusCode);
        using var removedOptionsDocument = JsonDocument.Parse(await removedOptions.Content.ReadAsStreamAsync());
        var removedChallenge = removedOptionsDocument.RootElement.GetProperty("challenge").GetString();
        Assert.False(string.IsNullOrWhiteSpace(removedChallenge));

        using var removedCredential = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/passkeys/login/verify",
            await GetAntiforgeryTokenAsync(client),
            new { credentialJson = CreateAssertion(removedChallenge!, credentialId, userId, key) }));
        Assert.Equal(HttpStatusCode.Unauthorized, removedCredential.StatusCode);
        using var sessionAfterRemoval = await client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.Unauthorized, sessionAfterRemoval.StatusCode);
    }

    [Fact]
    public async Task PasskeyLogin_RejectsMissingUserVerification()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);

        var userId = await CreateConfirmedUserAsync(factory, "passkey-uv@example.com");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var credentialId = RandomNumberGenerator.GetBytes(32);
        await SeedCryptographicPasskeyAsync(factory, userId, credentialId, key);

        using var client = CreateClient(factory, "198.51.100.5");
        using var optionsResponse = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/passkeys/login/options",
            await GetAntiforgeryTokenAsync(client)));
        Assert.Equal(HttpStatusCode.OK, optionsResponse.StatusCode);
        using var optionsDocument = JsonDocument.Parse(await optionsResponse.Content.ReadAsStreamAsync());
        var challenge = optionsDocument.RootElement.GetProperty("challenge").GetString();
        Assert.False(string.IsNullOrWhiteSpace(challenge));

        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/passkeys/login/verify",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                credentialJson = CreateAssertion(
                    challenge!,
                    credentialId,
                    userId,
                    key,
                    flags: 0x09)
            }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
    }

    [Fact]
    public async Task PasskeyLogin_RejectsTamperedChallenge()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);

        var userId = await CreateConfirmedUserAsync(factory, "passkey-tampered@example.com");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var credentialId = RandomNumberGenerator.GetBytes(32);
        await SeedCryptographicPasskeyAsync(factory, userId, credentialId, key);

        using var client = CreateClient(factory, "198.51.100.6");
        using var optionsResponse = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/passkeys/login/options",
            await GetAntiforgeryTokenAsync(client)));
        Assert.Equal(HttpStatusCode.OK, optionsResponse.StatusCode);
        using var optionsDocument = JsonDocument.Parse(await optionsResponse.Content.ReadAsStreamAsync());
        var challenge = optionsDocument.RootElement.GetProperty("challenge").GetString();
        Assert.False(string.IsNullOrWhiteSpace(challenge));
        var tamperedChallenge = (challenge![0] == 'A' ? 'B' : 'A') + challenge[1..];

        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/passkeys/login/verify",
            await GetAntiforgeryTokenAsync(client),
            new { credentialJson = CreateAssertion(tamperedChallenge, credentialId, userId, key) }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
    }

    [Fact]
    public async Task PasskeyLogin_RejectsExpiredChallenge()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(
            database.GetConnectionString(),
            certificate,
            passkeyChallengeLifetime: TimeSpan.FromMilliseconds(100));
        await MigrateAsync(factory);

        var userId = await CreateConfirmedUserAsync(factory, "passkey-expired@example.com");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var credentialId = RandomNumberGenerator.GetBytes(32);
        await SeedCryptographicPasskeyAsync(factory, userId, credentialId, key);

        using var client = CreateClient(factory, "198.51.100.7");
        using var optionsResponse = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/passkeys/login/options",
            await GetAntiforgeryTokenAsync(client)));
        Assert.Equal(HttpStatusCode.OK, optionsResponse.StatusCode);
        using var optionsDocument = JsonDocument.Parse(await optionsResponse.Content.ReadAsStreamAsync());
        var challenge = optionsDocument.RootElement.GetProperty("challenge").GetString();
        Assert.False(string.IsNullOrWhiteSpace(challenge));

        await Task.Delay(250);
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/passkeys/login/verify",
            await GetAntiforgeryTokenAsync(client),
            new { credentialJson = CreateAssertion(challenge!, credentialId, userId, key) }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
    }

    [Fact]
    public async Task PasskeyLogin_RejectsMalformedAndOversizedPayloadsWithoutServerErrorsOrEchoes()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory, "198.51.100.2");
        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);

        foreach (var credentialJson in new[] { "null", "[]", "\"not-an-object\"", "{\"response\":null}" })
        {
            using var response = await client.SendAsync(CreateBrowserRequest(
                HttpMethod.Post,
                "/api/auth/passkeys/login/verify",
                antiforgeryToken,
                new { credentialJson }));

            Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized });
            Assert.True(response.Headers.CacheControl?.NoStore == true);
            var responseBody = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(credentialJson, responseBody, StringComparison.Ordinal);
            Assert.DoesNotContain("StackTrace", responseBody, StringComparison.OrdinalIgnoreCase);
        }

        var oversizedCredential = new string('x', 129 * 1024);
        using var oversizedResponse = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/passkeys/login/verify",
            antiforgeryToken,
            new { credentialJson = oversizedCredential }));

        Assert.NotEqual(HttpStatusCode.InternalServerError, oversizedResponse.StatusCode);
        Assert.Contains(oversizedResponse.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized, HttpStatusCode.RequestEntityTooLarge });
        Assert.True(oversizedResponse.Headers.CacheControl?.NoStore == true);
        var oversizedBody = await oversizedResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(oversizedCredential, oversizedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", oversizedBody, StringComparison.OrdinalIgnoreCase);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate,
        TimeSpan? passkeyChallengeLifetime = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:EventLog:LogLevel:Default"] = "None"
            }));
            builder.ConfigureServices(services =>
            {
                services.AddInfrastructurePersistence(connectionString, certificate);
                services.AddTransient<IStartupFilter, TestRemoteIpStartupFilter>();
                if (passkeyChallengeLifetime is { } lifetime)
                {
                    services.Configure<CookieAuthenticationOptions>(
                        IdentityConstants.TwoFactorUserIdScheme,
                        options => options.ExpireTimeSpan = lifetime);
                }
            });
        });

    private static HttpClient CreateClient(
        WebApplicationFactory<Program> factory,
        string forwardedFor)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Test-Remote-Ip", forwardedFor);
        return client;
    }

    private sealed class TestRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (IPAddress.TryParse(context.Request.Headers["X-Test-Remote-Ip"].FirstOrDefault(), out var remoteIp))
                {
                    context.Connection.RemoteIpAddress = remoteIp;
                }

                await nextMiddleware();
            });

            next(app);
        };
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

    private static async Task<Guid> CreateConfirmedUserAsync(
        WebApplicationFactory<Program> factory,
        string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };
        var result = await userManager.CreateAsync(user, "StrongPassword!123");
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        return user.Id;
    }

    private static async Task SeedCryptographicPasskeyAsync(
        WebApplicationFactory<Program> factory,
        Guid userId,
        byte[] credentialId,
        ECDsa key)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString("D"));
        Assert.NotNull(user);

        var result = await userManager.AddOrUpdatePasskeyAsync(
            user!,
            new UserPasskeyInfo(
                credentialId,
                CreateCosePublicKey(key),
                DateTimeOffset.UtcNow,
                0,
                ["internal"],
                true,
                true,
                false,
                [],
                []));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
    }

    private static async Task RemovePasskeyAsync(
        WebApplicationFactory<Program> factory,
        Guid userId,
        byte[] credentialId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString("D"));
        Assert.NotNull(user);
        var result = await userManager.RemovePasskeyAsync(user!, credentialId);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
    }

    private static byte[] CreateCosePublicKey(ECDsa key)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        var writer = new CborWriter();
        writer.WriteStartMap(5);
        writer.WriteInt32(1);
        writer.WriteInt32(2);
        writer.WriteInt32(3);
        writer.WriteInt32(-7);
        writer.WriteInt32(-1);
        writer.WriteInt32(1);
        writer.WriteInt32(-2);
        writer.WriteByteString(parameters.Q.X!);
        writer.WriteInt32(-3);
        writer.WriteByteString(parameters.Q.Y!);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static string CreateAssertion(
        string challenge,
        byte[] credentialId,
        Guid userId,
        ECDsa key,
        string origin = "http://localhost:3000",
        string rpId = "localhost",
        byte flags = 0x0d)
    {
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "webauthn.get",
            challenge,
            origin,
            crossOrigin = false
        });
        var authenticatorData = new byte[37];
        SHA256.HashData(Encoding.UTF8.GetBytes(rpId), authenticatorData.AsSpan(0, 32));
        authenticatorData[32] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(authenticatorData.AsSpan(33), 0);
        var clientDataHash = SHA256.HashData(clientDataJson);
        var signedData = new byte[authenticatorData.Length + clientDataHash.Length];
        authenticatorData.CopyTo(signedData, 0);
        clientDataHash.CopyTo(signedData, authenticatorData.Length);

        return JsonSerializer.Serialize(new
        {
            id = WebEncoders.Base64UrlEncode(credentialId),
            rawId = WebEncoders.Base64UrlEncode(credentialId),
            response = new
            {
                clientDataJSON = WebEncoders.Base64UrlEncode(clientDataJson),
                authenticatorData = WebEncoders.Base64UrlEncode(authenticatorData),
                signature = WebEncoders.Base64UrlEncode(key.SignData(
                    signedData,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.Rfc3279DerSequence)),
                userHandle = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(userId.ToString("D"))),
                clientExtensionResults = new { }
            },
            clientExtensionResults = new { },
            type = "public-key"
        });
    }
}
