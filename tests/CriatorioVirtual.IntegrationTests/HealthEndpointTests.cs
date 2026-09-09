using System.Net;
using CriatorioVirtual.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CriatorioVirtual.IntegrationTests;

public sealed class HealthEndpointTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task GetHealth_ReturnsHealthyResponse()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Healthy", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetReadiness_ReturnsHealthyWithoutExternalDependencies()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MissingEndpoint_UsesTheSameCorrelationIdInTheResponseAndProblemDetails()
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/does-not-exist");
        request.Headers.Add(CorrelationIdMiddlewareExtensions.HeaderName, "request-123");

        var response = await client.SendAsync(request);
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("request-123", response.Headers.GetValues(CorrelationIdMiddlewareExtensions.HeaderName).Single());
        Assert.Equal("request-123", document.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task GetAntiforgeryToken_UsesASecureCookieAndConfiguredHeader()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/antiforgery/token");
        request.Headers.Add("Origin", "http://localhost:3000");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName, out var tokens));
        Assert.False(string.IsNullOrWhiteSpace(tokens.Single()));
        Assert.Contains(
            HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName,
            response.Headers.GetValues("Access-Control-Expose-Headers"),
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("__Host-CriatorioVirtual-Antiforgery", cookie, StringComparison.Ordinal);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=none", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAntiforgeryToken_AssumesHttpsFromTrustedDeploymentConfiguration()
    {
        using var configuredFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Logging:EventLog:LogLevel:Default"] = "None",
                    ["Security:AssumeHttpsBehindProxy"] = "true"
                })));
        using var client = configuredFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost")
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/antiforgery/token");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("X-Forwarded-Proto", "http");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Health_OnlyAllowsTheConfiguredOrigin()
    {
        var client = factory.CreateClient();
        using var trustedRequest = new HttpRequestMessage(HttpMethod.Get, "/health");
        trustedRequest.Headers.Add("Origin", "http://localhost:3000");
        using var untrustedRequest = new HttpRequestMessage(HttpMethod.Get, "/health");
        untrustedRequest.Headers.Add("Origin", "https://untrusted.example");

        var trustedResponse = await client.SendAsync(trustedRequest);
        var untrustedResponse = await client.SendAsync(untrustedRequest);

        Assert.Equal("http://localhost:3000", trustedResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(untrustedResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }
}

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Logging:EventLog:LogLevel:Default"] = "None"
        }));
    }
}
