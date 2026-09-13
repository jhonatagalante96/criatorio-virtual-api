namespace CriatorioVirtual.Application.Identity;

public interface IAccountPasskeyAuthenticationService
{
    Task<PasskeyRequestOptionsResult> CreateRequestOptionsAsync(
        CancellationToken cancellationToken = default);

    Task<PasskeyAuthenticationResult> AuthenticateAsync(
        string? credentialJson,
        CancellationToken cancellationToken = default);
}

public enum PasskeyAuthenticationStatus
{
    Succeeded,
    Invalid,
    NotAllowed,
    Unsupported
}

public sealed record PasskeyRequestOptionsResult(
    PasskeyAuthenticationStatus Status,
    string? OptionsJson)
{
    public static PasskeyRequestOptionsResult Succeeded(string optionsJson) =>
        new(PasskeyAuthenticationStatus.Succeeded, optionsJson);

    public static PasskeyRequestOptionsResult Failure(PasskeyAuthenticationStatus status) =>
        new(status, null);
}

public sealed record PasskeyAuthenticationResult(PasskeyAuthenticationStatus Status)
{
    public static PasskeyAuthenticationResult Succeeded() =>
        new(PasskeyAuthenticationStatus.Succeeded);

    public static PasskeyAuthenticationResult Failure(PasskeyAuthenticationStatus status) =>
        new(status);
}
