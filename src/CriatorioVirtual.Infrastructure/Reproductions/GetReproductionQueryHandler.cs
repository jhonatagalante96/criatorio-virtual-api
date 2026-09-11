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
            .Where(candidate =>
                candidate.Id == query.ReproductionId &&
                candidate.BreedingFarmId == breedingFarmId)
            .Join(
                dbContext.Birds.AsNoTracking().Where(bird => bird.BreedingFarmId == breedingFarmId),
                candidate => new { candidate.BreedingFarmId, BirdId = candidate.MaleBirdId },
                bird => new { bird.BreedingFarmId, BirdId = bird.Id },
                (candidate, maleBird) => new { candidate, maleBird })
            .Join(
                dbContext.Birds.AsNoTracking().Where(bird => bird.BreedingFarmId == breedingFarmId),
                row => new { row.candidate.BreedingFarmId, BirdId = row.candidate.FemaleBirdId },
                bird => new { bird.BreedingFarmId, BirdId = bird.Id },
                (row, femaleBird) => new ReproductionDetailsProjection(
                    row.candidate.Id,
                    row.candidate.BreedingFarmId,
                    new ReproductionBirdProjection(
                        row.maleBird.Id,
                        row.maleBird.Name,
                        row.maleBird.Sex,
                        row.maleBird.BirthDate,
                        row.maleBird.RingNumber,
                        row.maleBird.Status),
                    new ReproductionBirdProjection(
                        femaleBird.Id,
                        femaleBird.Name,
                        femaleBird.Sex,
                        femaleBird.BirthDate,
                        femaleBird.RingNumber,
                        femaleBird.Status),
                    row.candidate.StartDate,
                    row.candidate.EndDate,
                    row.candidate.Notes,
                    row.candidate.Status,
                    row.candidate.CreatedAtUtc,
                    row.candidate.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
        if (reproduction is null)
        {
            return GetReproductionResult.ReproductionNotFound();
        }

        return GetReproductionResult.Succeeded(
            new ReproductionDetailsResult(
                reproduction.ReproductionId,
                reproduction.BreedingFarmId,
                ToBirdResult(reproduction.MaleBird),
                ToBirdResult(reproduction.FemaleBird),
                reproduction.StartDate,
                reproduction.EndDate,
                reproduction.Notes,
                reproduction.Status,
                reproduction.CreatedAtUtc,
                reproduction.UpdatedAtUtc));
    }

    private static ReproductionBirdResult ToBirdResult(ReproductionBirdProjection projection) =>
        new(
            projection.BirdId,
            projection.Name,
            projection.Sex,
            projection.BirthDate,
            projection.RingNumber,
            projection.Status);

    private sealed record ReproductionDetailsProjection(
        Guid ReproductionId,
        Guid BreedingFarmId,
        ReproductionBirdProjection MaleBird,
        ReproductionBirdProjection FemaleBird,
        DateOnly StartDate,
        DateOnly? EndDate,
        string? Notes,
        CriatorioVirtual.Domain.Reproductions.ReproductionStatus Status,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);

    private sealed record ReproductionBirdProjection(
        Guid BirdId,
        string Name,
        CriatorioVirtual.Domain.Birds.BirdSex Sex,
        DateOnly? BirthDate,
        string? RingNumber,
        CriatorioVirtual.Domain.Birds.BirdStatus Status);
}
