using CriatorioVirtual.Application.Identity;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Domain.BreedingFarms;
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
                                 candidate.IsActive &&
                                 candidate.Role == BreedingFarmRole.Owner,
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
            var hasAnyOwnerFarm = await dbContext.BreedingFarmUsers
                .AsNoTracking()
                .AnyAsync(
                    candidate => candidate.UserId == query.UserId &&
                                 candidate.IsActive &&
                                 candidate.Role == BreedingFarmRole.Owner,
                    cancellationToken);

            onboarding = new OnboardingSummary(
                OnboardingStatus.Pending,
                hasAnyOwnerFarm ? "SelectBreedingFarm" : "CreateBreedingFarm");
        }

        if (farmSummary is null)
        {
            return new AccessContextResult(
                userSummary,
                null,
                onboarding,
                BillingAccessPolicy.ForUnselectedBreedingFarm(),
                null);
        }

        var subscription = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(candidate => candidate.BreedingFarmId == farmSummary.Id)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .ThenByDescending(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var accessDetails = BillingAccessPolicy.Evaluate(
            subscription?.Status,
            subscription?.TrialEndsAtUtc,
            subscription?.GracePeriodEndsAtUtc);

        var subscriptionSummary = subscription is null
            ? null
            : new SubscriptionSummary(
                subscription.PlanCode,
                subscription.BillingCycle.ToString(),
                subscription.Status.ToString());

        return new AccessContextResult(userSummary, farmSummary, onboarding, accessDetails, subscriptionSummary);
    }
}
