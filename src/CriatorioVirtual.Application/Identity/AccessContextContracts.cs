using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;

namespace CriatorioVirtual.Application.Identity;

public sealed record GetAccessContextQuery(
    Guid UserId,
    string? AvatarUrl) : IQuery<AccessContextResult?>;

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
    SubscriptionStatus Status,
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
    public static bool CanAccessApp(SubscriptionStatus? status) =>
        status is SubscriptionStatus.Trial
            or SubscriptionStatus.Active
            or SubscriptionStatus.GracePeriod;

    public static AccessDetails Evaluate(
        SubscriptionStatus? status,
        DateTimeOffset? trialEndsAtUtc = null,
        DateTimeOffset? gracePeriodEndsAtUtc = null)
    {
        if (status is null)
        {
            return new AccessDetails(
                Status: SubscriptionStatus.PendingSubscription,
                CanAccessApp: false,
                BlockedReason: AccessBlockedReason.SubscriptionRequired,
                RequiredAction: AccessRequiredAction.Subscribe,
                TrialEndsAt: null,
                GracePeriodEndsAt: null);
        }

        var (canAccessApp, blockedReason, requiredAction) = status.Value switch
        {
            SubscriptionStatus.Trial => (true, (AccessBlockedReason?)null, AccessRequiredAction.None),
            SubscriptionStatus.Active => (true, (AccessBlockedReason?)null, AccessRequiredAction.None),
            SubscriptionStatus.GracePeriod => (true, (AccessBlockedReason?)null, AccessRequiredAction.Regularize),
            SubscriptionStatus.Blocked => (false, (AccessBlockedReason?)AccessBlockedReason.PaymentOverdue, AccessRequiredAction.Regularize),
            SubscriptionStatus.Cancelled => (false, (AccessBlockedReason?)AccessBlockedReason.SubscriptionCancelled, AccessRequiredAction.Resubscribe),
            SubscriptionStatus.PendingSubscription => (false, (AccessBlockedReason?)AccessBlockedReason.SubscriptionRequired, AccessRequiredAction.Subscribe),
            _ => throw new InvalidOperationException($"The subscription status '{status}' is not supported.")
        };

        return new AccessDetails(
            Status: status.Value,
            CanAccessApp: canAccessApp,
            BlockedReason: blockedReason,
            RequiredAction: requiredAction,
            TrialEndsAt: trialEndsAtUtc,
            GracePeriodEndsAt: gracePeriodEndsAtUtc);
    }

    public static AccessDetails ForUnselectedBreedingFarm() =>
        new(
            Status: SubscriptionStatus.PendingSubscription,
            CanAccessApp: false,
            BlockedReason: null,
            RequiredAction: AccessRequiredAction.None,
            TrialEndsAt: null,
            GracePeriodEndsAt: null);
}
