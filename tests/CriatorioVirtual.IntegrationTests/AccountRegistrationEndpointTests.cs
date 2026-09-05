using System.Net;
using System.Net.Http.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Xml.Linq;
using Xunit;

namespace CriatorioVirtual.IntegrationTests;

public sealed class AccountRegistrationEndpointTests
{
    [Fact]
    public async Task Register_RejectsInvalidInputWithoutReturningSecrets()
    {
        using var certificate = TestCertificate.Create();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CriatorioVirtual"] = "Host=localhost;Port=5432;Database=registration_contract;Username=postgres;Password=test",
                ["Logging:EventLog:LogLevel:Default"] = "None",
                [DataProtectionCertificateLoader.CertificateBase64ConfigurationKey] = Convert.ToBase64String(
                    certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12, TestCertificate.Password)),
                [DataProtectionCertificateLoader.CertificatePasswordConfigurationKey] = TestCertificate.Password
            }));
            builder.ConfigureServices(services => services.PostConfigure<KeyManagementOptions>(options =>
                options.XmlRepository = new TestXmlRepository()));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, "/antiforgery/token");
        tokenRequest.Headers.Add("Origin", "http://localhost:3000");
        using var tokenResponse = await client.SendAsync(tokenRequest);
        var requestToken = tokenResponse.Headers.GetValues(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName).Single();

        using var registrationRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new { email = "not-an-email", password = "short" })
        };
        registrationRequest.Headers.Add("Origin", "http://localhost:3000");
        registrationRequest.Headers.Add(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName, requestToken);

        using var response = await client.SendAsync(registrationRequest);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("short", body, StringComparison.Ordinal);
        Assert.DoesNotContain("not-an-email", body, StringComparison.Ordinal);
    }

    private sealed class TestXmlRepository : IXmlRepository
    {
        private readonly List<XElement> elements = [];

        public IReadOnlyCollection<XElement> GetAllElements() => elements.Select(element => new XElement(element)).ToArray();

        public void StoreElement(XElement element, string friendlyName) => elements.Add(new XElement(element));
    }
}
