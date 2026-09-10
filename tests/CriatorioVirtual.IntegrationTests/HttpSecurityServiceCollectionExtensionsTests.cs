using CriatorioVirtual.Api;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
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
    public void AddHttpSecurity_RejectsPartialGoogleConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Google:ClientId"] = "client-id"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddHttpSecurity(configuration));
    }

    [Fact]
    public async Task AddHttpSecurity_ConfiguresGoogleAsAnExternalCookieProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Google:ClientId"] = "client-id",
                ["Security:Google:ClientSecret"] = "client-secret"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddHttpSecurity(configuration);
        using var provider = services.BuildServiceProvider();

        var scheme = await provider.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetSchemeAsync(GoogleDefaults.AuthenticationScheme);
        var options = provider.GetRequiredService<IOptionsMonitor<GoogleOptions>>()
            .Get(GoogleDefaults.AuthenticationScheme);

        Assert.NotNull(scheme);
        Assert.Equal(IdentityConstants.ExternalScheme, options.SignInScheme);
        Assert.Equal("/signin-google", options.CallbackPath);
        Assert.False(options.SaveTokens);
        Assert.Contains("openid", options.Scope);
        Assert.Contains("profile", options.Scope);
        Assert.Contains("email", options.Scope);
    }

    [Fact]
    public void AddHttpSecurity_ConfiguresSafeGoogleFrontendRedirects()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:AllowedOrigins:0"] = "https://app.example.com",
                ["Security:Google:ClientId"] = "client-id",
                ["Security:Google:ClientSecret"] = "client-secret",
                ["Security:Google:ClientBaseUrl"] = "https://app.example.com",
                ["Security:Google:SuccessPath"] = "/dashboard",
                ["Security:Google:FailurePath"] = "/login"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddHttpSecurity(configuration);
        using var provider = services.BuildServiceProvider();

        var redirectOptions = provider.GetRequiredService<GoogleAuthenticationRedirectOptions>();

        Assert.Equal("https://app.example.com/dashboard", redirectOptions.BuildSuccessRedirect());
        var failureRedirect = redirectOptions.BuildFailureRedirect("email_unverified", "correlation-123");
        Assert.StartsWith("https://app.example.com/login?", failureRedirect, StringComparison.Ordinal);
        Assert.Contains("googleError=email_unverified", failureRedirect, StringComparison.Ordinal);
        Assert.Contains("correlationId=correlation-123", failureRedirect, StringComparison.Ordinal);
    }

    [Fact]
    public void AddHttpSecurity_DefaultsGoogleSuccessRedirectToLogin()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:AllowedOrigins:0"] = "https://app.example.com",
                ["Security:Google:ClientId"] = "client-id",
                ["Security:Google:ClientSecret"] = "client-secret",
                ["Security:Google:ClientBaseUrl"] = "https://app.example.com"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddHttpSecurity(configuration);
        using var provider = services.BuildServiceProvider();

        var redirectOptions = provider.GetRequiredService<GoogleAuthenticationRedirectOptions>();

        Assert.Equal("https://app.example.com/login", redirectOptions.BuildSuccessRedirect());
    }

    [Fact]
    public void AddHttpSecurity_RejectsGoogleFrontendRedirectOutsideAllowedOrigins()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:AllowedOrigins:0"] = "https://app.example.com",
                ["Security:Google:ClientId"] = "client-id",
                ["Security:Google:ClientSecret"] = "client-secret",
                ["Security:Google:ClientBaseUrl"] = "https://attacker.example.com"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddHttpSecurity(configuration));
    }

    [Fact]
    public void AddHttpSecurity_RejectsGoogleFrontendOpenRedirectPath()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Google:ClientId"] = "client-id",
                ["Security:Google:ClientSecret"] = "client-secret",
                ["Security:Google:FailurePath"] = "https://attacker.example.com"
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
        Assert.Equal(SameSiteMode.None, options.Cookie.SameSite);
    }

    [Fact]
    public void AddHttpSecurity_ConfiguresTheAntiforgeryCookieForTheTrustedCrossSiteClient()
    {
        var services = new ServiceCollection();
        services.AddHttpSecurity(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;

        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal("__Host-CriatorioVirtual-Antiforgery", options.Cookie.Name);
        Assert.Equal("/", options.Cookie.Path);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.None, options.Cookie.SameSite);
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

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task AddHttpSecurity_UsesApiStatusCodesWithoutRedirectingOrSigningOut(int expectedStatusCode)
    {
        var services = new ServiceCollection();
        services.AddHttpSecurity(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var context = new DefaultHttpContext
        {
            Response = { Body = new MemoryStream() }
        };
        context.Request.Path = "/api/auth/session";
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
        context.User = principal;
        var redirectContext = new RedirectContext<CookieAuthenticationOptions>(
            context,
            new AuthenticationScheme(
                IdentityConstants.ApplicationScheme,
                displayName: null,
                typeof(CookieAuthenticationHandler)),
            options,
            new AuthenticationProperties(),
            "/login?returnUrl=%2Fapi%2Fauth%2Fsession");

        if (expectedStatusCode == StatusCodes.Status401Unauthorized)
        {
            await options.Events.OnRedirectToLogin(redirectContext);
        }
        else
        {
            await options.Events.OnRedirectToAccessDenied(redirectContext);
        }

        Assert.Equal(expectedStatusCode, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
        Assert.Same(principal, context.User);
        Assert.True(context.User.Identity?.IsAuthenticated);
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
