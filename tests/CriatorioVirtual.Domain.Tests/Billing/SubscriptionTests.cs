using CriatorioVirtual.Domain.Billing;
using Xunit;

namespace CriatorioVirtual.Domain.Tests.Billing;

public sealed class SubscriptionTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(BillingCycle.Monthly)]
    [InlineData(BillingCycle.Annual)]
    public void ConfirmRecurringSubscription_StartsSevenDayTrialAndSchedulesFirstCharge(
        BillingCycle cycle)
    {
        var subscription = CreateSubscription(cycle);
        var confirmedAt = CreatedAtUtc.AddDays(2);

        subscription.ConfirmRecurringSubscription("customer-123", "subscription-456", confirmedAt);

        Assert.Equal(SubscriptionStatus.Trial, subscription.Status);
        Assert.Equal(confirmedAt, subscription.TrialStartedAtUtc);
        Assert.Equal(confirmedAt.AddDays(7), subscription.TrialEndsAtUtc);
        Assert.Equal(subscription.TrialEndsAtUtc, subscription.NextChargeDueAtUtc);
        Assert.Equal("customer-123", subscription.GatewayCustomerId);
        Assert.Equal("subscription-456", subscription.GatewaySubscriptionId);
        Assert.Equal(cycle, subscription.BillingCycle);

        var firstChargeDueAt = subscription.NextChargeDueAtUtc!.Value;
        subscription.ConfirmFirstPayment(firstChargeDueAt);

        var expectedNextChargeDueAt = cycle == BillingCycle.Monthly
            ? firstChargeDueAt.AddMonths(1)
            : firstChargeDueAt.AddYears(1);
        Assert.Equal(expectedNextChargeDueAt, subscription.NextChargeDueAtUtc);
    }

    [Fact]
    public void ConfirmRecurringSubscription_CannotStartTrialTwice()
    {
        var subscription = CreateSubscription();
        subscription.ConfirmRecurringSubscription("customer-123", "subscription-456", CreatedAtUtc.AddDays(2));

        Assert.Throws<InvalidOperationException>(() => subscription.ConfirmRecurringSubscription(
            "customer-789",
            "subscription-999",
            CreatedAtUtc.AddDays(3)));
    }

    [Fact]
    public void ConfirmRecurringSubscription_RequiresGatewayConfirmationBeforeTrial()
    {
        var subscription = CreateSubscription();

        Assert.Throws<ArgumentException>(() => subscription.ConfirmRecurringSubscription(
            " ",
            "subscription-456",
            CreatedAtUtc.AddDays(2)));
        Assert.Equal(SubscriptionStatus.PendingSubscription, subscription.Status);
        Assert.Null(subscription.TrialStartedAtUtc);
        Assert.Null(subscription.NextChargeDueAtUtc);
    }

    [Fact]
    public void ConfirmFirstPayment_ActivatesSubscriptionAndAdvancesByBillingCycle()
    {
        var subscription = CreateSubscription(BillingCycle.Monthly);
        subscription.ConfirmRecurringSubscription("customer-123", "subscription-456", CreatedAtUtc);

        subscription.ConfirmFirstPayment(subscription.TrialEndsAtUtc!.Value);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(subscription.TrialEndsAtUtc.Value.AddMonths(1), subscription.NextChargeDueAtUtc);
    }

    [Fact]
    public void ConfirmFirstPayment_RejectsPaymentBeforeTrialEnds()
    {
        var subscription = CreateSubscription();
        subscription.ConfirmRecurringSubscription("customer-123", "subscription-456", CreatedAtUtc);

        Assert.Throws<InvalidOperationException>(() => subscription.ConfirmFirstPayment(CreatedAtUtc.AddDays(6)));
        Assert.Equal(SubscriptionStatus.Trial, subscription.Status);
    }

    [Fact]
    public void FailFirstPayment_StartsSevenDayGracePeriodFromFailure()
    {
        var subscription = CreateSubscription();
        subscription.ConfirmRecurringSubscription("customer-123", "subscription-456", CreatedAtUtc);
        var failedAt = subscription.TrialEndsAtUtc!.Value.AddMinutes(5);

        subscription.FailFirstPayment(failedAt);

        Assert.Equal(SubscriptionStatus.GracePeriod, subscription.Status);
        Assert.Equal(failedAt, subscription.GracePeriodStartedAtUtc);
        Assert.Equal(failedAt.AddDays(Subscription.GracePeriodDurationDays), subscription.GracePeriodEndsAtUtc);
    }

    [Fact]
    public void ConfirmFirstPayment_RecoversGracePeriodOnlyFromANewerPaymentEvent()
    {
        var subscription = CreateSubscription();
        subscription.ConfirmRecurringSubscription("customer-123", "subscription-456", CreatedAtUtc);
        var failedAt = subscription.TrialEndsAtUtc!.Value.AddMinutes(5);
        subscription.FailFirstPayment(failedAt);

        Assert.Throws<InvalidOperationException>(() => subscription.ConfirmFirstPayment(failedAt.AddMinutes(-1)));
        Assert.Equal(SubscriptionStatus.GracePeriod, subscription.Status);

        var paidAt = failedAt.AddHours(2);
        subscription.ConfirmFirstPayment(paidAt);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Null(subscription.GracePeriodStartedAtUtc);
        Assert.Null(subscription.GracePeriodEndsAtUtc);
        Assert.Equal(subscription.TrialEndsAtUtc.Value.AddMonths(1), subscription.NextChargeDueAtUtc);
        Assert.Equal(paidAt, subscription.UpdatedAtUtc);
    }

    [Fact]
    public void FailFirstPayment_RejectsFailureBeforeTheFirstChargeIsDue()
    {
        var subscription = CreateSubscription();
        subscription.ConfirmRecurringSubscription("customer-123", "subscription-456", CreatedAtUtc);

        Assert.Throws<InvalidOperationException>(() => subscription.FailFirstPayment(CreatedAtUtc.AddDays(6)));
        Assert.Equal(SubscriptionStatus.Trial, subscription.Status);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Trial)]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.GracePeriod)]
    public void Cancel_StopsFutureChargesAndPreservesSubscriptionHistory(SubscriptionStatus status)
    {
        var subscription = CreateSubscription();
        subscription.ConfirmRecurringSubscription("customer-123", "subscription-456", CreatedAtUtc);
        if (status == SubscriptionStatus.Active)
        {
            subscription.ConfirmFirstPayment(subscription.TrialEndsAtUtc!.Value);
        }
        else if (status == SubscriptionStatus.GracePeriod)
        {
            subscription.FailFirstPayment(subscription.TrialEndsAtUtc!.Value);
        }

        var cancelledAt = CreatedAtUtc.AddDays(10);
        var gracePeriodStartedAt = subscription.GracePeriodStartedAtUtc;
        var gracePeriodEndsAt = subscription.GracePeriodEndsAtUtc;
        subscription.Cancel(cancelledAt);

        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
        Assert.Null(subscription.NextChargeDueAtUtc);
        Assert.Equal(gracePeriodStartedAt, subscription.GracePeriodStartedAtUtc);
        Assert.Equal(gracePeriodEndsAt, subscription.GracePeriodEndsAtUtc);
        Assert.Equal(CreatedAtUtc, subscription.TrialStartedAtUtc);
        Assert.Equal(CreatedAtUtc.AddDays(7), subscription.TrialEndsAtUtc);
        Assert.Equal("subscription-456", subscription.GatewaySubscriptionId);
        Assert.Equal(cancelledAt, subscription.UpdatedAtUtc);
    }

    [Fact]
    public void Cancel_IsIdempotentAndDoesNotChangeTheOriginalCancellationTimestamp()
    {
        var subscription = CreateSubscription();
        subscription.ConfirmRecurringSubscription("customer-123", "subscription-456", CreatedAtUtc);
        var cancelledAt = CreatedAtUtc.AddDays(10);
        subscription.Cancel(cancelledAt);

        subscription.Cancel(cancelledAt.AddDays(1));

        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
        Assert.Equal(cancelledAt, subscription.UpdatedAtUtc);
    }

    [Fact]
    public void Cancel_RejectsPendingSubscriptionAndNonUtcTimestamp()
    {
        var pending = CreateSubscription();

        Assert.Throws<InvalidOperationException>(() => pending.Cancel(CreatedAtUtc));

        var confirmed = CreateSubscription();
        confirmed.ConfirmRecurringSubscription("customer-123", "subscription-456", CreatedAtUtc);
        Assert.Throws<ArgumentException>(() => confirmed.Cancel(CreatedAtUtc.ToOffset(TimeSpan.FromHours(-3))));
        Assert.Equal(SubscriptionStatus.Trial, confirmed.Status);
    }

    [Fact]
    public void Subscription_DoesNotExposePaymentCardOrTokenData()
    {
        var propertyNames = typeof(Subscription).GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain(propertyNames, name => name.Contains("card", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("cvv", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Constructor_StoresTheAgreedPurchaseAmount()
    {
        var subscription = new Subscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "standard",
            BillingCycle.Annual,
            CreatedAtUtc,
            199.90m);

        Assert.Equal(199.90m, subscription.AgreedAmount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(19.999)]
    public void Constructor_RejectsInvalidAgreedPurchaseAmounts(decimal amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Subscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "standard",
            BillingCycle.Monthly,
            CreatedAtUtc,
            amount));
    }

    [Fact]
    public void Constructor_RejectsAgreedAmountOutsideDatabasePrecision()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Subscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "standard",
            BillingCycle.Monthly,
            CreatedAtUtc,
            Subscription.MaximumAgreedAmount + 0.01m));
    }

    private static Subscription CreateSubscription(BillingCycle cycle = BillingCycle.Monthly) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "standard", cycle, CreatedAtUtc);
}
