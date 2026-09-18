using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CriatorioVirtual.Infrastructure.Billing;

public sealed class AsaasOptions
{
    public const string SectionName = "Billing:Asaas";

    public const string SandboxBaseUrl = "https://api-sandbox.asaas.com/v3/";

    public const string ProductionBaseUrl = "https://api.asaas.com/v3/";

    public const string HomologationEnvironmentName = "Homologation";

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string WebhookToken { get; set; } = string.Empty;

    public string CheckoutCallbackBaseUrl { get; set; } = string.Empty;

    public int CheckoutMinutesToExpire { get; set; } = 1440;
}

public sealed class AsaasOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<AsaasOptions>
{
    public ValidateOptionsResult Validate(string? name, AsaasOptions options)
    {
        var failures = new List<string>();
        var isLocalEnvironment = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        var isSandboxEnvironment = isLocalEnvironment ||
                                   environment.IsEnvironment(AsaasOptions.HomologationEnvironmentName);
        var expectedBaseUrl = isSandboxEnvironment
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

        if (!isLocalEnvironment && string.IsNullOrWhiteSpace(options.WebhookToken))
        {
            failures.Add($"{AsaasOptions.SectionName}:WebhookToken is required outside Development and Testing environments.");
        }

        if (!string.IsNullOrWhiteSpace(options.WebhookToken) &&
            (options.WebhookToken.Length is < 32 or > 255 || options.WebhookToken.Any(char.IsWhiteSpace)))
        {
            failures.Add($"{AsaasOptions.SectionName}:WebhookToken must contain 32 to 255 non-whitespace characters.");
        }

        if (!string.IsNullOrWhiteSpace(options.WebhookToken) &&
            !string.IsNullOrWhiteSpace(options.ApiKey) &&
            string.Equals(options.WebhookToken, options.ApiKey, StringComparison.Ordinal))
        {
            failures.Add($"{AsaasOptions.SectionName}:WebhookToken must not be the Asaas API key.");
        }

        if (!Uri.TryCreate(options.CheckoutCallbackBaseUrl, UriKind.Absolute, out var callbackBaseUri) ||
            callbackBaseUri is null ||
            (callbackBaseUri.Scheme != Uri.UriSchemeHttps && callbackBaseUri.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(callbackBaseUri.UserInfo) ||
            !string.IsNullOrEmpty(callbackBaseUri.Query) ||
            !string.IsNullOrEmpty(callbackBaseUri.Fragment) ||
            callbackBaseUri.AbsolutePath != "/" ||
            (!isLocalEnvironment && callbackBaseUri.Scheme != Uri.UriSchemeHttps) ||
            (isLocalEnvironment && callbackBaseUri.Scheme != Uri.UriSchemeHttps && !IsLoopback(callbackBaseUri.Host)))
        {
            failures.Add($"{AsaasOptions.SectionName}:CheckoutCallbackBaseUrl must be an allowed absolute client origin.");
        }

        if (options.CheckoutMinutesToExpire is < 10 or > 1440)
        {
            failures.Add($"{AsaasOptions.SectionName}:CheckoutMinutesToExpire must be between 10 and 1440 minutes.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        System.Net.IPAddress.TryParse(host, out var address) && System.Net.IPAddress.IsLoopback(address);
}
