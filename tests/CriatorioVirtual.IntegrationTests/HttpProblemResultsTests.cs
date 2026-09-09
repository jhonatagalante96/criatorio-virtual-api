using System.Text;
using CriatorioVirtual.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;

namespace CriatorioVirtual.IntegrationTests;

public sealed class HttpProblemResultsTests
{
    [Theory]
    [InlineData(400, "Bad Request")]
    [InlineData(401, "Unauthorized")]
    [InlineData(403, "Forbidden")]
    [InlineData(404, "Not Found")]
    [InlineData(409, "Conflict")]
    [InlineData(413, "Payload Too Large")]
    [InlineData(415, "Unsupported Media Type")]
    [InlineData(429, "Too Many Requests")]
    [InlineData(500, "Internal Server Error")]
    public async Task Write_UsesStandardProblemDetailsContract(int statusCode, string title)
    {
        var context = new DefaultHttpContext();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        context.RequestServices = services.BuildServiceProvider();
        context.Response.Body = new MemoryStream();

        await HttpProblemResults.Write(context, statusCode);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        Assert.Equal(statusCode, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Contains($"\"title\":\"{title}\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sql", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Write_IncludesTheRequestCorrelationId()
    {
        var context = new DefaultHttpContext();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        context.RequestServices = services.BuildServiceProvider();
        context.TraceIdentifier = "request-123";
        context.Response.Body = new MemoryStream();

        await HttpProblemResults.Write(context, StatusCodes.Status500InternalServerError);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("request-123", document.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Write_IncludesAnOperationalDetail()
    {
        var context = new DefaultHttpContext();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        context.RequestServices = services.BuildServiceProvider();
        context.Response.Body = new MemoryStream();

        await HttpProblemResults.Write(context, StatusCodes.Status401Unauthorized);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(
            "Authentication is required to access this resource.",
            document.RootElement.GetProperty("detail").GetString());
    }
}
