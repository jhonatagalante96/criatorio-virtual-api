using CriatorioVirtual.Domain.Primitives;

namespace CriatorioVirtual.Domain.Billing;

public sealed class Subscription : Entity
{
    public const int TrialDurationDays = 7;
    public const int GracePeriodDurationDays = 7;
    public const int PlanCodeMaxLength = 64;
    public const int GatewayIdMaxLength = 128;
    public const decimal MaximumAgreedAmount = 9_999_999_999_999_999.99m;

    private Subscription()
        : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
    {
    }

    public Subscription(
        Guid id,
        Guid breedingFarmId,
        string planCode,
        BillingCycle billingCycle,
        DateTimeOffset createdAtUtc,
        decimal? agreedAmount = null)
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

        if (agreedAmount is not null &&
            (agreedAmount <= 0 ||
             agreedAmount > MaximumAgreedAmount ||
             decimal.Round(agreedAmount.Value, 2, MidpointRounding.ToEven) != agreedAmount.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(agreedAmount), agreedAmount, "The agreed amount must be positive and use at most two decimal places.");
        }

        BreedingFarmId = breedingFarmId;
        PlanCode = Require(planCode, PlanCodeMaxLength, nameof(planCode));
        BillingCycle = billingCycle;
        AgreedAmount = agreedAmount;
        Status = SubscriptionStatus.PendingSubscription;
    }

    public Guid BreedingFarmId { get; private set; }

    public string PlanCode { get; private set; } = null!;

    public BillingCycle BillingCycle { get; private set; }

    public decimal? AgreedAmount { get; private set; }

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

    public void ConfirmPayment(DateTimeOffset paidAtUtc)
    {
        EnsureUtc(paidAtUtc, nameof(paidAtUtc));
        var chargeDueAtUtc = NextChargeDueAtUtc;
        if (chargeDueAtUtc is null || paidAtUtc < chargeDueAtUtc.Value)
        {
            throw new InvalidOperationException("A charge cannot be confirmed before it is due.");
        }

        if (Status is SubscriptionStatus.GracePeriod or SubscriptionStatus.Blocked)
        {
            if (GracePeriodStartedAtUtc is null || paidAtUtc < GracePeriodStartedAtUtc.Value)
            {
                throw new InvalidOperationException("A failed charge can only be recovered by a later payment event.");
            }
        }
        else if (Status is not (SubscriptionStatus.Trial or SubscriptionStatus.Active))
        {
            throw new InvalidOperationException($"A subscription in {Status} cannot confirm a charge.");
        }

        Status = SubscriptionStatus.Active;
        GracePeriodStartedAtUtc = null;
        GracePeriodEndsAtUtc = null;
        NextChargeDueAtUtc = BillingCycle == BillingCycle.Monthly
            ? chargeDueAtUtc.Value.AddMonths(1)
            : chargeDueAtUtc.Value.AddYears(1);
        Touch(paidAtUtc);
    }

    public void StartGracePeriod(DateTimeOffset failedAtUtc)
    {
        EnsureUtc(failedAtUtc, nameof(failedAtUtc));
        if (Status is not (SubscriptionStatus.Trial or SubscriptionStatus.Active))
        {
            throw new InvalidOperationException($"A subscription in {Status} cannot start a grace period.");
        }

        if (NextChargeDueAtUtc is null || failedAtUtc < NextChargeDueAtUtc.Value)
        {
            throw new InvalidOperationException("A charge cannot fail before its due date.");
        }

        Status = SubscriptionStatus.GracePeriod;
        GracePeriodStartedAtUtc = failedAtUtc;
        GracePeriodEndsAtUtc = failedAtUtc.AddDays(GracePeriodDurationDays);
        Touch(failedAtUtc);
    }

    public bool TryBlockAfterGracePeriodExpiration(DateTimeOffset blockedAtUtc)
    {
        EnsureUtc(blockedAtUtc, nameof(blockedAtUtc));
        if (Status != SubscriptionStatus.GracePeriod ||
            GracePeriodEndsAtUtc is not { } gracePeriodEndsAtUtc ||
            blockedAtUtc < gracePeriodEndsAtUtc)
        {
            return false;
        }

        Status = SubscriptionStatus.Blocked;
        Touch(blockedAtUtc);
        return true;
    }

    public void Cancel(DateTimeOffset cancelledAtUtc)
    {
        EnsureUtc(cancelledAtUtc, nameof(cancelledAtUtc));
        if (Status == SubscriptionStatus.Cancelled)
        {
            return;
        }

        if (Status is not (SubscriptionStatus.Trial or
            SubscriptionStatus.Active or
            SubscriptionStatus.GracePeriod or
            SubscriptionStatus.Blocked))
        {
            throw new InvalidOperationException($"A subscription in {Status} cannot be cancelled.");
        }

        Status = SubscriptionStatus.Cancelled;
        NextChargeDueAtUtc = null;
        Touch(cancelledAtUtc);
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
