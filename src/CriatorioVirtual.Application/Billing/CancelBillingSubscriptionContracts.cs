using CriatorioVirtual.Application.Messaging;

namespace CriatorioVirtual.Application.Billing;

public enum CancelBillingSubscriptionStatus
{
    CancellationRequested,
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    NotFarmOwner,
    SubscriptionNotFound,
    SubscriptionNotCancelable,
    GatewayUnavailable
}

public sealed record CancelBillingSubscriptionCommand(Guid UserId)
    : ICommand<CancelBillingSubscriptionResult>;

public sealed record CancelBillingSubscriptionResult(
    CancelBillingSubscriptionStatus Status,
    Guid? BreedingFarmId = null,
    Guid? SubscriptionId = null,
    string? GatewaySubscriptionId = null)
{
    public static CancelBillingSubscriptionResult RequestCancellation(
        Guid breedingFarmId,
        Guid subscriptionId,
        string gatewaySubscriptionId) =>
        new(
            CancelBillingSubscriptionStatus.CancellationRequested,
            breedingFarmId,
            subscriptionId,
            gatewaySubscriptionId);

    public static CancelBillingSubscriptionResult Succeeded(
        Guid? breedingFarmId = null,
        Guid? subscriptionId = null) =>
        new(CancelBillingSubscriptionStatus.Success, breedingFarmId, subscriptionId);

    public static CancelBillingSubscriptionResult Failed(CancelBillingSubscriptionStatus status) =>
        new(status);
}
