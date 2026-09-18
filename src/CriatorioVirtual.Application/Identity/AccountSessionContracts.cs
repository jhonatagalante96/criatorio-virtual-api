namespace CriatorioVirtual.Application.Identity;

public interface IAccountSessionService
{
    Task<AccountLoginResult> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);

    Task<AccountSession?> GetCurrentAsync(Guid userId, CancellationToken cancellationToken = default);

    Task LogoutAsync(CancellationToken cancellationToken = default);
}

public enum AccountLoginStatus
{
    Succeeded,
    EmailUnconfirmed,
    Invalid,
    LockedOut
}

public sealed record AccountLoginResult(AccountLoginStatus Status)
{
    public static AccountLoginResult Succeeded() => new(AccountLoginStatus.Succeeded);

    public static AccountLoginResult EmailUnconfirmed() => new(AccountLoginStatus.EmailUnconfirmed);

    public static AccountLoginResult Invalid() => new(AccountLoginStatus.Invalid);

    public static AccountLoginResult LockedOut() => new(AccountLoginStatus.LockedOut);
}

public sealed record AccountSession(Guid UserId, string Email, bool EmailConfirmed, bool HasAvatar);
