using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CriatorioVirtual.Api;

public static class DataProtectionCertificateLoader
{
    public const string CertificateBase64ConfigurationKey = "Security:DataProtection:CertificateBase64";
    public const string CertificatePasswordConfigurationKey = "Security:DataProtection:CertificatePassword";

    public static X509Certificate2 Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var certificateBase64 = configuration[CertificateBase64ConfigurationKey];
        var certificatePassword = configuration[CertificatePasswordConfigurationKey];

        if (string.IsNullOrWhiteSpace(certificateBase64) || string.IsNullOrWhiteSpace(certificatePassword))
        {
            throw new InvalidOperationException(
                $"{CertificateBase64ConfigurationKey} and {CertificatePasswordConfigurationKey} are required when PostgreSQL persistence is enabled.");
        }

        try
        {
            var certificate = X509CertificateLoader.LoadPkcs12(
                Convert.FromBase64String(certificateBase64),
                certificatePassword,
                X509KeyStorageFlags.EphemeralKeySet);

            if (!certificate.HasPrivateKey)
            {
                certificate.Dispose();
                throw new InvalidOperationException("The Data Protection certificate must contain a private key.");
            }

            var now = DateTimeOffset.UtcNow;
            if (now < certificate.NotBefore || now > certificate.NotAfter)
            {
                certificate.Dispose();
                throw new InvalidOperationException("The Data Protection certificate is not currently valid.");
            }

            return certificate;
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("The configured Data Protection certificate is not valid base64.", exception);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("The configured Data Protection certificate could not be loaded.", exception);
        }
    }
}
