using CriatorioVirtual.Domain.Primitives;

namespace CriatorioVirtual.Domain.Billing;

public sealed class Payment : Entity
{
    public const int CurrencyCodeLength = 3;

    private Payment()
        : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
    {
    }

    public Payment(
        Guid id,
        Guid breedingFarmId,
        Guid subscriptionId,
        string gatewayPaymentId,
        decimal amount,
        string currencyCode,
        DateTimeOffset dueAtUtc,
        DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
        if (breedingFarmId == Guid.Empty)
        {
            throw new ArgumentException("A breeding farm is required.", nameof(breedingFarmId));
        }

        if (subscriptionId == Guid.Empty)
        {
            throw new ArgumentException("A subscription is required.", nameof(subscriptionId));
        }

        if (string.IsNullOrWhiteSpace(gatewayPaymentId) || gatewayPaymentId.Trim().Length > Subscription.GatewayIdMaxLength)
        {
            throw new ArgumentException("A gateway payment identifier is required.", nameof(gatewayPaymentId));
        }

        if (amount <= 0 || decimal.Round(amount, 2, MidpointRounding.ToEven) != amount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "A payment amount must be positive and use at most two decimal places.");
        }

        if (currencyCode is null || currencyCode.Trim().Length != CurrencyCodeLength || currencyCode.Trim().Any(character => !char.IsAsciiLetter(character)))
        {
            throw new ArgumentException("The currency must be a three-letter ISO code.", nameof(currencyCode));
        }

        EnsureUtc(dueAtUtc, nameof(dueAtUtc));

        BreedingFarmId = breedingFarmId;
        SubscriptionId = subscriptionId;
        GatewayPaymentId = gatewayPaymentId.Trim();
        Amount = amount;
        CurrencyCode = currencyCode.Trim().ToUpperInvariant();
        DueAtUtc = dueAtUtc;
        Status = PaymentStatus.Pending;
    }

    public Guid BreedingFarmId { get; private set; }

    public Guid SubscriptionId { get; private set; }

    public string GatewayPaymentId { get; private set; } = null!;

    public decimal Amount { get; private set; }

    public string CurrencyCode { get; private set; } = null!;

    public DateTimeOffset DueAtUtc { get; private set; }

    public PaymentStatus Status { get; private set; }

    public DateTimeOffset? PaidAtUtc { get; private set; }

    public void Confirm(DateTimeOffset paidAtUtc)
    {
        EnsureUtc(paidAtUtc, nameof(paidAtUtc));

        if (Status == PaymentStatus.Confirmed)
        {
            if (PaidAtUtc == paidAtUtc)
            {
                return;
            }

            throw new InvalidOperationException("A confirmed payment cannot be confirmed with a different timestamp.");
        }

        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException($"A payment in {Status} cannot be confirmed.");
        }

        Status = PaymentStatus.Confirmed;
        PaidAtUtc = paidAtUtc;
        Touch(paidAtUtc);
    }

    public void Fail(DateTimeOffset failedAtUtc)
    {
        EnsureUtc(failedAtUtc, nameof(failedAtUtc));

        if (Status == PaymentStatus.Failed)
        {
            return;
        }

        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException($"A payment in {Status} cannot fail.");
        }

        Status = PaymentStatus.Failed;
        Touch(failedAtUtc);
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamps must be expressed in UTC.", parameterName);
        }
    }
}
