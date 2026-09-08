using CriatorioVirtual.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CriatorioVirtual.IntegrationTests;

public sealed class HttpsSchemeMiddlewareExtensionsTests
{
    [Fact]
    public async Task UseAssumedHttpsBehindProxy_LeavesSchemeUnchangedByDefault()
    {
        var context = await InvokeMiddlewareAsync(new ConfigurationBuilder().Build(), "http");

        Assert.Equal("http", context.Request.Scheme);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http")]
    [InlineData("https")]
    public async Task UseAssumedHttpsBehindProxy_UsesConfiguredHttpsWithoutTrustingForwardedProto(
        string? forwardedProto)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:AssumeHttpsBehindProxy"] = "true"
            })
            .Build();

        var context = await InvokeMiddlewareAsync(configuration, "http", forwardedProto);

        Assert.Equal("https", context.Request.Scheme);
    }

    private static async Task<DefaultHttpContext> InvokeMiddlewareAsync(
        IConfiguration configuration,
        string scheme,
        string? forwardedProto = null)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var builder = new ApplicationBuilder(services);
        builder.UseAssumedHttpsBehindProxy(configuration);
        builder.Run(_ => Task.CompletedTask);
        var application = builder.Build();
        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        context.Request.Scheme = scheme;
        if (forwardedProto is not null)
        {
            context.Request.Headers["X-Forwarded-Proto"] = forwardedProto;
        }

        await application(context);
        return context;
    }
}
