using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CriatorioVirtual.Infrastructure.Billing;

public sealed class AsaasOptions
{
    public const string SectionName = "Billing:Asaas";

    public const string SandboxBaseUrl = "https://api-sandbox.asaas.com/v3/";

    public const string ProductionBaseUrl = "https://api.asaas.com/v3/";

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;
}

public sealed class AsaasOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<AsaasOptions>
{
    public ValidateOptionsResult Validate(string? name, AsaasOptions options)
    {
        var failures = new List<string>();
        var isLocalEnvironment = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        var expectedBaseUrl = isLocalEnvironment
            ? AsaasOptions.SandboxBaseUrl
            : AsaasOptions.ProductionBaseUrl;

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri is null ||
            baseUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(baseUri.UserInfo) ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment) ||
            !string.Equals(baseUri.AbsoluteUri.TrimEnd('/'), expectedBaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{AsaasOptions.SectionName}:BaseUrl must be {expectedBaseUrl} for the current environment.");
        }

        if (!isLocalEnvironment && string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add($"{AsaasOptions.SectionName}:ApiKey is required outside Development and Testing environments.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
