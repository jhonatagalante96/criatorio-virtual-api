using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CriatorioVirtual.Infrastructure.Storage;

public sealed class PrivateStorageOptions
{
    public const string SectionName = "Storage";

    public string Provider { get; set; } = "FileSystem";

    public string PrivateRootPath { get; set; } = string.Empty;

    public string LegacyPrivateRootPath { get; set; } = string.Empty;

    public S3PrivateStorageOptions S3 { get; set; } = new();
}

public sealed class S3PrivateStorageOptions
{
    public string Endpoint { get; set; } = string.Empty;

    public string Bucket { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string AccessKeyId { get; set; } = string.Empty;

    public string SecretAccessKey { get; set; } = string.Empty;

    public bool ForcePathStyle { get; set; }
}

public sealed class PrivateStorageOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<PrivateStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, PrivateStorageOptions options)
    {
        var isLocal = IsLocalEnvironment(environment);
        var provider = options.Provider?.Trim();

        if (string.Equals(provider, "FileSystem", StringComparison.OrdinalIgnoreCase))
        {
            if (!isLocal)
            {
                return ValidateOptionsResult.Fail("Storage:Provider must be S3 outside Development and Testing environments.");
            }

            if (!string.IsNullOrWhiteSpace(options.PrivateRootPath) &&
                !IsAbsolutePath(options.PrivateRootPath))
            {
                return ValidateOptionsResult.Fail("Storage:PrivateRootPath must be an absolute path.");
            }
        }
        else if (string.Equals(provider, "S3", StringComparison.OrdinalIgnoreCase))
        {
            var s3 = options.S3;
            if (s3 is null ||
                string.IsNullOrWhiteSpace(s3.Bucket) ||
                string.IsNullOrWhiteSpace(s3.Region) ||
                string.IsNullOrWhiteSpace(s3.AccessKeyId) ||
                string.IsNullOrWhiteSpace(s3.SecretAccessKey) ||
                !Uri.TryCreate(s3.Endpoint, UriKind.Absolute, out var endpoint) ||
                endpoint.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(endpoint.UserInfo) ||
                !string.IsNullOrEmpty(endpoint.Query) ||
                !string.IsNullOrEmpty(endpoint.Fragment))
            {
                return ValidateOptionsResult.Fail("Storage:S3 requires an HTTPS endpoint, bucket, region, access key ID, and secret access key.");
            }
        }
        else
        {
            return ValidateOptionsResult.Fail("Storage:Provider must be FileSystem or S3.");
        }

        if (!string.IsNullOrWhiteSpace(options.LegacyPrivateRootPath) &&
            !IsAbsolutePath(options.LegacyPrivateRootPath))
        {
            return ValidateOptionsResult.Fail("Storage:LegacyPrivateRootPath must be an absolute path.");
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsLocalEnvironment(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing");

    private static bool IsAbsolutePath(string path)
    {
        try
        {
            return Path.IsPathRooted(path) && !string.IsNullOrWhiteSpace(Path.GetFullPath(path));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
