using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;

namespace CriatorioVirtual.Infrastructure.Identity;

public sealed class AuthenticationEmailOptions
{
    public const string SectionName = "Security:Email";

    public string Provider { get; set; } = string.Empty;

    public string ClientBaseUrl { get; set; } = string.Empty;

    public string ConfirmationPath { get; set; } = "/auth/confirm-email";

    public string PasswordResetPath { get; set; } = "/auth/reset-password";

    public string SmtpHost { get; set; } = string.Empty;

    public int SmtpPort { get; set; } = 587;

    public bool SmtpUseSsl { get; set; } = true;

    public string SenderAddress { get; set; } = string.Empty;

    public string SmtpUsername { get; set; } = string.Empty;

    public string SmtpPassword { get; set; } = string.Empty;
}

internal sealed class AuthenticationEmailOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<AuthenticationEmailOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthenticationEmailOptions options)
    {
        var failures = new List<string>();
        var isLocalEnvironment = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        var provider = options.Provider?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(provider))
        {
            failures.Add("Security:Email:Provider is required.");
        }
        else if (!string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(provider, "Smtp", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(provider, "Unavailable", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Security:Email:Provider must be InMemory, Smtp, or Unavailable.");
        }

        if (string.Equals(provider, "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(options.SmtpHost))
            {
                failures.Add("Security:Email:SmtpHost is required for the Smtp provider.");
            }

            if (options.SmtpPort is < 1 or > 65535)
            {
                failures.Add("Security:Email:SmtpPort must be between 1 and 65535.");
            }

            if (!System.Net.Mail.MailAddress.TryCreate(options.SenderAddress, out _))
            {
                failures.Add("Security:Email:SenderAddress must be a valid e-mail address for the Smtp provider.");
            }
        }

        if (!Uri.TryCreate(options.ClientBaseUrl, UriKind.Absolute, out var clientBaseUri) ||
            clientBaseUri is null ||
            (clientBaseUri.Scheme != Uri.UriSchemeHttp && clientBaseUri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(clientBaseUri.UserInfo) ||
            !string.IsNullOrEmpty(clientBaseUri.Query) ||
            !string.IsNullOrEmpty(clientBaseUri.Fragment))
        {
            failures.Add("Security:Email:ClientBaseUrl must be an absolute HTTP(S) URL without credentials, query, or fragment.");
        }
        else if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase) &&
                 (!isLocalEnvironment || !IsLoopback(clientBaseUri.Host)))
        {
            failures.Add("The InMemory email provider is restricted to local and testing environments with a loopback ClientBaseUrl.");
        }
        else if (!isLocalEnvironment &&
                 !string.Equals(provider, "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Non-local authentication email delivery must use the Smtp provider.");
        }
        else if (!isLocalEnvironment &&
                 (clientBaseUri.Scheme != Uri.UriSchemeHttps || IsLoopback(clientBaseUri.Host)))
        {
            failures.Add("Production authentication email links must use HTTPS and a non-loopback ClientBaseUrl.");
        }

        ValidatePath(options.ConfirmationPath, "ConfirmationPath", failures);
        ValidatePath(options.PasswordResetPath, "PasswordResetPath", failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidatePath(string? path, string name, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith("/", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            path.Contains('?') ||
            path.Contains('#') ||
            path.Contains("..", StringComparison.Ordinal))
        {
            failures.Add($"Security:Email:{name} must be a safe relative path.");
        }
    }

    private static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        (System.Net.IPAddress.TryParse(host, out var address) && System.Net.IPAddress.IsLoopback(address));
}
