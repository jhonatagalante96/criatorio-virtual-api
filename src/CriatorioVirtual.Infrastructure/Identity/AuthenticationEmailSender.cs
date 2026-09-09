using System.Collections.Concurrent;
using CriatorioVirtual.Application.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace CriatorioVirtual.Infrastructure.Identity;

public interface IAuthenticationEmailInbox
{
    IReadOnlyCollection<AuthenticationEmailMessage> Messages { get; }
}

public sealed class InMemoryAuthenticationEmailSender(IOptions<AuthenticationEmailOptions> options)
    : IAuthenticationEmailSender, IAuthenticationEmailInbox
{
    private readonly ConcurrentQueue<AuthenticationEmailMessage> messages = new();

    public IReadOnlyCollection<AuthenticationEmailMessage> Messages => messages.ToArray();

    public Task<AuthenticationEmailDeliveryResult> SendAsync(
        AuthenticationEmailMessage message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!AuthenticationEmailActionUrlPolicy.IsSafeMessage(message, options.Value))
        {
            // Deliberately return a detail-free result. The action URL contains a
            // single-use token and must never be copied into logs or exceptions.
            return Task.FromResult(AuthenticationEmailDeliveryResult.Failed());
        }

        messages.Enqueue(message);
        return Task.FromResult(AuthenticationEmailDeliveryResult.Delivered());
    }
}

public sealed class UnavailableAuthenticationEmailSender : IAuthenticationEmailSender
{
    public Task<AuthenticationEmailDeliveryResult> SendAsync(
        AuthenticationEmailMessage message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AuthenticationEmailDeliveryResult.Failed());
    }
}

internal static class AuthenticationEmailActionUrlPolicy
{
    public static bool IsSafeMessage(
        AuthenticationEmailMessage message,
        AuthenticationEmailOptions options)
    {
        if (string.IsNullOrWhiteSpace(message.Recipient) ||
            !Uri.TryCreate(options.ClientBaseUrl, UriKind.Absolute, out var baseUri) ||
            !message.ActionUrl.IsAbsoluteUri ||
            !string.Equals(message.ActionUrl.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(message.ActionUrl.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) ||
            message.ActionUrl.Port != baseUri.Port ||
            !string.IsNullOrEmpty(message.ActionUrl.UserInfo) ||
            !string.IsNullOrEmpty(message.ActionUrl.Fragment))
        {
            return false;
        }

        var expectedPath = message.Kind switch
        {
            AuthenticationEmailKind.Confirmation => options.ConfirmationPath,
            AuthenticationEmailKind.PasswordReset => options.PasswordResetPath,
            _ => null
        };
        if (expectedPath is null ||
            !string.Equals(message.ActionUrl.AbsolutePath, expectedPath, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var query = QueryHelpers.ParseQuery(message.ActionUrl.Query);
            return query.TryGetValue("token", out var token) && !string.IsNullOrWhiteSpace(token.ToString());
        }
        catch (Exception)
        {
            return false;
        }
    }
}
