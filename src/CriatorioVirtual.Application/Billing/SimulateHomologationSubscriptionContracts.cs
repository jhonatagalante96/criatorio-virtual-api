using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;

namespace CriatorioVirtual.Application.Billing;

public enum SimulatedSubscriptionState
{
    None,
    PendingSubscription,
    Trial,
    Active,
    GracePeriod,
    Blocked,
    Cancelled
}

public sealed record SimulateHomologationSubscriptionCommand(
    Guid UserId,
    Guid? BreedingFarmId,
    SimulatedSubscriptionState State,
    string? PlanCode = null,
    BillingCycle? BillingCycle = null) : ICommand<SimulateHomologationSubscriptionResult>;

public enum SimulateHomologationSubscriptionStatus
{
    Success,
    FarmNotSelected,
    FarmNotFound,
    NotFarmOwner
}

public sealed record SimulateHomologationSubscriptionResult(
    SimulateHomologationSubscriptionStatus Status,
    Guid? BreedingFarmId,
    Guid? SubscriptionId,
    SimulatedSubscriptionState State,
    SubscriptionStatus? SubscriptionStatus,
    DateTimeOffset? TrialStartedAtUtc,
    DateTimeOffset? TrialEndsAtUtc,
    DateTimeOffset? NextChargeDueAtUtc,
    DateTimeOffset? GracePeriodStartedAtUtc,
    DateTimeOffset? GracePeriodEndsAtUtc)
{
    public static SimulateHomologationSubscriptionResult Failure(SimulateHomologationSubscriptionStatus status) =>
        new(status, null, null, SimulatedSubscriptionState.None, null, null, null, null, null, null);
}
