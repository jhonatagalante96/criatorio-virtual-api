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

public sealed record GoogleAuthenticationResult(GoogleAuthenticationStatus Status)
{
    public static GoogleAuthenticationResult Succeeded() => new(GoogleAuthenticationStatus.Succeeded);

    public static GoogleAuthenticationResult Invalid() => new(GoogleAuthenticationStatus.Invalid);

    public static GoogleAuthenticationResult EmailConflict() => new(GoogleAuthenticationStatus.EmailConflict);
}
