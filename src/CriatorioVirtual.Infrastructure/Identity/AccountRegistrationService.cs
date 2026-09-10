using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using CriatorioVirtual.Application.Identity;

namespace CriatorioVirtual.Infrastructure.Identity;

public enum AccountRegistrationStatus
{
    Created,
    Invalid,
    Duplicate,
    EmailConfirmationRequired,
    EmailDeliveryFailed
}

public sealed record AccountRegistrationResult(
    AccountRegistrationStatus Status,
    ApplicationUser? User,
    IReadOnlyCollection<IdentityError> Errors)
{
    public static AccountRegistrationResult Created(ApplicationUser user) =>
        new(AccountRegistrationStatus.Created, user, Array.Empty<IdentityError>());

    public static AccountRegistrationResult Invalid(IEnumerable<IdentityError> errors) =>
        new(AccountRegistrationStatus.Invalid, null, errors.ToArray());

    public static AccountRegistrationResult Duplicate() =>
        new(AccountRegistrationStatus.Duplicate, null, Array.Empty<IdentityError>());

    public static AccountRegistrationResult EmailConfirmationRequired() =>
        new(AccountRegistrationStatus.EmailConfirmationRequired, null, Array.Empty<IdentityError>());

    public static AccountRegistrationResult EmailDeliveryFailed() =>
        new(AccountRegistrationStatus.EmailDeliveryFailed, null, Array.Empty<IdentityError>());
}

public sealed class AccountRegistrationService(
    UserManager<ApplicationUser> userManager,
    IAuthenticationEmailLinkBuilder emailLinkBuilder,
    IAuthenticationEmailSender emailSender,
    ILogger<AccountRegistrationService> logger)
{
    public async Task<AccountRegistrationResult> RegisterAsync(
        string? email,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return AccountRegistrationResult.Invalid([
                new IdentityError { Code = "InvalidEmail", Description = "A valid email address is required." }]);
        }

        if (string.IsNullOrEmpty(password))
        {
            return AccountRegistrationResult.Invalid([
                new IdentityError { Code = "PasswordRequired", Description = "A password is required." }]);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var normalizedEmail = email.Trim();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = normalizedEmail,
            Email = normalizedEmail,
            LockoutEnabled = true
        };

        try
        {
            var result = await userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                return await SendConfirmationEmailAndRollbackOnFailureAsync(user, cancellationToken);
            }

            if (!result.Errors.Any(IsDuplicateError))
            {
                return AccountRegistrationResult.Invalid(result.Errors);
            }

            return await ExistingEmailResultAsync(normalizedEmail);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Identity validation and the database constraint both participate in
            // duplicate protection. The latter closes the race between two requests.
            return await ExistingEmailResultAsync(normalizedEmail);
        }
    }

    private async Task<AccountRegistrationResult> SendConfirmationEmailAndRollbackOnFailureAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        AccountRegistrationResult result;
        try
        {
            result = await SendConfirmationEmailAsync(user, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await RollbackCreatedUserAsync(user);
            throw;
        }

        if (result.Status != AccountRegistrationStatus.Created)
        {
            await RollbackCreatedUserAsync(user);
        }

        return result;
    }

    private async Task RollbackCreatedUserAsync(ApplicationUser user)
    {
        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            logger.LogError(
                "Unable to roll back account registration for user {UserId}. Identity errors: {Errors}.",
                user.Id,
                string.Join("; ", result.Errors.Select(error => error.Code)));
        }
    }

    private async Task<AccountRegistrationResult> ExistingEmailResultAsync(string email)
    {
        var existingUser = await userManager.FindByEmailAsync(email);
        return existingUser is { EmailConfirmed: false }
            ? AccountRegistrationResult.EmailConfirmationRequired()
            : AccountRegistrationResult.Duplicate();
    }

    private async Task<AccountRegistrationResult> SendConfirmationEmailAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        try
        {
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            var actionUrl = emailLinkBuilder.Build(
                AuthenticationEmailKind.Confirmation,
                user.Id,
                token);
            var delivery = await emailSender.SendAsync(
                new AuthenticationEmailMessage(AuthenticationEmailKind.Confirmation, user.Email!, actionUrl),
                cancellationToken);

            return delivery.Succeeded
                ? AccountRegistrationResult.Created(user)
                : AccountRegistrationResult.EmailDeliveryFailed();
        }
        catch (InvalidOperationException)
        {
            // Token-provider and delivery failures must remain retryable through
            // the future resend flow without exposing a confirmation token.
            return AccountRegistrationResult.EmailDeliveryFailed();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Delivery adapters must not turn a provider outage into a response
            // that contains the action URL or its token. Cancellation is allowed
            // to propagate and is not handled by this filter.
            return AccountRegistrationResult.EmailDeliveryFailed();
        }
    }

    private static bool IsDuplicateError(IdentityError error) =>
        string.Equals(error.Code, nameof(IdentityErrorDescriber.DuplicateEmail), StringComparison.Ordinal) ||
        string.Equals(error.Code, nameof(IdentityErrorDescriber.DuplicateUserName), StringComparison.Ordinal);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } ||
        exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
