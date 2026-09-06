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

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Criatório Virtual API", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
