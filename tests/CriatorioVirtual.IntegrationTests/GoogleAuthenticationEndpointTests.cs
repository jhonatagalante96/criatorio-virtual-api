using System.Net;
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
    public async Task TamperedGoogleCallback_RedirectsToFrontendWithSafeErrorCode()
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

        AssertGoogleFailureRedirect(response, "remote_provider_failure");
        Assert.DoesNotContain("forged", response.Headers.Location!.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GoogleCallback_RedirectsToFrontendWithSafeErrorCode()
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

        AssertGoogleFailureRedirect(response, "email_unverified");
    }

    [Fact]
    public async Task SuccessfulGoogleCallback_RedirectsToFrontend()
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
            builder.ConfigureServices(services =>
            {
                services.AddScoped<IGoogleAccountAuthenticationService, SuccessfulGoogleAuthenticationService>();
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
            "/api/test/google/seed?providerKey=google-sub&email=google@example.com");
        Assert.Equal(HttpStatusCode.NoContent, seed.StatusCode);

        using var response = await client.GetAsync("/api/auth/google/callback");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("http://localhost:3000/", response.Headers.Location?.ToString());
    }

    private sealed class StubGoogleAuthenticationService : IGoogleAccountAuthenticationService
    {
        public Task<GoogleAuthenticationResult> CompleteAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(GoogleAuthenticationResult.Invalid(GoogleAuthenticationFailureReason.EmailUnverified));
    }

    private sealed class SuccessfulGoogleAuthenticationService : IGoogleAccountAuthenticationService
    {
        public Task<GoogleAuthenticationResult> CompleteAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(GoogleAuthenticationResult.Succeeded());
    }

    private static void AssertGoogleFailureRedirect(
        HttpResponseMessage response,
        string expectedErrorCode)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal("http", location.Scheme);
        Assert.Equal("localhost", location.Host);
        Assert.Equal(3000, location.Port);
        Assert.Equal("/login", location.AbsolutePath);
        Assert.Contains($"googleError={expectedErrorCode}", location.Query, StringComparison.Ordinal);
        Assert.Contains("correlationId=", location.Query, StringComparison.Ordinal);
    }
}
