using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CriatorioVirtual.IntegrationTests.Security;

internal static class TestCertificate
{
    public const string Password = "integration-test-only";

    public static X509Certificate2 Create()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=CriatorioVirtualIntegrationTests",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        using var generatedCertificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        var pkcs12 = generatedCertificate.Export(X509ContentType.Pkcs12, Password);

        return X509CertificateLoader.LoadPkcs12(
            pkcs12,
            Password,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
    }
}
