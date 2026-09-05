using CriatorioVirtual.Api;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);
    }
}
