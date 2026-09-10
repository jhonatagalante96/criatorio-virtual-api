namespace CriatorioVirtual.Application.Identity;

public interface IGoogleAccountAuthenticationService
{
    Task<GoogleAuthenticationResult> CompleteAsync(CancellationToken cancellationToken = default);
}

public enum GoogleAuthenticationStatus
{
    Succeeded,
    Invalid,
    EmailConflict
}

public enum GoogleAuthenticationFailureReason
{
    ExternalLoginUnavailable,
    EmailMissing,
    EmailUnverified,
    AccountUnavailable,
    AccountProvisioningFailed,
    EmailConflict
}

public sealed record GoogleAuthenticationResult(
    GoogleAuthenticationStatus Status,
    GoogleAuthenticationFailureReason? FailureReason = null)
{
    public static GoogleAuthenticationResult Succeeded() => new(GoogleAuthenticationStatus.Succeeded);

    public static GoogleAuthenticationResult Invalid(
        GoogleAuthenticationFailureReason reason = GoogleAuthenticationFailureReason.ExternalLoginUnavailable) =>
        new(GoogleAuthenticationStatus.Invalid, reason);

    public static GoogleAuthenticationResult EmailConflict() =>
        new(GoogleAuthenticationStatus.EmailConflict, GoogleAuthenticationFailureReason.EmailConflict);
}
