using CriatorioVirtual.Application.Identity;
using Microsoft.AspNetCore.Identity;

namespace CriatorioVirtual.Infrastructure.Identity;

public sealed class AccountPasswordService(
    UserManager<ApplicationUser> userManager,
    IAuthenticationEmailLinkBuilder emailLinkBuilder,
    IAuthenticationEmailSender emailSender)
    : IAccountPasswordService
{
    public async Task<PasswordRecoveryRequestResult> RequestResetAsync(
        string? email,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return PasswordRecoveryRequestResult.Accepted();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByEmailAsync(email.Trim());
        if (user is null || string.IsNullOrWhiteSpace(user.Email) || !await userManager.HasPasswordAsync(user))
        {
            return PasswordRecoveryRequestResult.Accepted();
        }

        try
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var actionUrl = emailLinkBuilder.Build(
                AuthenticationEmailKind.PasswordReset,
                user.Id,
                token);
            await emailSender.SendAsync(
                new AuthenticationEmailMessage(AuthenticationEmailKind.PasswordReset, user.Email, actionUrl),
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Recovery remains deliberately generic even when token generation or
            // delivery fails, so the endpoint cannot disclose account state.
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Do not expose provider details or the action URL, which contains a
            // password reset token.
        }

        return PasswordRecoveryRequestResult.Accepted();
    }

    public async Task<PasswordResetResult> ResetAsync(
        Guid userId,
        string? token,
        string? newPassword,
        string? confirmPassword,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(token))
        {
            return PasswordResetResult.InvalidToken();
        }

        if (string.IsNullOrEmpty(newPassword))
        {
            return PasswordResetResult.InvalidPassword();
        }

        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            return PasswordResetResult.ConfirmationMismatch();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByIdAsync(userId.ToString("D"));
        if (user is null || !await userManager.HasPasswordAsync(user))
        {
            return PasswordResetResult.InvalidToken();
        }

        var result = await userManager.ResetPasswordAsync(user, token, newPassword);
        if (result.Succeeded)
        {
            return PasswordResetResult.Succeeded();
        }

        return result.Errors.Any(IsInvalidToken)
            ? PasswordResetResult.InvalidToken()
            : PasswordResetResult.InvalidPassword();
    }

    public async Task<PasswordChangeResult> ChangeAsync(
        Guid userId,
        string? currentPassword,
        string? newPassword,
        string? confirmPassword,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return PasswordChangeResult.UserNotFound();
        }

        if (string.IsNullOrEmpty(currentPassword))
        {
            return PasswordChangeResult.InvalidCurrentPassword();
        }

        if (string.IsNullOrEmpty(newPassword))
        {
            return PasswordChangeResult.InvalidPassword();
        }

        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            return PasswordChangeResult.ConfirmationMismatch();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByIdAsync(userId.ToString("D"));
        if (user is null)
        {
            return PasswordChangeResult.UserNotFound();
        }

        if (!await userManager.HasPasswordAsync(user))
        {
            return PasswordChangeResult.NoLocalPassword();
        }

        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (result.Succeeded)
        {
            return PasswordChangeResult.Succeeded();
        }

        return result.Errors.Any(IsPasswordMismatch)
            ? PasswordChangeResult.InvalidCurrentPassword()
            : PasswordChangeResult.InvalidPassword();
    }

    private static bool IsInvalidToken(IdentityError error) =>
        string.Equals(error.Code, "InvalidToken", StringComparison.Ordinal) ||
        error.Code.Contains("Token", StringComparison.OrdinalIgnoreCase);

    private static bool IsPasswordMismatch(IdentityError error) =>
        string.Equals(error.Code, "PasswordMismatch", StringComparison.Ordinal);
}
