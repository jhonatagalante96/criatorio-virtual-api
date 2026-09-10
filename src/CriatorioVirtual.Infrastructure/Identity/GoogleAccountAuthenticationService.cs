using System.Security.Claims;
using CriatorioVirtual.Application.Identity;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CriatorioVirtual.Infrastructure.Identity;

public sealed class GoogleAccountAuthenticationService(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    CriatorioVirtualDbContext dbContext,
    ILogger<GoogleAccountAuthenticationService> logger) : IGoogleAccountAuthenticationService
{
    private const string GoogleLoginProvider = "Google";

    public async Task<GoogleAuthenticationResult> CompleteAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var externalLogin = await signInManager.GetExternalLoginInfoAsync();
        if (externalLogin is null ||
            !string.Equals(externalLogin.LoginProvider, GoogleLoginProvider, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(externalLogin.ProviderKey))
        {
            return Reject(GoogleAuthenticationFailureReason.ExternalLoginUnavailable);
        }

        var email = externalLogin.Principal.FindFirstValue(ClaimTypes.Email)?.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            return Reject(GoogleAuthenticationFailureReason.EmailMissing);
        }

        if (!HasVerifiedEmail(externalLogin.Principal))
        {
            return Reject(GoogleAuthenticationFailureReason.EmailUnverified);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var signInResult = await signInManager.ExternalLoginSignInAsync(
            externalLogin.LoginProvider,
            externalLogin.ProviderKey,
            isPersistent: false,
            bypassTwoFactor: true);

        if (signInResult.Succeeded)
        {
            return GoogleAuthenticationResult.Succeeded();
        }

        if (signInResult.IsLockedOut || signInResult.IsNotAllowed)
        {
            return Reject(GoogleAuthenticationFailureReason.AccountUnavailable);
        }

        var existingUser = await userManager.FindByEmailAsync(email);
        if (existingUser is not null)
        {
            // Never link an external identity based only on a matching e-mail.
            // The account owner must explicitly link providers in a future flow.
            return Reject(GoogleAuthenticationFailureReason.EmailConflict);
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            LockoutEnabled = true
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var createResult = await userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                var reason = HasDuplicateIdentityError(createResult.Errors)
                    ? GoogleAuthenticationFailureReason.EmailConflict
                    : GoogleAuthenticationFailureReason.AccountProvisioningFailed;
                logger.LogWarning(
                    "Google authentication rejected while provisioning an account. Reason: {Reason}. IdentityErrorCodes: {IdentityErrorCodes}.",
                    reason,
                    string.Join(",", createResult.Errors.Select(error => error.Code)));
                return Reject(reason, log: false);
            }

            var addLoginResult = await userManager.AddLoginAsync(user, externalLogin);
            if (!addLoginResult.Succeeded)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                dbContext.ChangeTracker.Clear();
                var concurrentlyLinkedUser = await userManager.FindByLoginAsync(
                    externalLogin.LoginProvider,
                    externalLogin.ProviderKey);
                if (concurrentlyLinkedUser is not null)
                {
                    await signInManager.SignInAsync(
                        concurrentlyLinkedUser,
                        isPersistent: false,
                        authenticationMethod: externalLogin.LoginProvider);
                    return GoogleAuthenticationResult.Succeeded();
                }

                return Reject(GoogleAuthenticationFailureReason.AccountProvisioningFailed);
            }

            await transaction.CommitAsync(CancellationToken.None);
            await signInManager.SignInAsync(
                user,
                isPersistent: false,
                authenticationMethod: externalLogin.LoginProvider);
            return GoogleAuthenticationResult.Succeeded();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();

            var concurrentlyLinkedUser = await userManager.FindByLoginAsync(
                externalLogin.LoginProvider,
                externalLogin.ProviderKey);
            if (concurrentlyLinkedUser is not null)
            {
                await signInManager.SignInAsync(
                    concurrentlyLinkedUser,
                    isPersistent: false,
                    authenticationMethod: externalLogin.LoginProvider);
                return GoogleAuthenticationResult.Succeeded();
            }

            return Reject(GoogleAuthenticationFailureReason.EmailConflict);
        }
    }

    private GoogleAuthenticationResult Reject(
        GoogleAuthenticationFailureReason reason,
        bool log = true)
    {
        if (log)
        {
            logger.LogWarning("Google authentication rejected. Reason: {Reason}.", reason);
        }

        return reason == GoogleAuthenticationFailureReason.EmailConflict
            ? GoogleAuthenticationResult.EmailConflict()
            : GoogleAuthenticationResult.Invalid(reason);
    }

    private static bool HasVerifiedEmail(ClaimsPrincipal principal)
    {
        var verifiedClaim = principal.FindFirst("urn:google:email_verified")?.Value ??
            principal.FindFirst("email_verified")?.Value;
        return string.Equals(verifiedClaim, bool.TrueString, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasDuplicateIdentityError(IEnumerable<IdentityError> errors) =>
        errors.Any(error =>
            string.Equals(error.Code, nameof(IdentityErrorDescriber.DuplicateEmail), StringComparison.Ordinal) ||
            string.Equals(error.Code, nameof(IdentityErrorDescriber.DuplicateUserName), StringComparison.Ordinal));

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } ||
        exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
