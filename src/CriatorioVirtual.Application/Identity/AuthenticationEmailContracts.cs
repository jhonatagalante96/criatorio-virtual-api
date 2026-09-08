namespace CriatorioVirtual.Application.Identity;

public enum AuthenticationEmailKind
{
    Confirmation,
    PasswordReset
}

public sealed record AuthenticationEmailMessage(
    AuthenticationEmailKind Kind,
    string Recipient,
    Uri ActionUrl);

public enum AuthenticationEmailDeliveryStatus
{
    Delivered,
    Failed
}

public sealed record AuthenticationEmailDeliveryResult(AuthenticationEmailDeliveryStatus Status)
{
    public bool Succeeded => Status == AuthenticationEmailDeliveryStatus.Delivered;

    public static AuthenticationEmailDeliveryResult Delivered() =>
        new(AuthenticationEmailDeliveryStatus.Delivered);

    public static AuthenticationEmailDeliveryResult Failed() =>
        new(AuthenticationEmailDeliveryStatus.Failed);
}

public interface IAuthenticationEmailSender
{
    Task<AuthenticationEmailDeliveryResult> SendAsync(
        AuthenticationEmailMessage message,
        CancellationToken cancellationToken = default);
}

public interface IAuthenticationEmailLinkBuilder
{
    Uri Build(AuthenticationEmailKind kind, Guid userId, string token);
}

public enum EmailConfirmationStatus
{
    Confirmed,
    AlreadyConfirmed,
    InvalidToken
}

public sealed record EmailConfirmationResult(EmailConfirmationStatus Status)
{
    public static EmailConfirmationResult Confirmed() =>
        new(EmailConfirmationStatus.Confirmed);

    public static EmailConfirmationResult AlreadyConfirmed() =>
        new(EmailConfirmationStatus.AlreadyConfirmed);

    public static EmailConfirmationResult InvalidToken() =>
        new(EmailConfirmationStatus.InvalidToken);
}

public enum EmailConfirmationResendStatus
{
    Accepted,
    RateLimited,
    DeliveryFailed
}

public sealed record EmailConfirmationResendResult(EmailConfirmationResendStatus Status)
{
    public static EmailConfirmationResendResult Accepted() =>
        new(EmailConfirmationResendStatus.Accepted);

    public static EmailConfirmationResendResult RateLimited() =>
        new(EmailConfirmationResendStatus.RateLimited);

    public static EmailConfirmationResendResult DeliveryFailed() =>
        new(EmailConfirmationResendStatus.DeliveryFailed);
}

public interface IAccountEmailConfirmationService
{
    Task<EmailConfirmationResult> ConfirmAsync(
        Guid userId,
        string? token,
        CancellationToken cancellationToken = default);

    Task<EmailConfirmationResendResult> ResendAsync(
        string? email,
        CancellationToken cancellationToken = default);
}
