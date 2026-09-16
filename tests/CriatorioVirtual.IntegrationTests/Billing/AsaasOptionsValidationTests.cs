using CriatorioVirtual.Infrastructure.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Billing;

public sealed class AsaasOptionsValidationTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void LocalEnvironment_DefaultsToSandbox(string environmentName)
    {
        var options = GetOptions(environmentName, new Dictionary<string, string?>());

        Assert.Equal(AsaasOptions.SandboxBaseUrl, options.BaseUrl);
    }

    [Fact]
    public void LocalEnvironment_RejectsProductionApiUrl()
    {
        var exception = Assert.Throws<OptionsValidationException>(() =>
            GetOptions("Development", new Dictionary<string, string?>
            {
                ["Billing:Asaas:BaseUrl"] = AsaasOptions.ProductionBaseUrl
            }));

        Assert.Contains(AsaasOptions.SandboxBaseUrl, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Homologation_RequiresApiKeyAndDefaultsToSandbox()
    {
        var exception = Assert.Throws<OptionsValidationException>(() =>
            GetOptions(AsaasOptions.HomologationEnvironmentName, new Dictionary<string, string?>()));

        Assert.Contains("ApiKey", exception.Message, StringComparison.Ordinal);

        var options = GetOptions(AsaasOptions.HomologationEnvironmentName, new Dictionary<string, string?>
        {
            ["Billing:Asaas:ApiKey"] = "sandbox-test-key"
        });

        Assert.Equal(AsaasOptions.SandboxBaseUrl, options.BaseUrl);
    }

    [Fact]
    public void Homologation_RejectsProductionApiUrlEvenWithConfiguredApiKey()
    {
        var exception = Assert.Throws<OptionsValidationException>(() =>
            GetOptions(AsaasOptions.HomologationEnvironmentName, new Dictionary<string, string?>
            {
                ["Billing:Asaas:ApiKey"] = "sandbox-test-key",
                ["Billing:Asaas:BaseUrl"] = AsaasOptions.ProductionBaseUrl
            }));

        Assert.Contains(AsaasOptions.SandboxBaseUrl, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalEnvironment_NormalizesSandboxApiRootForRelativeRequests()
    {
        var options = GetOptions("Development", new Dictionary<string, string?>
        {
            ["Billing:Asaas:BaseUrl"] = "https://api-sandbox.asaas.com/v3"
        });

        Assert.Equal(AsaasOptions.SandboxBaseUrl, options.BaseUrl);
    }

    [Fact]
    public void Production_RequiresApiKeyAndUsesProductionHost()
    {
        var exception = Assert.Throws<OptionsValidationException>(() =>
            GetOptions("Production", new Dictionary<string, string?>()));

        Assert.Contains("ApiKey", exception.Message, StringComparison.Ordinal);

        var options = GetOptions("Production", new Dictionary<string, string?>
        {
            ["Billing:Asaas:ApiKey"] = "production-secret"
        });
        Assert.Equal(AsaasOptions.ProductionBaseUrl, options.BaseUrl);
    }

    private static AsaasOptions GetOptions(
        string environmentName,
        IReadOnlyDictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var environment = new TestHostEnvironment(environmentName);
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(environment);
        services.AddAsaasBillingGateway(configuration, environment);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<AsaasOptions>>().Value;
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "CriatorioVirtual.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
