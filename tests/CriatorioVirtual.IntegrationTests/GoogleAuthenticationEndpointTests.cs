using System.Net;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Application.Identity;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CriatorioVirtual.IntegrationTests;

public sealed class GoogleAuthenticationEndpointTests
{
    [Fact]
    public async Task TamperedGoogleCallback_ReturnsUnauthorizedWithoutRedirecting()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Security:Google:ClientId", "test-client-id");
            builder.UseSetting("Security:Google:ClientSecret", "test-client-secret");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Google:ClientId"] = "test-client-id",
                ["Security:Google:ClientSecret"] = "test-client-secret",
                ["Logging:EventLog:LogLevel:Default"] = "None"
            }));
            builder.ConfigureServices(services => services.AddScoped<IGoogleAccountAuthenticationService, StubGoogleAuthenticationService>());
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        using var start = await client.GetAsync("/api/auth/google");
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);

        using var response = await client.GetAsync("/signin-google?code=forged-code&state=forged-state");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Location"));
        using var problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(
            "Google sign-in could not be completed. Start the sign-in flow again.",
            problem.RootElement.GetProperty("detail").GetString());
        Assert.DoesNotContain("forged", problem.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GoogleCallback_ReturnsSafeFailureDetail()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:EventLog:LogLevel:Default"] = "None"
            }));
            builder.ConfigureServices(services =>
            {
                services.AddScoped<IGoogleAccountAuthenticationService, StubGoogleAuthenticationService>();
                services.AddControllers()
                    .AddApplicationPart(typeof(GoogleExternalCookieController).Assembly);
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        using var seed = await client.GetAsync(
            "/api/test/google/seed?providerKey=google-sub&email=unverified@example.com&verified=false");
        Assert.Equal(HttpStatusCode.NoContent, seed.StatusCode);

        using var response = await client.GetAsync("/api/auth/google/callback");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(
            "The Google account e-mail must be verified before it can be used to sign in.",
            problem.RootElement.GetProperty("detail").GetString());
    }

    private sealed class StubGoogleAuthenticationService : IGoogleAccountAuthenticationService
    {
        public Task<GoogleAuthenticationResult> CompleteAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(GoogleAuthenticationResult.Invalid(GoogleAuthenticationFailureReason.EmailUnverified));
    }
}
