using System.Collections.Concurrent;
using System.Net;
using System.Net.Mail;
using CriatorioVirtual.Application.Identity;
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

        if (!IsSafeMessage(message, options.Value))
        {
            // Deliberately return a detail-free result. The action URL contains a
            // single-use token and must never be copied into logs or exceptions.
            return Task.FromResult(AuthenticationEmailDeliveryResult.Failed());
        }

        messages.Enqueue(message);
        return Task.FromResult(AuthenticationEmailDeliveryResult.Delivered());
    }

    private static bool IsSafeMessage(
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

        return message.Kind is AuthenticationEmailKind.Confirmation or AuthenticationEmailKind.PasswordReset;
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

public sealed class SmtpAuthenticationEmailSender(IOptions<AuthenticationEmailOptions> options)
    : IAuthenticationEmailSender
{
    public async Task<AuthenticationEmailDeliveryResult> SendAsync(
        AuthenticationEmailMessage message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var settings = options.Value;
            using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
            {
                EnableSsl = settings.SmtpUseSsl
            };
            if (!string.IsNullOrWhiteSpace(settings.SmtpUsername))
            {
                client.Credentials = new NetworkCredential(settings.SmtpUsername, settings.SmtpPassword);
            }

            using var mail = new MailMessage(
                new MailAddress(settings.SenderAddress),
                new MailAddress(message.Recipient))
            {
                Subject = message.Kind switch
                {
                    AuthenticationEmailKind.Confirmation => "Confirm your Criatório Virtual account",
                    AuthenticationEmailKind.PasswordReset => "Reset your Criatório Virtual password",
                    _ => "Criatório Virtual account security"
                },
                Body = $"Follow this link to continue: {message.ActionUrl}",
                IsBodyHtml = false
            };

            await client.SendMailAsync(mail, cancellationToken);
            return AuthenticationEmailDeliveryResult.Delivered();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // SMTP failures are intentionally detail-free. The action URL contains
            // a single-use token and must never be written to logs or errors.
            return AuthenticationEmailDeliveryResult.Failed();
        }
    }
}
