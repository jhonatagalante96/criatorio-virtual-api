using CriatorioVirtual.Application.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace CriatorioVirtual.Infrastructure.Identity;

public sealed class AuthenticationEmailLinkBuilder(IOptions<AuthenticationEmailOptions> options)
    : IAuthenticationEmailLinkBuilder
{
    public Uri Build(AuthenticationEmailKind kind, Guid userId, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var settings = options.Value;
        var path = kind switch
        {
            AuthenticationEmailKind.Confirmation => settings.ConfirmationPath,
            AuthenticationEmailKind.PasswordReset => settings.PasswordResetPath,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported authentication email kind.")
        };

        var baseUri = new Uri(settings.ClientBaseUrl, UriKind.Absolute);
        var builder = new UriBuilder(new Uri(baseUri, path));
        builder.Query = QueryHelpers.AddQueryString(
            string.Empty,
            new Dictionary<string, string?>
            {
                ["userId"] = userId.ToString("D"),
                ["token"] = token
            }).TrimStart('?');

        return builder.Uri;
    }
}
