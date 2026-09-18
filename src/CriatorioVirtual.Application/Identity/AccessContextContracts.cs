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
