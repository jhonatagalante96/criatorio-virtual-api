using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Reproductions;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Reproductions;
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
                .Select(reproduction => new ReproductionListProjection(
                    reproduction.Id,
                    reproduction.BreedingFarmId,
                    reproduction.MaleBirdId,
                    reproduction.MaleBirdName,
                    reproduction.MaleBirdSex,
                    reproduction.MaleBirdBirthDate,
                    reproduction.MaleBirdRingNumber,
                    reproduction.MaleBirdStatus,
                    reproduction.FemaleBirdId,
                    reproduction.FemaleBirdName,
                    reproduction.FemaleBirdSex,
                    reproduction.FemaleBirdBirthDate,
                    reproduction.FemaleBirdRingNumber,
                    reproduction.FemaleBirdStatus,
                    reproduction.StartDate,
                    reproduction.EndDate,
                    reproduction.Status,
                    reproduction.CreatedAtUtc,
                    reproduction.UpdatedAtUtc))
                .ToArrayAsync(cancellationToken);
        }

        var participantBirdIds = rows
            .SelectMany(row => new[] { row.MaleBirdId, row.FemaleBirdId })
            .Distinct()
            .ToArray();

        var authorizedBirdIds = participantBirdIds.Length == 0
            ? new HashSet<Guid>()
            : (await dbContext.Birds
                .AsNoTracking()
                .Where(bird => bird.BreedingFarmId == breedingFarmId && participantBirdIds.Contains(bird.Id))
                .Select(bird => bird.Id)
                .ToArrayAsync(cancellationToken))
                .ToHashSet();

        var items = rows
            .Select(row => new ReproductionListItemResult(
                row.ReproductionId,
                row.BreedingFarmId,
                new ReproductionBirdResult(
                    row.MaleBirdId,
                    row.MaleBirdName,
                    row.MaleBirdSex,
                    row.MaleBirdBirthDate,
                    row.MaleBirdRingNumber,
                    row.MaleBirdStatus,
                    authorizedBirdIds.Contains(row.MaleBirdId)),
                new ReproductionBirdResult(
                    row.FemaleBirdId,
                    row.FemaleBirdName,
                    row.FemaleBirdSex,
                    row.FemaleBirdBirthDate,
                    row.FemaleBirdRingNumber,
                    row.FemaleBirdStatus,
                    authorizedBirdIds.Contains(row.FemaleBirdId)),
                row.StartDate,
                row.EndDate,
                row.Status,
                row.CreatedAtUtc,
                row.UpdatedAtUtc))
            .ToArray();

        return ListReproductionsResult.Succeeded(
            breedingFarmId,
            items,
            query.Page,
            query.PageSize,
            totalCount);
    }

    private sealed record ReproductionListProjection(
        Guid ReproductionId,
        Guid BreedingFarmId,
        Guid MaleBirdId,
        string MaleBirdName,
        BirdSex MaleBirdSex,
        DateOnly? MaleBirdBirthDate,
        string? MaleBirdRingNumber,
        BirdStatus MaleBirdStatus,
        Guid FemaleBirdId,
        string FemaleBirdName,
        BirdSex FemaleBirdSex,
        DateOnly? FemaleBirdBirthDate,
        string? FemaleBirdRingNumber,
        BirdStatus FemaleBirdStatus,
        DateOnly StartDate,
        DateOnly? EndDate,
        ReproductionStatus Status,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);
}
