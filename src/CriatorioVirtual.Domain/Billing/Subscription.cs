using CriatorioVirtual.Domain.Primitives;

namespace CriatorioVirtual.Domain.Billing;

public sealed class Subscription : Entity
{
    public const int TrialDurationDays = 7;
    public const int GracePeriodDurationDays = 7;
    public const int PlanCodeMaxLength = 64;
    public const int GatewayIdMaxLength = 128;

    private Subscription()
        : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
    {
    }

    public Subscription(
        Guid id,
        Guid breedingFarmId,
        string planCode,
        BillingCycle billingCycle,
        DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
        if (breedingFarmId == Guid.Empty)
        {
            throw new ArgumentException("A breeding farm is required.", nameof(breedingFarmId));
        }

        if (!Enum.IsDefined(billingCycle))
        {
            throw new ArgumentOutOfRangeException(nameof(billingCycle), billingCycle, "The billing cycle is not supported.");
        }

        BreedingFarmId = breedingFarmId;
        PlanCode = Require(planCode, PlanCodeMaxLength, nameof(planCode));
        BillingCycle = billingCycle;
        Status = SubscriptionStatus.PendingSubscription;
    }

    public Guid BreedingFarmId { get; private set; }

    public string PlanCode { get; private set; } = null!;

    public BillingCycle BillingCycle { get; private set; }

    public SubscriptionStatus Status { get; private set; }

    public string? GatewayCustomerId { get; private set; }

    public string? GatewaySubscriptionId { get; private set; }

    public DateTimeOffset? TrialStartedAtUtc { get; private set; }

    public DateTimeOffset? TrialEndsAtUtc { get; private set; }

    public DateTimeOffset? NextChargeDueAtUtc { get; private set; }

    public DateTimeOffset? GracePeriodStartedAtUtc { get; private set; }

    public DateTimeOffset? GracePeriodEndsAtUtc { get; private set; }

    public void ConfirmRecurringSubscription(
        string gatewayCustomerId,
        string gatewaySubscriptionId,
        DateTimeOffset confirmedAtUtc)
    {
        EnsureUtc(confirmedAtUtc, nameof(confirmedAtUtc));
        EnsureStatus(SubscriptionStatus.PendingSubscription);

        GatewayCustomerId = Require(gatewayCustomerId, GatewayIdMaxLength, nameof(gatewayCustomerId));
        GatewaySubscriptionId = Require(gatewaySubscriptionId, GatewayIdMaxLength, nameof(gatewaySubscriptionId));
        TrialStartedAtUtc = confirmedAtUtc;
        TrialEndsAtUtc = confirmedAtUtc.AddDays(TrialDurationDays);
        NextChargeDueAtUtc = TrialEndsAtUtc;
        Status = SubscriptionStatus.Trial;
        Touch(confirmedAtUtc);
    }

    public void ConfirmFirstPayment(DateTimeOffset paidAtUtc)
    {
        EnsureUtc(paidAtUtc, nameof(paidAtUtc));
        EnsureStatus(SubscriptionStatus.Trial);

        if (TrialEndsAtUtc is null || paidAtUtc < TrialEndsAtUtc.Value)
        {
            throw new InvalidOperationException("The first charge cannot be confirmed before the trial ends.");
        }

        Status = SubscriptionStatus.Active;
        NextChargeDueAtUtc = BillingCycle == BillingCycle.Monthly
            ? TrialEndsAtUtc.Value.AddMonths(1)
            : TrialEndsAtUtc.Value.AddYears(1);
        Touch(paidAtUtc);
    }

    public void FailFirstPayment(DateTimeOffset failedAtUtc)
    {
        EnsureUtc(failedAtUtc, nameof(failedAtUtc));
        EnsureStatus(SubscriptionStatus.Trial);

        if (TrialEndsAtUtc is null || failedAtUtc < TrialEndsAtUtc.Value)
        {
            throw new InvalidOperationException("The first charge cannot fail before its due date.");
        }

        Status = SubscriptionStatus.GracePeriod;
        GracePeriodStartedAtUtc = failedAtUtc;
        GracePeriodEndsAtUtc = failedAtUtc.AddDays(GracePeriodDurationDays);
        Touch(failedAtUtc);
    }

    private void EnsureStatus(SubscriptionStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"A subscription in {Status} cannot perform this transition.");
        }
    }

    private static string Require(string value, int maxLength, string parameterName)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is null || normalized.Length > maxLength)
        {
            throw new ArgumentException($"A non-empty value of at most {maxLength} characters is required.", parameterName);
        }

        return normalized;
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamps must be expressed in UTC.", parameterName);
        }
    }
}
