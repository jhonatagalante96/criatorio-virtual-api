using CriatorioVirtual.Application.Messaging;

namespace CriatorioVirtual.Application.Identity;

public sealed record GetAccessContextQuery(
    Guid UserId,
    string? AvatarUrl) : IQuery<AccessContextResult?>;

public enum AccessStatus
{
    PendingSubscription,
    Trial,
    Active,
    GracePeriod,
    Blocked,
    Cancelled
}

public enum AccessRequiredAction
{
    None,
    Subscribe,
    Regularize,
    Resubscribe
}

public enum AccessBlockedReason
{
    SubscriptionRequired,
    PaymentOverdue,
    SubscriptionCancelled
}

public enum OnboardingStatus
{
    Pending,
    Completed
}

public sealed record UserProfileSummary(
    Guid Id,
    string Name,
    string Email,
    string? AvatarUrl);

public sealed record BreedingFarmAccessSummary(
    Guid Id,
    string Name,
    string Role);

public sealed record OnboardingSummary(
    OnboardingStatus Status,
    string? NextStep);

public sealed record AccessDetails(
    AccessStatus Status,
    bool CanAccessApp,
    AccessBlockedReason? BlockedReason,
    AccessRequiredAction RequiredAction,
    DateTimeOffset? TrialEndsAt,
    DateTimeOffset? GracePeriodEndsAt);

public sealed record SubscriptionSummary(
    string Plan,
    string Cycle,
    string Status);

public sealed record AccessContextResult(
    UserProfileSummary User,
    BreedingFarmAccessSummary? BreedingFarm,
    OnboardingSummary Onboarding,
    AccessDetails Access,
    SubscriptionSummary? Subscription);

public static class BillingAccessPolicy
{
    public static bool CanAccessApp(CriatorioVirtual.Domain.Billing.SubscriptionStatus? status) =>
        status is CriatorioVirtual.Domain.Billing.SubscriptionStatus.Trial
            or CriatorioVirtual.Domain.Billing.SubscriptionStatus.Active
            or CriatorioVirtual.Domain.Billing.SubscriptionStatus.GracePeriod;

    public static AccessDetails Evaluate(
        CriatorioVirtual.Domain.Billing.SubscriptionStatus? status,
        DateTimeOffset? trialEndsAtUtc = null,
        DateTimeOffset? gracePeriodEndsAtUtc = null)
    {
        if (status is null)
        {
            return new AccessDetails(
                Status: AccessStatus.PendingSubscription,
                CanAccessApp: false,
                BlockedReason: AccessBlockedReason.SubscriptionRequired,
                RequiredAction: AccessRequiredAction.Subscribe,
                TrialEndsAt: null,
                GracePeriodEndsAt: null);
        }

        var (accessStatus, canAccessApp, blockedReason, requiredAction) = status switch
        {
            CriatorioVirtual.Domain.Billing.SubscriptionStatus.Trial => (AccessStatus.Trial, true, (AccessBlockedReason?)null, AccessRequiredAction.None),
            CriatorioVirtual.Domain.Billing.SubscriptionStatus.Active => (AccessStatus.Active, true, (AccessBlockedReason?)null, AccessRequiredAction.None),
            CriatorioVirtual.Domain.Billing.SubscriptionStatus.GracePeriod => (AccessStatus.GracePeriod, true, (AccessBlockedReason?)null, AccessRequiredAction.Regularize),
            CriatorioVirtual.Domain.Billing.SubscriptionStatus.Blocked => (AccessStatus.Blocked, false, (AccessBlockedReason?)AccessBlockedReason.PaymentOverdue, AccessRequiredAction.Regularize),
            CriatorioVirtual.Domain.Billing.SubscriptionStatus.Cancelled => (AccessStatus.Cancelled, false, (AccessBlockedReason?)AccessBlockedReason.SubscriptionCancelled, AccessRequiredAction.Resubscribe),
            CriatorioVirtual.Domain.Billing.SubscriptionStatus.PendingSubscription => (AccessStatus.PendingSubscription, false, (AccessBlockedReason?)AccessBlockedReason.SubscriptionRequired, AccessRequiredAction.Subscribe),
            _ => throw new InvalidOperationException($"The subscription status '{status}' is not supported.")
        };

        return new AccessDetails(
            Status: accessStatus,
            CanAccessApp: canAccessApp,
            BlockedReason: blockedReason,
            RequiredAction: requiredAction,
            TrialEndsAt: trialEndsAtUtc,
            GracePeriodEndsAt: gracePeriodEndsAtUtc);
    }

    public static AccessDetails ForUnselectedBreedingFarm() =>
        new(
            Status: AccessStatus.PendingSubscription,
            CanAccessApp: false,
            BlockedReason: null,
            RequiredAction: AccessRequiredAction.None,
            TrialEndsAt: null,
            GracePeriodEndsAt: null);
}
