using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CriatorioVirtual.Infrastructure.Identity;

public enum AccountRegistrationStatus
{
    Created,
    Invalid,
    Duplicate
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
}

public sealed class AccountRegistrationService(UserManager<ApplicationUser> userManager)
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
                return AccountRegistrationResult.Created(user);
            }

            return result.Errors.Any(IsDuplicateError)
                ? AccountRegistrationResult.Duplicate()
                : AccountRegistrationResult.Invalid(result.Errors);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Identity validation and the database constraint both participate in
            // duplicate protection. The latter closes the race between two requests.
            return AccountRegistrationResult.Duplicate();
        }
    }

    private static bool IsDuplicateError(IdentityError error) =>
        string.Equals(error.Code, nameof(IdentityErrorDescriber.DuplicateEmail), StringComparison.Ordinal) ||
        string.Equals(error.Code, nameof(IdentityErrorDescriber.DuplicateUserName), StringComparison.Ordinal);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } ||
        exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
