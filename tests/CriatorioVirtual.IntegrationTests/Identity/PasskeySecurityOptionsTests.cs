using CriatorioVirtual.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Identity;

public sealed class PasskeySecurityOptionsTests
{
    [Fact]
    public async Task AddPasskeySecurity_ConfiguresNativeIdentityRequirementsAndAllowedOrigins()
    {
        var services = new ServiceCollection();
        services.AddPasskeySecurity(
            CreateConfiguration(
                ("Security:AllowedOrigins:0", "https://app.example.com"),
                ("Security:Passkeys:ServerDomain", "example.com")),
            CreateEnvironment("Production"));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<IdentityPasskeyOptions>>().Value;

        Assert.Equal("example.com", options.ServerDomain);
        Assert.Equal(TimeSpan.FromMinutes(2), options.AuthenticatorTimeout);
        Assert.Equal(32, options.ChallengeSize);
        Assert.Equal("required", options.UserVerificationRequirement);
        Assert.Equal("required", options.ResidentKeyRequirement);
        Assert.Equal("none", options.AttestationConveyancePreference);
        Assert.NotNull(options.ValidateOrigin);
        Assert.True(await options.ValidateOrigin!(new PasskeyOriginValidationContext
        {
            HttpContext = new DefaultHttpContext(),
            Origin = "https://app.example.com",
            CrossOrigin = false
        }));
        Assert.False(await options.ValidateOrigin!(new PasskeyOriginValidationContext
        {
            HttpContext = new DefaultHttpContext(),
            Origin = "https://attacker.example",
            CrossOrigin = false
        }));
        Assert.False(await options.ValidateOrigin!(new PasskeyOriginValidationContext
        {
            HttpContext = new DefaultHttpContext(),
            Origin = "https://app.example.com",
            CrossOrigin = true
        }));
    }

    [Fact]
    public void AddPasskeySecurity_UsesLocalhostOnlyForLocalEnvironments()
    {
        var services = new ServiceCollection();
        services.AddPasskeySecurity(
            CreateConfiguration(("Security:AllowedOrigins:0", "http://localhost:3000")),
            CreateEnvironment("Testing"));

        using var provider = services.BuildServiceProvider();

        Assert.Equal(
            "localhost",
            provider.GetRequiredService<IOptions<IdentityPasskeyOptions>>().Value.ServerDomain);
    }

    [Fact]
    public void AddPasskeySecurity_RejectsMissingProductionRpId()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddPasskeySecurity(
            CreateConfiguration(("Security:AllowedOrigins:0", "https://app.example.com")),
            CreateEnvironment("Production")));

        Assert.Contains("Security:Passkeys:ServerDomain", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddPasskeySecurity_RejectsOriginsOutsideRpId()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddPasskeySecurity(
            CreateConfiguration(
                ("Security:AllowedOrigins:0", "https://attacker.example"),
                ("Security:Passkeys:ServerDomain", "example.com")),
            CreateEnvironment("Production")));

        Assert.Contains("outside the configured passkey RP ID", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddPasskeySecurity_KeepsLoopbackOriginsForCorsOnlyWhenProductionOriginsAreConfigured()
    {
        var services = new ServiceCollection();
        services.AddPasskeySecurity(
            CreateConfiguration(
                ("Security:AllowedOrigins:0", "https://hml.criatorio-virtual.com.br"),
                ("Security:AllowedOrigins:1", "http://localhost:3001"),
                ("Security:AllowedOrigins:2", "http://localhost:3000"),
                ("Security:Passkeys:ServerDomain", "criatorio-virtual.com.br")),
            CreateEnvironment("Production"));

        using var provider = services.BuildServiceProvider();
        var validateOrigin = provider.GetRequiredService<IOptions<IdentityPasskeyOptions>>().Value.ValidateOrigin!;

        Assert.True(await validateOrigin(new PasskeyOriginValidationContext
        {
            HttpContext = new DefaultHttpContext(),
            Origin = "https://hml.criatorio-virtual.com.br",
            CrossOrigin = false
        }));
        Assert.False(await validateOrigin(new PasskeyOriginValidationContext
        {
            HttpContext = new DefaultHttpContext(),
            Origin = "http://localhost:3001",
            CrossOrigin = false
        }));
        Assert.False(await validateOrigin(new PasskeyOriginValidationContext
        {
            HttpContext = new DefaultHttpContext(),
            Origin = "http://localhost:3000",
            CrossOrigin = false
        }));
    }

    [Fact]
    public async Task AddPasskeySecurity_UsesExplicitPasskeyOriginsWhenConfigured()
    {
        var services = new ServiceCollection();
        services.AddPasskeySecurity(
            CreateConfiguration(
                ("Security:AllowedOrigins:0", "https://hml.criatorio-virtual.com.br"),
                ("Security:AllowedOrigins:1", "https://admin.criatorio-virtual.com.br"),
                ("Security:Passkeys:AllowedOrigins:0", "https://hml.criatorio-virtual.com.br"),
                ("Security:Passkeys:ServerDomain", "criatorio-virtual.com.br")),
            CreateEnvironment("Production"));

        using var provider = services.BuildServiceProvider();
        var validateOrigin = provider.GetRequiredService<IOptions<IdentityPasskeyOptions>>().Value.ValidateOrigin!;

        Assert.True(await validateOrigin(new PasskeyOriginValidationContext
        {
            HttpContext = new DefaultHttpContext(),
            Origin = "https://hml.criatorio-virtual.com.br",
            CrossOrigin = false
        }));
        Assert.False(await validateOrigin(new PasskeyOriginValidationContext
        {
            HttpContext = new DefaultHttpContext(),
            Origin = "https://admin.criatorio-virtual.com.br",
            CrossOrigin = false
        }));
    }

    private static IConfiguration CreateConfiguration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(value => value.Key, value => (string?)value.Value))
            .Build();

    private static IHostEnvironment CreateEnvironment(string environmentName) =>
        new TestHostEnvironment
        {
            ApplicationName = "CriatorioVirtual.IntegrationTests",
            EnvironmentName = environmentName
        };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = string.Empty;

        public string ApplicationName { get; set; } = string.Empty;

        public string ApplicationId { get; set; } = string.Empty;

        public string ContentRootPath { get; set; } = string.Empty;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
