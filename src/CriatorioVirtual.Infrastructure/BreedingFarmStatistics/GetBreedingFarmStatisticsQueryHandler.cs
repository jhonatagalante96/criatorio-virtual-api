using CriatorioVirtual.Application.BreedingFarmStatistics;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Reproductions;
using CriatorioVirtual.Domain.Transfers;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.BreedingFarmStatistics;

public sealed class GetBreedingFarmStatisticsQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<GetBreedingFarmStatisticsQuery, GetBreedingFarmStatisticsResult>
{
    public async Task<GetBreedingFarmStatisticsResult> Handle(
        GetBreedingFarmStatisticsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);
        if (user is null)
        {
            return GetBreedingFarmStatisticsResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return GetBreedingFarmStatisticsResult.BreedingFarmNotSelected();
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
            return GetBreedingFarmStatisticsResult.BreedingFarmNotFound();
        }

        var birdsByStatusGroups = await dbContext.Birds
            .AsNoTracking()
            .Where(bird => bird.BreedingFarmId == breedingFarmId)
            .GroupBy(bird => bird.Status)
            .Select(group => new BirdStatusCountProjection(group.Key, group.Count()))
            .ToArrayAsync(cancellationToken);
        var birdsBySexGroups = await dbContext.Birds
            .AsNoTracking()
            .Where(bird => bird.BreedingFarmId == breedingFarmId)
            .GroupBy(bird => bird.Sex)
            .Select(group => new BirdSexCountProjection(group.Key, group.Count()))
            .ToArrayAsync(cancellationToken);
        var speciesCounts = await dbContext.Birds
            .AsNoTracking()
            .Where(bird => bird.BreedingFarmId == breedingFarmId)
            .GroupBy(bird => bird.SpeciesId)
            .Select(group => new SpeciesCountProjection(group.Key, group.Count()))
            .ToDictionaryAsync(group => group.SpeciesId, group => group.Count, cancellationToken);
        var species = await dbContext.Species
            .AsNoTracking()
            .OrderBy(candidate => candidate.PopularName)
            .ThenBy(candidate => candidate.ScientificName)
            .Select(candidate => new SpeciesProjection(
                candidate.Id,
                candidate.PopularName,
                candidate.ScientificName))
            .ToArrayAsync(cancellationToken);

        var fromUtc = new DateTimeOffset(query.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var toExclusiveUtc = new DateTimeOffset(query.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        var registeredBirdCounts = await dbContext.Birds
            .AsNoTracking()
            .Where(bird =>
                bird.BreedingFarmId == breedingFarmId &&
                bird.CreatedAtUtc >= fromUtc &&
                bird.CreatedAtUtc < toExclusiveUtc)
            .GroupBy(bird => bird.CreatedAtUtc.DateTime.Date)
            .Select(group => new TimestampCountProjection(group.Key, group.Count()))
            .ToArrayAsync(cancellationToken);
        var birthsByDate = await dbContext.Birds
            .AsNoTracking()
            .Where(bird =>
                bird.BreedingFarmId == breedingFarmId &&
                bird.BirthDate != null &&
                bird.BirthDate >= query.From &&
                bird.BirthDate <= query.To)
            .GroupBy(bird => bird.BirthDate!.Value)
            .Select(group => new DateCountProjection(group.Key, group.Count()))
            .ToArrayAsync(cancellationToken);
        var reproductionsStarted = await dbContext.Reproductions
            .AsNoTracking()
            .Where(reproduction =>
                reproduction.BreedingFarmId == breedingFarmId &&
                reproduction.StartDate >= query.From &&
                reproduction.StartDate <= query.To)
            .GroupBy(reproduction => reproduction.StartDate)
            .Select(group => new DateCountProjection(group.Key, group.Count()))
            .ToArrayAsync(cancellationToken);
        var reproductionsCompleted = await dbContext.Reproductions
            .AsNoTracking()
            .Where(reproduction =>
                reproduction.BreedingFarmId == breedingFarmId &&
                reproduction.Status == ReproductionStatus.Finished &&
                reproduction.EndDate != null &&
                reproduction.EndDate >= query.From &&
                reproduction.EndDate <= query.To)
            .GroupBy(reproduction => reproduction.EndDate!.Value)
            .Select(group => new DateCountProjection(group.Key, group.Count()))
            .ToArrayAsync(cancellationToken);
        var internalTransfersIn = await dbContext.InternalTransferRequests
            .AsNoTracking()
            .Where(transfer =>
                transfer.DestinationBreedingFarmId == breedingFarmId &&
                transfer.Status == InternalTransferRequestStatus.Accepted &&
                transfer.UpdatedAtUtc >= fromUtc &&
                transfer.UpdatedAtUtc < toExclusiveUtc)
            .GroupBy(transfer => transfer.UpdatedAtUtc.DateTime.Date)
            .Select(group => new TimestampCountProjection(group.Key, group.Count()))
            .ToArrayAsync(cancellationToken);
        var internalTransfersOut = await dbContext.InternalTransferRequests
            .AsNoTracking()
            .Where(transfer =>
                transfer.SourceBreedingFarmId == breedingFarmId &&
                transfer.Status == InternalTransferRequestStatus.Accepted &&
                transfer.UpdatedAtUtc >= fromUtc &&
                transfer.UpdatedAtUtc < toExclusiveUtc)
            .GroupBy(transfer => transfer.UpdatedAtUtc.DateTime.Date)
            .Select(group => new TimestampCountProjection(group.Key, group.Count()))
            .ToArrayAsync(cancellationToken);
        var externalTransfersOut = await dbContext.ExternalTransfers
            .AsNoTracking()
            .Where(transfer =>
                transfer.BreedingFarmId == breedingFarmId &&
                transfer.CreatedAtUtc >= fromUtc &&
                transfer.CreatedAtUtc < toExclusiveUtc)
            .GroupBy(transfer => transfer.CreatedAtUtc.DateTime.Date)
            .Select(group => new TimestampCountProjection(group.Key, group.Count()))
            .ToArrayAsync(cancellationToken);

        var incomingStatusCounts = await dbContext.InternalTransferRequests
            .AsNoTracking()
            .Where(transfer =>
                transfer.DestinationBreedingFarmId == breedingFarmId &&
                transfer.CreatedAtUtc >= fromUtc &&
                transfer.CreatedAtUtc < toExclusiveUtc)
            .GroupBy(transfer => transfer.Status)
            .Select(group => new TransferStatusCountProjection(group.Key, group.Count()))
            .ToArrayAsync(cancellationToken);
        var outgoingStatusCounts = await dbContext.InternalTransferRequests
            .AsNoTracking()
            .Where(transfer =>
                transfer.SourceBreedingFarmId == breedingFarmId &&
                transfer.CreatedAtUtc >= fromUtc &&
                transfer.CreatedAtUtc < toExclusiveUtc)
            .GroupBy(transfer => transfer.Status)
            .Select(group => new TransferStatusCountProjection(group.Key, group.Count()))
            .ToArrayAsync(cancellationToken);

        var registeredByDay = ToDateCounts(registeredBirdCounts);
        var birthsByDay = birthsByDate.ToDictionary(group => group.Date, group => group.Count);
        var startedByDay = reproductionsStarted.ToDictionary(group => group.Date, group => group.Count);
        var completedByDay = reproductionsCompleted.ToDictionary(group => group.Date, group => group.Count);
        var internalInByDay = ToDateCounts(internalTransfersIn);
        var internalOutByDay = ToDateCounts(internalTransfersOut);
        var externalOutByDay = ToDateCounts(externalTransfersOut);
        var daily = BuildDailyStatistics(
            query.From,
            query.To,
            registeredByDay,
            birthsByDay,
            startedByDay,
            completedByDay,
            internalInByDay,
            internalOutByDay,
            externalOutByDay);

        return GetBreedingFarmStatisticsResult.Succeeded(
            new BreedingFarmStatisticsData(
                breedingFarmId,
                query.From,
                query.To,
                Enum.GetValues<BirdStatus>()
                    .Select(status => new BirdStatusCount(
                        status.ToString(),
                        birdsByStatusGroups.SingleOrDefault(group => group.Status == status)?.Count ?? 0))
                    .ToArray(),
                Enum.GetValues<BirdSex>()
                    .Select(sex => new BirdSexCount(
                        sex.ToString(),
                        birdsBySexGroups.SingleOrDefault(group => group.Sex == sex)?.Count ?? 0))
                    .ToArray(),
                species.Select(candidate => new BirdSpeciesCount(
                    candidate.Id,
                    candidate.PopularName,
                    candidate.ScientificName,
                    speciesCounts.GetValueOrDefault(candidate.Id)))
                    .ToArray(),
                daily,
                new BreedingFarmTransferStatistics(
                    daily.Sum(item => item.InternalTransfersInCount),
                    daily.Sum(item => item.InternalTransfersOutCount),
                    daily.Sum(item => item.ExternalTransfersOutCount),
                    BuildTransferStatusCounts(incomingStatusCounts),
                    BuildTransferStatusCounts(outgoingStatusCounts))));
    }

    private static Dictionary<DateOnly, int> ToDateCounts(
        IReadOnlyCollection<TimestampCountProjection> counts) =>
        counts.ToDictionary(
            group => DateOnly.FromDateTime(group.Date),
            group => group.Count);

    private static IReadOnlyCollection<DailyBreedingFarmStatistics> BuildDailyStatistics(
        DateOnly from,
        DateOnly to,
        IReadOnlyDictionary<DateOnly, int> registeredBirds,
        IReadOnlyDictionary<DateOnly, int> births,
        IReadOnlyDictionary<DateOnly, int> reproductionsStarted,
        IReadOnlyDictionary<DateOnly, int> reproductionsCompleted,
        IReadOnlyDictionary<DateOnly, int> internalTransfersIn,
        IReadOnlyDictionary<DateOnly, int> internalTransfersOut,
        IReadOnlyDictionary<DateOnly, int> externalTransfersOut)
    {
        var result = new List<DailyBreedingFarmStatistics>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            result.Add(new DailyBreedingFarmStatistics(
                date,
                GetCount(registeredBirds, date),
                GetCount(births, date),
                GetCount(reproductionsStarted, date),
                GetCount(reproductionsCompleted, date),
                GetCount(internalTransfersIn, date),
                GetCount(internalTransfersOut, date),
                GetCount(externalTransfersOut, date)));
        }

        return result;
    }

    private static int GetCount(IReadOnlyDictionary<DateOnly, int> counts, DateOnly date) =>
        counts.GetValueOrDefault(date);

    private static IReadOnlyCollection<TransferRequestStatusCount> BuildTransferStatusCounts(
        IReadOnlyCollection<TransferStatusCountProjection> counts) =>
        Enum.GetValues<InternalTransferRequestStatus>()
            .Select(status => new TransferRequestStatusCount(
                status.ToString(),
                counts.SingleOrDefault(group => group.Status == status)?.Count ?? 0))
            .ToArray();

    private sealed record BirdStatusCountProjection(BirdStatus Status, int Count);

    private sealed record BirdSexCountProjection(BirdSex Sex, int Count);

    private sealed record SpeciesCountProjection(Guid SpeciesId, int Count);

    private sealed record SpeciesProjection(Guid Id, string PopularName, string ScientificName);

    private sealed record TimestampCountProjection(DateTime Date, int Count);

    private sealed record DateCountProjection(DateOnly Date, int Count);

    private sealed record TransferStatusCountProjection(InternalTransferRequestStatus Status, int Count);
}
