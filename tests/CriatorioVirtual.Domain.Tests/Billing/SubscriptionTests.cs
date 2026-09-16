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
    public void FailFirstPayment_RejectsFailureBeforeTheFirstChargeIsDue()
    {
        var subscription = CreateSubscription();
        subscription.ConfirmRecurringSubscription("customer-123", "subscription-456", CreatedAtUtc);

        Assert.Throws<InvalidOperationException>(() => subscription.FailFirstPayment(CreatedAtUtc.AddDays(6)));
        Assert.Equal(SubscriptionStatus.Trial, subscription.Status);
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
