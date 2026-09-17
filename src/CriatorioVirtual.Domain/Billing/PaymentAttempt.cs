using CriatorioVirtual.Domain.Primitives;

namespace CriatorioVirtual.Domain.Billing;

public sealed class PaymentAttempt : Entity
{
    public const int RequestFingerprintLength = 64;

    private PaymentAttempt()
        : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
    {
    }

    public PaymentAttempt(
        Guid id,
        Guid paymentId,
        Guid idempotencyKey,
        string requestFingerprint,
        DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
        if (paymentId == Guid.Empty)
        {
            throw new ArgumentException("A payment is required.", nameof(paymentId));
        }

        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        if (requestFingerprint is null ||
            requestFingerprint.Length != RequestFingerprintLength ||
            requestFingerprint.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException("A SHA-256 request fingerprint is required.", nameof(requestFingerprint));
        }

        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        PaymentId = paymentId;
        IdempotencyKey = idempotencyKey;
        RequestFingerprint = requestFingerprint.ToUpperInvariant();
        Status = PaymentAttemptStatus.Processing;
    }

    public Guid PaymentId { get; private set; }

    public Guid IdempotencyKey { get; private set; }

    public string RequestFingerprint { get; private set; } = null!;

    public PaymentAttemptStatus Status { get; private set; }

    public void MarkAwaitingConfirmation(DateTimeOffset occurredAtUtc)
    {
        Transition(PaymentAttemptStatus.AwaitingConfirmation, occurredAtUtc);
    }

    public void MarkFailed(DateTimeOffset occurredAtUtc)
    {
        Transition(PaymentAttemptStatus.Failed, occurredAtUtc);
    }

    public void MarkOutcomeUnknown(DateTimeOffset occurredAtUtc)
    {
        Transition(PaymentAttemptStatus.OutcomeUnknown, occurredAtUtc);
    }

    private void Transition(PaymentAttemptStatus status, DateTimeOffset occurredAtUtc)
    {
        EnsureUtc(occurredAtUtc, nameof(occurredAtUtc));
        var validTransition = Status switch
        {
            PaymentAttemptStatus.Processing => status is PaymentAttemptStatus.AwaitingConfirmation or
                PaymentAttemptStatus.Failed or PaymentAttemptStatus.OutcomeUnknown,
            PaymentAttemptStatus.OutcomeUnknown => status is PaymentAttemptStatus.AwaitingConfirmation or
                PaymentAttemptStatus.Failed or PaymentAttemptStatus.OutcomeUnknown,
            PaymentAttemptStatus.AwaitingConfirmation => status == PaymentAttemptStatus.AwaitingConfirmation,
            PaymentAttemptStatus.Failed => status == PaymentAttemptStatus.Failed,
            _ => false
        };
        if (!validTransition)
        {
            throw new InvalidOperationException($"A payment attempt in {Status} cannot transition to {status}.");
        }

        if (Status == status)
        {
            return;
        }

        Status = status;
        Touch(occurredAtUtc);
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamps must be expressed in UTC.", parameterName);
        }
    }
}
