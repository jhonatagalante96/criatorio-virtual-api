using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Reproductions;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Reproductions;

public sealed class GetReproductionQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<GetReproductionQuery, GetReproductionResult>
{
    public async Task<GetReproductionResult> Handle(
        GetReproductionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);
        if (user is null)
        {
            return GetReproductionResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return GetReproductionResult.BreedingFarmNotSelected();
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
            return GetReproductionResult.BreedingFarmNotFound();
        }

        var reproduction = await dbContext.Reproductions
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.Id == query.ReproductionId &&
                candidate.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (reproduction is null)
        {
            return GetReproductionResult.ReproductionNotFound();
        }

        var birdIds = new[] { reproduction.MaleBirdId, reproduction.FemaleBirdId };
        var authorizedBirdIds = (await dbContext.Birds
            .AsNoTracking()
            .Where(b => b.BreedingFarmId == breedingFarmId && birdIds.Contains(b.Id))
            .Select(b => b.Id)
            .ToArrayAsync(cancellationToken))
            .ToHashSet();

        return GetReproductionResult.Succeeded(
            new ReproductionDetailsResult(
                reproduction.Id,
                reproduction.BreedingFarmId,
                new ReproductionBirdResult(
                    reproduction.MaleBirdId,
                    reproduction.MaleBirdName,
                    reproduction.MaleBirdSex,
                    reproduction.MaleBirdBirthDate,
                    reproduction.MaleBirdRingNumber,
                    reproduction.MaleBirdStatus,
                    authorizedBirdIds.Contains(reproduction.MaleBirdId)),
                new ReproductionBirdResult(
                    reproduction.FemaleBirdId,
                    reproduction.FemaleBirdName,
                    reproduction.FemaleBirdSex,
                    reproduction.FemaleBirdBirthDate,
                    reproduction.FemaleBirdRingNumber,
                    reproduction.FemaleBirdStatus,
                    authorizedBirdIds.Contains(reproduction.FemaleBirdId)),
                reproduction.StartDate,
                reproduction.EndDate,
                reproduction.Notes,
                reproduction.Status,
                reproduction.CreatedAtUtc,
                reproduction.UpdatedAtUtc));
    }
}
