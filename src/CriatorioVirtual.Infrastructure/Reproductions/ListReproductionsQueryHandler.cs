using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Reproductions;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Reproductions;

public sealed class ListReproductionsQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<ListReproductionsQuery, ListReproductionsResult>
{
    public async Task<ListReproductionsResult> Handle(
        ListReproductionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);
        if (user is null)
        {
            return ListReproductionsResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return ListReproductionsResult.BreedingFarmNotSelected();
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
            return ListReproductionsResult.BreedingFarmNotFound();
        }

        var reproductions = dbContext.Reproductions
            .AsNoTracking()
            .Where(reproduction => reproduction.BreedingFarmId == breedingFarmId);
        if (query.Status is not null)
        {
            reproductions = reproductions.Where(reproduction => reproduction.Status == query.Status.Value);
        }

        if (query.BirdId is not null)
        {
            reproductions = reproductions.Where(reproduction =>
                reproduction.MaleBirdId == query.BirdId.Value ||
                reproduction.FemaleBirdId == query.BirdId.Value);
        }

        var totalCount = await reproductions.CountAsync(cancellationToken);
        var skip = (long)(query.Page - 1) * query.PageSize;
        ReproductionListProjection[] rows;
        if (skip > int.MaxValue)
        {
            rows = [];
        }
        else
        {
            rows = await reproductions
                .OrderByDescending(reproduction => reproduction.StartDate)
                .ThenByDescending(reproduction => reproduction.CreatedAtUtc)
                .ThenByDescending(reproduction => reproduction.Id)
                .Skip((int)skip)
                .Take(query.PageSize)
                .Join(
                    dbContext.Birds.AsNoTracking().Where(bird => bird.BreedingFarmId == breedingFarmId),
                    reproduction => new { reproduction.BreedingFarmId, BirdId = reproduction.MaleBirdId },
                    bird => new { bird.BreedingFarmId, BirdId = bird.Id },
                    (reproduction, maleBird) => new { reproduction, maleBird })
                .Join(
                    dbContext.Birds.AsNoTracking().Where(bird => bird.BreedingFarmId == breedingFarmId),
                    row => new { row.reproduction.BreedingFarmId, BirdId = row.reproduction.FemaleBirdId },
                    bird => new { bird.BreedingFarmId, BirdId = bird.Id },
                    (row, femaleBird) => new ReproductionListProjection(
                        row.reproduction.Id,
                        row.reproduction.BreedingFarmId,
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
                        row.reproduction.StartDate,
                        row.reproduction.EndDate,
                        row.reproduction.Status,
                        row.reproduction.CreatedAtUtc,
                        row.reproduction.UpdatedAtUtc))
                .ToArrayAsync(cancellationToken);
        }

        return ListReproductionsResult.Succeeded(
            breedingFarmId,
            rows.Select(ToResult).ToArray(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    private static ReproductionListItemResult ToResult(ReproductionListProjection projection) =>
        new(
            projection.ReproductionId,
            projection.BreedingFarmId,
            ToBirdResult(projection.MaleBird),
            ToBirdResult(projection.FemaleBird),
            projection.StartDate,
            projection.EndDate,
            projection.Status,
            projection.CreatedAtUtc,
            projection.UpdatedAtUtc);

    private static ReproductionBirdResult ToBirdResult(ReproductionBirdProjection projection) =>
        new(
            projection.BirdId,
            projection.Name,
            projection.Sex,
            projection.BirthDate,
            projection.RingNumber,
            projection.Status);

    private sealed record ReproductionListProjection(
        Guid ReproductionId,
        Guid BreedingFarmId,
        ReproductionBirdProjection MaleBird,
        ReproductionBirdProjection FemaleBird,
        DateOnly StartDate,
        DateOnly? EndDate,
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
