using CriatorioVirtual.Application.Dashboard;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Reproductions;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Dashboard;

public sealed class GetDashboardQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<GetDashboardQuery, GetDashboardResult>
{
    public async Task<GetDashboardResult> Handle(
        GetDashboardQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);
        if (user is null)
        {
            return GetDashboardResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return GetDashboardResult.BreedingFarmNotSelected();
        }

        var breedingFarmId = user.SelectedBreedingFarmId.Value;
        var hasActiveOwnerMembership = await dbContext.BreedingFarmUsers
            .AsNoTracking()
            .AnyAsync(
                membership =>
                    membership.BreedingFarmId == breedingFarmId &&
                    membership.UserId == query.UserId &&
                    membership.IsActive &&
                    membership.Role == BreedingFarmRole.Owner,
                cancellationToken);
        if (!hasActiveOwnerMembership)
        {
            return GetDashboardResult.BreedingFarmNotFound();
        }

        var birdIndicators = await dbContext.Birds
            .AsNoTracking()
            .Where(bird => bird.BreedingFarmId == breedingFarmId)
            .GroupBy(_ => 1)
            .Select(group => new DashboardIndicatorProjection(
                group.Count(bird => bird.Status == BirdStatus.Active),
                group.Count(bird => bird.Status == BirdStatus.Active && bird.RingNumber == null)))
            .SingleOrDefaultAsync(cancellationToken)
            ?? new DashboardIndicatorProjection(0, 0);

        var activeReproductionCount = await dbContext.Reproductions
            .AsNoTracking()
            .CountAsync(
                reproduction =>
                    reproduction.BreedingFarmId == breedingFarmId &&
                    reproduction.Status == ReproductionStatus.Active,
                cancellationToken);

        var recentBirds = await dbContext.Birds
            .AsNoTracking()
            .Where(bird => bird.BreedingFarmId == breedingFarmId)
            .OrderByDescending(bird => bird.CreatedAtUtc)
            .ThenByDescending(bird => bird.Id)
            .Take(DashboardLimits.MaxRecentActivities)
            .Select(bird => new DashboardActivityProjection(
                "BirdRegistered",
                "bird",
                bird.Id,
                bird.Name,
                bird.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);

        var recentReproductions = await dbContext.Reproductions
            .AsNoTracking()
            .Where(reproduction => reproduction.BreedingFarmId == breedingFarmId)
            .OrderByDescending(reproduction => reproduction.CreatedAtUtc)
            .ThenByDescending(reproduction => reproduction.Id)
            .Take(DashboardLimits.MaxRecentActivities)
            .Select(reproduction => new DashboardActivityProjection(
                "ReproductionRegistered",
                "reproduction",
                reproduction.Id,
                "Reproduction registered",
                reproduction.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);

        var activities = recentBirds
            .Concat(recentReproductions)
            .OrderByDescending(activity => activity.OccurredAtUtc)
            .ThenByDescending(activity => activity.ResourceId)
            .Take(DashboardLimits.MaxRecentActivities)
            .Select(activity => new DashboardActivityResult(
                activity.ActivityType,
                activity.ResourceType,
                activity.ResourceId,
                activity.Title,
                activity.OccurredAtUtc))
            .ToArray();

        var pending = birdIndicators.PendingIdentificationCount == 0
            ? []
            : new[]
            {
                new DashboardPendingResult(
                    "BirdIdentificationPending",
                    "bird",
                    "Birds pending identification",
                    birdIndicators.PendingIdentificationCount)
            };

        return GetDashboardResult.Succeeded(
            new DashboardResult(
                breedingFarmId,
                new DashboardIndicatorsResult(
                    birdIndicators.ActiveBirdCount,
                    birdIndicators.PendingIdentificationCount,
                    activeReproductionCount),
                pending,
                activities));
    }

    private sealed record DashboardIndicatorProjection(
        int ActiveBirdCount,
        int PendingIdentificationCount);

    private sealed record DashboardActivityProjection(
        string ActivityType,
        string ResourceType,
        Guid ResourceId,
        string Title,
        DateTimeOffset OccurredAtUtc);
}
