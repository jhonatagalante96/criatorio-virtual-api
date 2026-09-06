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
