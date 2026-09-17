using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;

namespace CriatorioVirtual.Application.Billing;

public sealed class RegularizeBillingPaymentCommand : ICommand<RegularizeBillingPaymentResult>
{
    public RegularizeBillingPaymentCommand(
        Guid userId,
        Guid paymentId,
        Guid idempotencyKey,
        string cardToken)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("An authenticated user is required.", nameof(userId));
        }

        if (paymentId == Guid.Empty)
        {
            throw new ArgumentException("A payment is required.", nameof(paymentId));
        }

        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(cardToken);
        if (cardToken.Trim().Length > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(cardToken), "The card token must not exceed 256 characters.");
        }

        UserId = userId;
        PaymentId = paymentId;
        IdempotencyKey = idempotencyKey;
        CardToken = cardToken.Trim();
    }

    public Guid UserId { get; }

    public Guid PaymentId { get; }

    public Guid IdempotencyKey { get; }

    public string CardToken { get; }

    public override string ToString() =>
        $"{nameof(RegularizeBillingPaymentCommand)} {{ UserId = {UserId}, PaymentId = {PaymentId}, " +
        $"IdempotencyKey = {IdempotencyKey}, CardToken = [redacted] }}";
}

public enum RegularizeBillingPaymentStatus
{
    AwaitingConfirmation,
    PaymentNotFound,
    UserNotFound,
    BreedingFarmNotSelected,
    NotFarmOwner,
    SubscriptionNotRecoverable,
    PaymentNotCurrent,
    PaymentAlreadyConfirmed,
    GatewayPaymentMismatch,
    GatewayDeclined,
    GatewayUnavailable,
    IdempotencyConflict,
    AnotherAttemptInProgress
}

public sealed record RegularizeBillingPaymentResult(
    RegularizeBillingPaymentStatus Status,
    Guid? PaymentId,
    PaymentStatus? PaymentStatus,
    Guid? PaymentAttemptId = null,
    bool StartPaymentAttempt = false)
{
    public static RegularizeBillingPaymentResult Failed(RegularizeBillingPaymentStatus status) =>
        new(status, null, null);

    public static RegularizeBillingPaymentResult Awaiting(
        Guid paymentId,
        PaymentStatus paymentStatus,
        Guid? paymentAttemptId = null,
        bool startPaymentAttempt = false) =>
        new(RegularizeBillingPaymentStatus.AwaitingConfirmation, paymentId, paymentStatus, paymentAttemptId, startPaymentAttempt);
}
