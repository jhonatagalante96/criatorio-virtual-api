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
    public void Homologation_RequiresBillingAndWebhookSecretsAndDefaultsToSandbox()
    {
        var exception = Assert.Throws<OptionsValidationException>(() =>
            GetOptions(AsaasOptions.HomologationEnvironmentName, new Dictionary<string, string?>()));

        Assert.Contains("ApiKey", exception.Message, StringComparison.Ordinal);
        Assert.Contains("WebhookToken", exception.Message, StringComparison.Ordinal);

        var options = GetOptions(AsaasOptions.HomologationEnvironmentName, new Dictionary<string, string?>
        {
            ["Billing:Asaas:ApiKey"] = "sandbox-test-key",
            ["Billing:Asaas:WebhookToken"] = "sandbox-webhook-secret-0123456789",
            ["Security:Email:ClientBaseUrl"] = "https://app.example.test"
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
                ["Billing:Asaas:WebhookToken"] = "sandbox-webhook-secret-0123456789",
                ["Security:Email:ClientBaseUrl"] = "https://app.example.test",
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
    public void Production_RequiresBillingAndWebhookSecretsAndUsesProductionHost()
    {
        var exception = Assert.Throws<OptionsValidationException>(() =>
            GetOptions("Production", new Dictionary<string, string?>()));

        Assert.Contains("ApiKey", exception.Message, StringComparison.Ordinal);
        Assert.Contains("WebhookToken", exception.Message, StringComparison.Ordinal);

        var options = GetOptions("Production", new Dictionary<string, string?>
        {
            ["Billing:Asaas:ApiKey"] = "production-secret",
            ["Billing:Asaas:WebhookToken"] = "production-webhook-secret-0123456789",
            ["Security:Email:ClientBaseUrl"] = "https://app.example.test"
        });
        Assert.Equal(AsaasOptions.ProductionBaseUrl, options.BaseUrl);
    }

    [Theory]
    [InlineData("too-short")]
    [InlineData("this-webhook-token-contains whitespace")]
    public void WebhookToken_RejectsValuesThatDoNotMeetAsaasRequirements(string token)
    {
        var exception = Assert.Throws<OptionsValidationException>(() =>
            GetOptions("Development", new Dictionary<string, string?>
            {
                ["Billing:Asaas:WebhookToken"] = token
            }));

        Assert.Contains("WebhookToken", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WebhookToken_MustBeDifferentFromApiKey()
    {
        const string token = "same-secret-value-0123456789-abcdef";
        var exception = Assert.Throws<OptionsValidationException>(() =>
            GetOptions("Development", new Dictionary<string, string?>
            {
                ["Billing:Asaas:ApiKey"] = token,
                ["Billing:Asaas:WebhookToken"] = token
            }));

        Assert.Contains("must not be the Asaas API key", exception.Message, StringComparison.Ordinal);
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
