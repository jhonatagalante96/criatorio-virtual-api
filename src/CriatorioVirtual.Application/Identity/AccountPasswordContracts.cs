namespace CriatorioVirtual.Application.Identity;

public interface IAccountPasswordService
{
    Task<PasswordRecoveryRequestResult> RequestResetAsync(
        string? email,
        CancellationToken cancellationToken = default);

    Task<PasswordResetResult> ResetAsync(
        Guid userId,
        string? token,
        string? newPassword,
        string? confirmPassword,
        CancellationToken cancellationToken = default);

    Task<PasswordChangeResult> ChangeAsync(
        Guid userId,
        string? currentPassword,
        string? newPassword,
        string? confirmPassword,
        CancellationToken cancellationToken = default);
}

public enum PasswordRecoveryRequestStatus
{
    Accepted
}

public sealed record PasswordRecoveryRequestResult(PasswordRecoveryRequestStatus Status)
{
    public static PasswordRecoveryRequestResult Accepted() =>
        new(PasswordRecoveryRequestStatus.Accepted);
}

public enum PasswordResetStatus
{
    Succeeded,
    InvalidToken,
    ConfirmationMismatch,
    InvalidPassword
}

public sealed record PasswordResetResult(PasswordResetStatus Status)
{
    public static PasswordResetResult Succeeded() =>
        new(PasswordResetStatus.Succeeded);

    public static PasswordResetResult InvalidToken() =>
        new(PasswordResetStatus.InvalidToken);

    public static PasswordResetResult ConfirmationMismatch() =>
        new(PasswordResetStatus.ConfirmationMismatch);

    public static PasswordResetResult InvalidPassword() =>
        new(PasswordResetStatus.InvalidPassword);
}

public enum PasswordChangeStatus
{
    Succeeded,
    InvalidCurrentPassword,
    ConfirmationMismatch,
    InvalidPassword,
    NoLocalPassword,
    UserNotFound
}

public sealed record PasswordChangeResult(PasswordChangeStatus Status)
{
    public static PasswordChangeResult Succeeded() =>
        new(PasswordChangeStatus.Succeeded);

    public static PasswordChangeResult InvalidCurrentPassword() =>
        new(PasswordChangeStatus.InvalidCurrentPassword);

    public static PasswordChangeResult ConfirmationMismatch() =>
        new(PasswordChangeStatus.ConfirmationMismatch);

    public static PasswordChangeResult InvalidPassword() =>
        new(PasswordChangeStatus.InvalidPassword);

    public static PasswordChangeResult NoLocalPassword() =>
        new(PasswordChangeStatus.NoLocalPassword);

    public static PasswordChangeResult UserNotFound() =>
        new(PasswordChangeStatus.UserNotFound);
}
