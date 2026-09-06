using System.Net;
using CriatorioVirtual.Api;
using CriatorioVirtual.Application.Identity;
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
        Assert.DoesNotContain("forged", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubGoogleAuthenticationService : IGoogleAccountAuthenticationService
    {
        public Task<GoogleAuthenticationResult> CompleteAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(GoogleAuthenticationResult.Invalid());
    }
}
