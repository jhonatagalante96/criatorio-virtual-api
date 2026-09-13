namespace CriatorioVirtual.Application.Identity;

public interface IAccountPasskeyService
{
    Task<PasskeyCreationOptionsResult> CreateRegistrationOptionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<PasskeyOperationResult> RegisterAsync(
        Guid userId,
        string? credentialJson,
        CancellationToken cancellationToken = default);

    Task<PasskeyListResult> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<PasskeyOperationResult> RenameAsync(
        Guid userId,
        string? credentialId,
        string? name,
        CancellationToken cancellationToken = default);

    Task<PasskeyOperationResult> RemoveAsync(
        Guid userId,
        string? credentialId,
        CancellationToken cancellationToken = default);
}

public enum PasskeyOperationStatus
{
    Succeeded,
    UserNotFound,
    InvalidCredential,
    InvalidCredentialId,
    InvalidName,
    PasskeyNotFound,
    Conflict,
    Unsupported,
    Failed
}

public sealed record PasskeyCreationOptionsResult(
    PasskeyOperationStatus Status,
    string? OptionsJson)
{
    public static PasskeyCreationOptionsResult Succeeded(string optionsJson) =>
        new(PasskeyOperationStatus.Succeeded, optionsJson);

    public static PasskeyCreationOptionsResult Failure(PasskeyOperationStatus status) =>
        new(status, null);
}

public sealed record PasskeyOperationResult(PasskeyOperationStatus Status)
{
    public static PasskeyOperationResult Succeeded() =>
        new(PasskeyOperationStatus.Succeeded);

    public static PasskeyOperationResult Failure(PasskeyOperationStatus status) =>
        new(status);
}

public sealed record PasskeyListResult(
    PasskeyOperationStatus Status,
    IReadOnlyCollection<AccountPasskey> Passkeys)
{
    public static PasskeyListResult Succeeded(IEnumerable<AccountPasskey> passkeys) =>
        new(PasskeyOperationStatus.Succeeded, passkeys.ToArray());

    public static PasskeyListResult Failure(PasskeyOperationStatus status) =>
        new(status, Array.Empty<AccountPasskey>());
}

public sealed record AccountPasskey(
    string CredentialId,
    string? Name,
    DateTimeOffset CreatedAt,
    IReadOnlyCollection<string> Transports,
    bool IsUserVerified,
    bool IsBackupEligible,
    bool IsBackedUp);
