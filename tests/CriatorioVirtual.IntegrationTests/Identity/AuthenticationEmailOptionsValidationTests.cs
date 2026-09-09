using CriatorioVirtual.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Identity;

public sealed class AuthenticationEmailOptionsValidationTests
{
    [Fact]
    public void TestingResendConfiguration_RejectsANonLoopbackClientBaseUrl()
    {
        using var provider = BuildProvider("Testing", new Dictionary<string, string?>
        {
            ["Security:Email:Provider"] = "Resend",
            ["Security:Email:ClientBaseUrl"] = "https://production.example.com",
            ["Security:Email:ResendApiKey"] = "re_test",
            ["Security:Email:SenderAddress"] = "noreply@example.com"
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<AuthenticationEmailOptions>>().Value);

        Assert.Contains("loopback", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionResendConfiguration_RequiresApiKey()
    {
        using var provider = BuildProvider("Production", new Dictionary<string, string?>
        {
            ["Security:Email:Provider"] = "Resend",
            ["Security:Email:ClientBaseUrl"] = "https://app.example.com",
            ["Security:Email:SenderAddress"] = "noreply@example.com"
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<AuthenticationEmailOptions>>().Value);

        Assert.Contains("ResendApiKey", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionSmtpProviderIsRejected()
    {
        using var provider = BuildProvider("Production", new Dictionary<string, string?>
        {
            ["Security:Email:Provider"] = "Smtp",
            ["Security:Email:ClientBaseUrl"] = "https://app.example.com",
            ["Security:Email:SenderAddress"] = "noreply@example.com"
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<AuthenticationEmailOptions>>().Value);

        Assert.Contains("Resend", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProviderWhitespaceIsTrimmedWhenSelectingTheSender()
    {
        using var provider = BuildProvider("Testing", new Dictionary<string, string?>
        {
            ["Security:Email:Provider"] = " Resend ",
            ["Security:Email:ClientBaseUrl"] = "http://localhost:3000",
            ["Security:Email:ResendApiKey"] = "re_test",
            ["Security:Email:SenderAddress"] = "noreply@example.com"
        });

        var sender = provider.GetRequiredService<CriatorioVirtual.Application.Identity.IAuthenticationEmailSender>();

        Assert.IsType<ResendAuthenticationEmailSender>(sender);
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        IReadOnlyDictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var environment = new TestHostEnvironment(environmentName);
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(environment);
        services.AddAuthenticationEmailDelivery(configuration, environment);
        return services.BuildServiceProvider();
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "CriatorioVirtual.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
