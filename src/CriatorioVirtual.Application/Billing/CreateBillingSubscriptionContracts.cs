using System.Net;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;

namespace CriatorioVirtual.Application.Billing;

public sealed record CreateBillingSubscriptionCommand(
    Guid UserId,
    BillingCycle BillingCycle,
    string CustomerTaxIdentifier,
    string CardToken,
    IPAddress RemoteIp) : ICommand<CreateBillingSubscriptionResult>
{
    public override string ToString() =>
        $"{nameof(CreateBillingSubscriptionCommand)} {{ UserId = {UserId}, BillingCycle = {BillingCycle}, " +
        "CustomerTaxIdentifier = [redacted], CardToken = [redacted], RemoteIp = [redacted] }";
}

public enum CreateBillingSubscriptionStatus
{
    PendingConfirmation,
    TrialStarted,
    SubscriptionAlreadyExists,
    FarmNotSelected,
    FarmNotFound,
    NotFarmOwner,
    BillingCycleConflict,
    PlanNotConfigured,
    GatewayUnavailable
}

public sealed record CreateBillingSubscriptionResult(
    CreateBillingSubscriptionStatus Status,
    Guid? BreedingFarmId,
    Guid? SubscriptionId,
    string? PlanCode,
    BillingCycle? BillingCycle,
    decimal? Amount,
    SubscriptionStatus? SubscriptionStatus,
    DateTimeOffset? TrialStartedAtUtc,
    DateTimeOffset? TrialEndsAtUtc,
    DateTimeOffset? NextChargeDueAtUtc)
{
    public static CreateBillingSubscriptionResult ForStatus(CreateBillingSubscriptionStatus status) =>
        new(status, null, null, null, null, null, null, null, null, null);

    public static CreateBillingSubscriptionResult ForSubscription(
        CreateBillingSubscriptionStatus status,
        Subscription subscription) =>
        new(
            status,
            subscription.BreedingFarmId,
            subscription.Id,
            subscription.PlanCode,
            subscription.BillingCycle,
            subscription.AgreedAmount,
            subscription.Status,
            subscription.TrialStartedAtUtc,
            subscription.TrialEndsAtUtc,
            subscription.NextChargeDueAtUtc);
}
