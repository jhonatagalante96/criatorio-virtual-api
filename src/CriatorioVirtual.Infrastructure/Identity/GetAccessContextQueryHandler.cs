using CriatorioVirtual.Application.Identity;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Identity;

public sealed class GetAccessContextQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<GetAccessContextQuery, AccessContextResult?>
{
    public async Task<AccessContextResult?> Handle(
        GetAccessContextQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);

        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            return null;
        }

        var userName = user.UserName ?? user.Email;
        var avatarUrl = user.AvatarObjectKey is not null ? query.AvatarUrl : null;
        var userSummary = new UserProfileSummary(user.Id, userName, user.Email, avatarUrl);

        BreedingFarmAccessSummary? farmSummary = null;
        if (user.SelectedBreedingFarmId is { } selectedFarmId)
        {
            var membership = await dbContext.BreedingFarmUsers
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.BreedingFarmId == selectedFarmId &&
                                 candidate.UserId == query.UserId &&
                                 candidate.IsActive,
                    cancellationToken);

            if (membership is not null)
            {
                var farm = await dbContext.BreedingFarms
                    .AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.Id == selectedFarmId, cancellationToken);

                if (farm is not null)
                {
                    farmSummary = new BreedingFarmAccessSummary(farm.Id, farm.Name, membership.Role.ToString());
                }
            }
        }

        OnboardingSummary onboarding;
        if (farmSummary is not null)
        {
            onboarding = new OnboardingSummary(OnboardingStatus.Completed, null);
        }
        else
        {
            var hasAnyFarm = await dbContext.BreedingFarmUsers
                .AsNoTracking()
                .AnyAsync(candidate => candidate.UserId == query.UserId && candidate.IsActive, cancellationToken);

            onboarding = new OnboardingSummary(
                OnboardingStatus.Pending,
                hasAnyFarm ? "SelectBreedingFarm" : "CreateBreedingFarm");
        }

        if (farmSummary is null)
        {
            var unselectedAccess = new AccessDetails(
                Status: AccessStatus.PendingSubscription,
                CanAccessApp: false,
                BlockedReason: null,
                RequiredAction: AccessRequiredAction.None,
                TrialEndsAt: null,
                GracePeriodEndsAt: null);

            return new AccessContextResult(userSummary, null, onboarding, unselectedAccess, null);
        }

        var subscription = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(candidate => candidate.BreedingFarmId == farmSummary.Id)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .ThenByDescending(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is null)
        {
            var pendingAccess = new AccessDetails(
                Status: AccessStatus.PendingSubscription,
                CanAccessApp: false,
                BlockedReason: AccessBlockedReason.SubscriptionRequired,
                RequiredAction: AccessRequiredAction.Subscribe,
                TrialEndsAt: null,
                GracePeriodEndsAt: null);

            return new AccessContextResult(userSummary, farmSummary, onboarding, pendingAccess, null);
        }

        var (accessStatus, canAccessApp, blockedReason, requiredAction) = subscription.Status switch
        {
            SubscriptionStatus.Trial => (AccessStatus.Trial, true, (AccessBlockedReason?)null, AccessRequiredAction.None),
            SubscriptionStatus.Active => (AccessStatus.Active, true, (AccessBlockedReason?)null, AccessRequiredAction.None),
            SubscriptionStatus.GracePeriod => (AccessStatus.GracePeriod, true, (AccessBlockedReason?)null, AccessRequiredAction.Regularize),
            SubscriptionStatus.Blocked => (AccessStatus.Blocked, false, (AccessBlockedReason?)AccessBlockedReason.PaymentOverdue, AccessRequiredAction.Regularize),
            SubscriptionStatus.Cancelled => (AccessStatus.Cancelled, false, (AccessBlockedReason?)AccessBlockedReason.SubscriptionCancelled, AccessRequiredAction.Resubscribe),
            SubscriptionStatus.PendingSubscription => (AccessStatus.PendingSubscription, false, (AccessBlockedReason?)AccessBlockedReason.SubscriptionRequired, AccessRequiredAction.Subscribe),
            _ => throw new InvalidOperationException($"The subscription status '{subscription.Status}' is not supported.")
        };

        var accessDetails = new AccessDetails(
            Status: accessStatus,
            CanAccessApp: canAccessApp,
            BlockedReason: blockedReason,
            RequiredAction: requiredAction,
            TrialEndsAt: subscription.TrialEndsAtUtc,
            GracePeriodEndsAt: subscription.GracePeriodEndsAtUtc);

        var subscriptionSummary = new SubscriptionSummary(
            subscription.PlanCode,
            subscription.BillingCycle.ToString(),
            subscription.Status.ToString());

        return new AccessContextResult(userSummary, farmSummary, onboarding, accessDetails, subscriptionSummary);
    }
}
