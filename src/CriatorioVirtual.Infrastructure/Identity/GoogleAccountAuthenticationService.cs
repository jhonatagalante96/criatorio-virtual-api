using System.Security.Claims;
using CriatorioVirtual.Application.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CriatorioVirtual.Infrastructure.Identity;

public sealed class GoogleAccountAuthenticationService(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager) : IGoogleAccountAuthenticationService
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
            return GoogleAuthenticationResult.Invalid();
        }

        var email = externalLogin.Principal.FindFirstValue(ClaimTypes.Email)?.Trim();
        if (string.IsNullOrWhiteSpace(email) || !HasVerifiedEmail(externalLogin.Principal))
        {
            return GoogleAuthenticationResult.Invalid();
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
            return GoogleAuthenticationResult.Invalid();
        }

        var existingUser = await userManager.FindByEmailAsync(email);
        if (existingUser is not null)
        {
            // Never link an external identity based only on a matching e-mail.
            // The account owner must explicitly link providers in a future flow.
            return GoogleAuthenticationResult.EmailConflict();
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            LockoutEnabled = true
        };

        var userCreated = false;
        try
        {
            var createResult = await userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                return HasDuplicateIdentityError(createResult.Errors)
                    ? GoogleAuthenticationResult.EmailConflict()
                    : GoogleAuthenticationResult.Invalid();
            }

            userCreated = true;
            cancellationToken.ThrowIfCancellationRequested();
            var addLoginResult = await userManager.AddLoginAsync(user, externalLogin);
            if (!addLoginResult.Succeeded)
            {
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

                await userManager.DeleteAsync(user);
                return GoogleAuthenticationResult.Invalid();
            }

            cancellationToken.ThrowIfCancellationRequested();
            await signInManager.SignInAsync(
                user,
                isPersistent: false,
                authenticationMethod: externalLogin.LoginProvider);
            return GoogleAuthenticationResult.Succeeded();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            if (userCreated)
            {
                await userManager.DeleteAsync(user);
            }

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

            return GoogleAuthenticationResult.EmailConflict();
        }
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
