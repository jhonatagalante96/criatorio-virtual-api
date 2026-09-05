using CriatorioVirtual.Api;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using Xunit;

namespace CriatorioVirtual.IntegrationTests;

public sealed class HttpSecurityServiceCollectionExtensionsTests
{
    [Fact]
    public void AddHttpSecurity_RejectsWildcardOrigins()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:AllowedOrigins:0"] = "https://*.example.com"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddHttpSecurity(configuration));
    }

    [Fact]
    public void AddHttpSecurity_RejectsInvalidTrustedProxyAddress()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:TrustedProxyAddresses:0"] = "not-an-ip-address"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddHttpSecurity(configuration));
    }

    [Fact]
    public void AddHttpSecurity_ConfiguresAProtectedApplicationCookie()
    {
        var services = new ServiceCollection();
        services.AddHttpSecurity(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal("__Host-CriatorioVirtual-Auth", options.Cookie.Name);
        Assert.Equal("/", options.Cookie.Path);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);
    }

    [Fact]
    public void AddHttpSecurity_ExposesTheAntiforgeryHeaderToTheTrustedOrigin()
    {
        var services = new ServiceCollection();
        services.AddHttpSecurity(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        var policy = provider.GetRequiredService<IOptions<CorsOptions>>().Value.GetPolicy("trusted-client");

        Assert.NotNull(policy);
        Assert.Contains(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName, policy.ExposedHeaders);
        Assert.True(policy.SupportsCredentials);
    }

    [Fact]
    public void AddHttpSecurity_DisablesForwardedHeadersWithoutATrustedProxy()
    {
        var services = new ServiceCollection();
        services.AddHttpSecurity(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Equal(ForwardedHeaders.None, options.ForwardedHeaders);
        Assert.Empty(options.KnownIPNetworks);
        Assert.Empty(options.KnownProxies);
    }

    [Fact]
    public void AddHttpSecurity_OnlyTrustsExplicitlyConfiguredProxyAddresses()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:TrustedProxyAddresses:0"] = "10.0.0.10"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddHttpSecurity(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
        Assert.Empty(options.KnownIPNetworks);
        Assert.Equal(System.Net.IPAddress.Parse("10.0.0.10"), Assert.Single(options.KnownProxies));
    }

    [Fact]
    public async Task ForwardedHeaders_FromAnUntrustedClientAreIgnored()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpSecurity(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var builder = new ApplicationBuilder(provider);
        builder.UseForwardedHeaders();
        builder.Run(_ => Task.CompletedTask);
        var application = builder.Build();
        var originalAddress = IPAddress.Parse("203.0.113.10");
        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };
        context.Request.Scheme = "http";
        context.Connection.RemoteIpAddress = originalAddress;
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.10";
        context.Request.Headers["X-Forwarded-Proto"] = "https";

        await application(context);

        Assert.Equal("http", context.Request.Scheme);
        Assert.Equal(originalAddress, context.Connection.RemoteIpAddress);
    }
}
