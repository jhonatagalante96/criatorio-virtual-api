using CriatorioVirtual.Api;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace CriatorioVirtual.IntegrationTests;

public sealed class DataProtectionCertificateLoaderTests
{
    [Fact]
    public void Load_RequiresCertificateAndPasswordConfiguration()
    {
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(() => DataProtectionCertificateLoader.Load(configuration));

        Assert.Contains(DataProtectionCertificateLoader.CertificateBase64ConfigurationKey, exception.Message, StringComparison.Ordinal);
        Assert.Contains(DataProtectionCertificateLoader.CertificatePasswordConfigurationKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_LoadsAValidPkcs12CertificateWithItsPrivateKey()
    {
        using var sourceCertificate = TestCertificate.Create();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DataProtectionCertificateLoader.CertificateBase64ConfigurationKey] = Convert.ToBase64String(
                    sourceCertificate.Export(X509ContentType.Pkcs12, TestCertificate.Password)),
                [DataProtectionCertificateLoader.CertificatePasswordConfigurationKey] = TestCertificate.Password
            })
            .Build();

        using var loadedCertificate = DataProtectionCertificateLoader.Load(configuration);

        Assert.True(loadedCertificate.HasPrivateKey);
        Assert.Equal(sourceCertificate.Thumbprint, loadedCertificate.Thumbprint);
    }

    [Fact]
    public void ProductionWithoutPostgreSqlPersistence_FailsStartup()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Production);
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CriatorioVirtual"] = string.Empty
            }));
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("required outside Development and Testing environments", exception.ToString(), StringComparison.Ordinal);
    }
}
