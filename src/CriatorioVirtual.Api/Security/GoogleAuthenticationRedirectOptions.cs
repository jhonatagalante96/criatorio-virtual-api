using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace CriatorioVirtual.Api;

public sealed class GoogleAuthenticationRedirectOptions
{
    public const string RemoteProviderFailureCode = "remote_provider_failure";

    private GoogleAuthenticationRedirectOptions(
        Uri clientBaseUrl,
        string successPath,
        string failurePath)
    {
        ClientBaseUrl = clientBaseUrl;
        SuccessPath = successPath;
        FailurePath = failurePath;
    }

    public Uri ClientBaseUrl { get; }

    public string SuccessPath { get; }

    public string FailurePath { get; }

    public static GoogleAuthenticationRedirectOptions Create(
        IConfiguration googleConfiguration,
        IReadOnlyCollection<string> allowedOrigins)
    {
        ArgumentNullException.ThrowIfNull(googleConfiguration);
        ArgumentNullException.ThrowIfNull(allowedOrigins);

        var configuredClientBaseUrl = googleConfiguration["ClientBaseUrl"];
        var clientBaseUrl = string.IsNullOrWhiteSpace(configuredClientBaseUrl)
            ? allowedOrigins.FirstOrDefault()
            : configuredClientBaseUrl.Trim();

        if (!Uri.TryCreate(clientBaseUrl, UriKind.Absolute, out var clientBaseUri) ||
            clientBaseUri is null ||
            (clientBaseUri.Scheme != Uri.UriSchemeHttp && clientBaseUri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(clientBaseUri.UserInfo) ||
            !string.IsNullOrEmpty(clientBaseUri.Query) ||
            !string.IsNullOrEmpty(clientBaseUri.Fragment) ||
            clientBaseUri.AbsolutePath != "/")
        {
            throw new InvalidOperationException(
                "Security:Google:ClientBaseUrl must be an allowed absolute HTTP(S) origin without credentials, path, query, or fragment.");
        }

        if (!allowedOrigins.Any(origin => HasSameOrigin(clientBaseUri, origin)))
        {
            throw new InvalidOperationException(
                "Security:Google:ClientBaseUrl must match one of the configured Security:AllowedOrigins.");
        }

        if (clientBaseUri.Scheme != Uri.UriSchemeHttps && !IsLoopback(clientBaseUri.Host))
        {
            throw new InvalidOperationException(
                "Security:Google:ClientBaseUrl must use HTTPS unless it targets a loopback host.");
        }

        var successPath = ValidatePath(
            googleConfiguration["SuccessPath"],
            "/login",
            "SuccessPath");
        var failurePath = ValidatePath(
            googleConfiguration["FailurePath"],
            "/login",
            "FailurePath");

        return new GoogleAuthenticationRedirectOptions(clientBaseUri, successPath, failurePath);
    }

    public string BuildSuccessRedirect() =>
        BuildRedirect(SuccessPath);

    public string BuildSuccessCallbackDocument()
    {
        var clientOrigin = ClientBaseUrl.GetComponents(
            UriComponents.SchemeAndServer,
            UriFormat.UriEscaped);
        var clientOriginLiteral = JsonSerializer.Serialize(clientOrigin);
        var fallbackRedirectLiteral = JsonSerializer.Serialize(BuildSuccessRedirect());

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <title>Authentication complete</title>
            </head>
            <body>
              <script>
                (() => {
                  const opener = window.opener;
                  if (opener && !opener.closed) {
                    opener.postMessage(
                      { status: "success", type: "criatorio-google-authentication" },
                      {{clientOriginLiteral}});
                    window.close();
                    return;
                  }

                  window.location.replace({{fallbackRedirectLiteral}});
                })();
              </script>
            </body>
            </html>
            """;
    }

    public string BuildFailureRedirect(
        string errorCode,
        string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var target = BuildRedirect(FailurePath);
        return QueryHelpers.AddQueryString(
            target,
            new Dictionary<string, string?>
            {
                ["googleError"] = errorCode,
                ["correlationId"] = correlationId
            });
    }

    private string BuildRedirect(string path) =>
        new Uri(ClientBaseUrl, path.TrimStart('/')).ToString();

    private static string ValidatePath(
        string? configuredPath,
        string defaultPath,
        string name)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? defaultPath
            : configuredPath.Trim();
        if (path.Length > 512 ||
            !path.StartsWith("/", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            path.Contains('\\') ||
            path.Contains('?') ||
            path.Contains('#') ||
            path.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Security:Google:{name} must be a safe relative path.");
        }

        return path;
    }

    private static bool HasSameOrigin(Uri clientBaseUri, string allowedOrigin)
    {
        if (!Uri.TryCreate(allowedOrigin, UriKind.Absolute, out var allowedUri) ||
            allowedUri is null ||
            !string.IsNullOrEmpty(allowedUri.UserInfo) ||
            !string.IsNullOrEmpty(allowedUri.Query) ||
            !string.IsNullOrEmpty(allowedUri.Fragment) ||
            allowedUri.AbsolutePath != "/")
        {
            return false;
        }

        return string.Equals(clientBaseUri.Scheme, allowedUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(clientBaseUri.Host, allowedUri.Host, StringComparison.OrdinalIgnoreCase) &&
            clientBaseUri.Port == allowedUri.Port;
    }

    private static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
}
