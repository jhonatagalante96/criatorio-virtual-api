using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;

namespace CriatorioVirtual.Application.Billing;

public sealed record CreateBillingSubscriptionCheckoutCommand(
    Guid UserId,
    BillingCycle BillingCycle,
    string CustomerTaxIdentifier) : ICommand<CreateBillingSubscriptionCheckoutResult>
{
    public override string ToString() =>
        $"{nameof(CreateBillingSubscriptionCheckoutCommand)} {{ UserId = {UserId}, BillingCycle = {BillingCycle}, " +
        "CustomerTaxIdentifier = [redacted] }";
}

public enum CreateBillingSubscriptionCheckoutStatus
{
    PendingCheckout,
    CheckoutAlreadyExists,
    SubscriptionAlreadyExists,
    FarmNotSelected,
    FarmNotFound,
    NotFarmOwner,
    BillingCycleConflict,
    PlanNotConfigured,
    GatewayUnavailable
}

public sealed record CreateBillingSubscriptionCheckoutResult(
    CreateBillingSubscriptionCheckoutStatus Status,
    Guid? BreedingFarmId,
    Guid? SubscriptionId,
    BillingCycle? BillingCycle,
    decimal? Amount,
    string? CheckoutId,
    string? CheckoutUrl,
    DateTimeOffset? ExpiresAtUtc)
{
    public static CreateBillingSubscriptionCheckoutResult ForStatus(CreateBillingSubscriptionCheckoutStatus status) =>
        new(status, null, null, null, null, null, null, null);

    public static CreateBillingSubscriptionCheckoutResult ForSubscription(
        CreateBillingSubscriptionCheckoutStatus status,
        Subscription subscription) =>
        new(
            status,
            subscription.BreedingFarmId,
            subscription.Id,
            subscription.BillingCycle,
            subscription.AgreedAmount,
            subscription.GatewayCheckoutId,
            subscription.GatewayCheckoutUrl,
            subscription.GatewayCheckoutExpiresAtUtc);
}
