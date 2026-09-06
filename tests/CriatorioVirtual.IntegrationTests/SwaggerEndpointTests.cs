using System.Net;
using CriatorioVirtual.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CriatorioVirtual.IntegrationTests;

public sealed class SwaggerEndpointTests
{
    [Fact]
    public async Task TestingEnvironment_ExposesSwaggerDocument()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Testing"));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        var document = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Criatório Virtual API", document, StringComparison.Ordinal);
        Assert.Contains("/api/auth/confirm-email", document, StringComparison.Ordinal);
        Assert.Contains("/api/auth/confirm-email/resend", document, StringComparison.Ordinal);
    }
}
