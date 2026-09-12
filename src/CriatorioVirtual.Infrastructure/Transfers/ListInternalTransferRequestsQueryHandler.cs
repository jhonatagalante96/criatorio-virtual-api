using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Transfers;
using CriatorioVirtual.Domain.Transfers;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Transfers;

public sealed class ListInternalTransferRequestsQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<ListInternalTransferRequestsQuery, ListInternalTransferRequestsResult>
{
    public async Task<ListInternalTransferRequestsResult> Handle(
        ListInternalTransferRequestsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);
        if (user is null)
        {
            return ListInternalTransferRequestsResult.UserNotFound(query.Direction);
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return ListInternalTransferRequestsResult.BreedingFarmNotSelected(query.Direction);
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
            return ListInternalTransferRequestsResult.BreedingFarmNotFound(query.Direction);
        }

        var transfers = dbContext.InternalTransferRequests
            .AsNoTracking()
            .Where(transferRequest => query.Direction == InternalTransferDirection.Sent
                ? transferRequest.SourceBreedingFarmId == breedingFarmId
                : transferRequest.DestinationBreedingFarmId == breedingFarmId);
        if (query.Status is not null)
        {
            transfers = transfers.Where(transferRequest => transferRequest.Status == query.Status.Value);
        }

        var totalCount = await transfers.CountAsync(cancellationToken);
        var skip = (long)(query.Page - 1) * query.PageSize;
        InternalTransferListProjection[] rows;
        if (skip > int.MaxValue)
        {
            rows = [];
        }
        else
        {
            rows = await transfers
                .OrderByDescending(transferRequest => transferRequest.CreatedAtUtc)
                .ThenByDescending(transferRequest => transferRequest.Id)
                .Skip((int)skip)
                .Take(query.PageSize)
                .Join(
                    dbContext.Birds.AsNoTracking(),
                    transferRequest => transferRequest.BirdId,
                    bird => bird.Id,
                    (transferRequest, bird) => new { transferRequest, bird })
                .Join(
                    dbContext.BreedingFarms.AsNoTracking(),
                    row => row.transferRequest.SourceBreedingFarmId,
                    farm => farm.Id,
                    (row, sourceFarm) => new { row.transferRequest, row.bird, sourceFarm })
                .Join(
                    dbContext.BreedingFarms.AsNoTracking(),
                    row => row.transferRequest.DestinationBreedingFarmId,
                    farm => farm.Id,
                    (row, destinationFarm) => new InternalTransferListProjection(
                        row.transferRequest.Id,
                        row.transferRequest.BirdId,
                        row.bird.Name,
                        row.bird.RingNumber,
                        row.transferRequest.SourceBreedingFarmId,
                        row.sourceFarm.Name,
                        row.transferRequest.DestinationBreedingFarmId,
                        destinationFarm.Name,
                        row.transferRequest.Status,
                        row.transferRequest.CreatedAtUtc,
                        row.transferRequest.UpdatedAtUtc))
                .ToArrayAsync(cancellationToken);
        }

        return ListInternalTransferRequestsResult.Succeeded(
            breedingFarmId,
            query.Direction,
            rows.Select(row => new InternalTransferListItemResult(
                row.TransferRequestId,
                row.BirdId,
                row.BirdName,
                row.RingNumber,
                row.SourceBreedingFarmId,
                row.SourceBreedingFarmName,
                row.DestinationBreedingFarmId,
                row.DestinationBreedingFarmName,
                row.Status,
                row.CreatedAtUtc,
                row.UpdatedAtUtc)).ToArray(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    private sealed record InternalTransferListProjection(
        Guid TransferRequestId,
        Guid BirdId,
        string BirdName,
        string? RingNumber,
        Guid SourceBreedingFarmId,
        string SourceBreedingFarmName,
        Guid DestinationBreedingFarmId,
        string DestinationBreedingFarmName,
        InternalTransferRequestStatus Status,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);
}
