using CriatorioVirtual.Application.Identity;
using Microsoft.AspNetCore.Identity;

namespace CriatorioVirtual.Infrastructure.Identity;

public interface IAuthenticationEmailConfirmationThrottle
{
    bool TryAcquire(string normalizedEmail);
}

public sealed class AccountEmailConfirmationService(
    UserManager<ApplicationUser> userManager,
    IAuthenticationEmailLinkBuilder emailLinkBuilder,
    IAuthenticationEmailSender emailSender,
    IAuthenticationEmailConfirmationThrottle throttle)
    : IAccountEmailConfirmationService
{
    public async Task<EmailConfirmationResult> ConfirmAsync(
        Guid userId,
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(token))
        {
            return EmailConfirmationResult.InvalidToken();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByIdAsync(userId.ToString("D"));
        if (user is null)
        {
            return EmailConfirmationResult.InvalidToken();
        }

        if (user.EmailConfirmed)
        {
            var tokenIsValid = await userManager.VerifyUserTokenAsync(
                user,
                TokenOptions.DefaultProvider,
                UserManager<ApplicationUser>.ConfirmEmailTokenPurpose,
                token);

            return tokenIsValid
                ? EmailConfirmationResult.AlreadyConfirmed()
                : EmailConfirmationResult.InvalidToken();
        }

        var result = await userManager.ConfirmEmailAsync(user, token);
        return result.Succeeded
            ? EmailConfirmationResult.Confirmed()
            : EmailConfirmationResult.InvalidToken();
    }

    public async Task<EmailConfirmationResendResult> ResendAsync(
        string? email,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return EmailConfirmationResendResult.Accepted();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var normalizedEmail = email.Trim();
        if (!throttle.TryAcquire(normalizedEmail))
        {
            return EmailConfirmationResendResult.RateLimited();
        }

        var user = await userManager.FindByEmailAsync(normalizedEmail);
        if (user is null || user.EmailConfirmed || string.IsNullOrWhiteSpace(user.Email))
        {
            // Keep unknown and already confirmed addresses indistinguishable from
            // an accepted resend so the endpoint cannot enumerate accounts.
            return EmailConfirmationResendResult.Accepted();
        }

        try
        {
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            var actionUrl = emailLinkBuilder.Build(
                AuthenticationEmailKind.Confirmation,
                user.Id,
                token);
            var delivery = await emailSender.SendAsync(
                new AuthenticationEmailMessage(AuthenticationEmailKind.Confirmation, user.Email, actionUrl),
                cancellationToken);

            return delivery.Succeeded
                ? EmailConfirmationResendResult.Accepted()
                : EmailConfirmationResendResult.DeliveryFailed();
        }
        catch (InvalidOperationException)
        {
            return EmailConfirmationResendResult.DeliveryFailed();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return EmailConfirmationResendResult.DeliveryFailed();
        }
    }
}
