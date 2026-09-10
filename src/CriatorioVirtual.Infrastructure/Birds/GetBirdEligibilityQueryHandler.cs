using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

public sealed class GetBirdEligibilityQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<GetBirdEligibilityQuery, GetBirdEligibilityResult>
{
    public async Task<GetBirdEligibilityResult> Handle(
        GetBirdEligibilityQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);
        if (user is null)
        {
            return GetBirdEligibilityResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return GetBirdEligibilityResult.BreedingFarmNotSelected();
        }

        var breedingFarmId = user.SelectedBreedingFarmId.Value;
        var hasActiveMembership = await dbContext.BreedingFarmUsers
            .AsNoTracking()
            .AnyAsync(
                membership =>
                    membership.BreedingFarmId == breedingFarmId &&
                    membership.UserId == query.UserId &&
                    membership.IsActive,
                cancellationToken);
        if (!hasActiveMembership)
        {
            return GetBirdEligibilityResult.BreedingFarmNotFound();
        }

        var bird = await dbContext.Birds
            .AsNoTracking()
            .Where(candidate =>
                candidate.Id == query.BirdId &&
                candidate.BreedingFarmId == breedingFarmId)
            .Select(candidate => new BirdEligibilityProjection(
                candidate.Id,
                candidate.RingNumber,
                candidate.Status))
            .SingleOrDefaultAsync(cancellationToken);
        if (bird is null)
        {
            return GetBirdEligibilityResult.BirdNotFound();
        }

        var eligibility = BirdEligibility.Evaluate(bird.RingNumber, bird.Status);
        return GetBirdEligibilityResult.Succeeded(
            new BirdEligibilityResult(
                bird.BirdId,
                eligibility.IsEligible,
                bird.RingNumber is null,
                eligibility.Issues));
    }

    private sealed record BirdEligibilityProjection(
        Guid BirdId,
        string? RingNumber,
        BirdStatus Status);
}
