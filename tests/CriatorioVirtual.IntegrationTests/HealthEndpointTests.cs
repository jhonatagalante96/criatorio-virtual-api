using System.Net;
using CriatorioVirtual.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CriatorioVirtual.IntegrationTests;

public sealed class HealthEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
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
}
