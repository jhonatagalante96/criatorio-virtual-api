using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Transfers;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Transfers;

public sealed class GetInternalTransferRequestQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<GetInternalTransferRequestQuery, GetInternalTransferRequestResult>
{
    public async Task<GetInternalTransferRequestResult> Handle(
        GetInternalTransferRequestQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);
        if (user is null)
        {
            return GetInternalTransferRequestResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return GetInternalTransferRequestResult.BreedingFarmNotSelected();
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
            return GetInternalTransferRequestResult.BreedingFarmNotFound();
        }

        var transferRequest = await dbContext.InternalTransferRequests
            .AsNoTracking()
            .Where(candidate =>
                candidate.Id == query.TransferRequestId &&
                (candidate.SourceBreedingFarmId == breedingFarmId ||
                 candidate.DestinationBreedingFarmId == breedingFarmId))
            .Join(
                dbContext.BreedingFarms.AsNoTracking(),
                candidate => candidate.SourceBreedingFarmId,
                farm => farm.Id,
                (candidate, sourceFarm) => new { candidate, sourceFarm })
            .Join(
                dbContext.BreedingFarms.AsNoTracking(),
                row => row.candidate.DestinationBreedingFarmId,
                farm => farm.Id,
                (row, destinationFarm) => new InternalTransferDetailsProjection(
                    row.candidate.Id,
                    row.candidate.BirdId,
                    row.candidate.BirdSnapshotName,
                    row.candidate.BirdSnapshotSex,
                    row.candidate.BirdSnapshotRingNumber,
                    row.candidate.BirdSnapshotStatus,
                    row.candidate.SourceBreedingFarmId,
                    row.sourceFarm.Name,
                    row.candidate.DestinationBreedingFarmId,
                    destinationFarm.Name,
                    row.candidate.Status,
                    row.candidate.CreatedAtUtc,
                    row.candidate.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
        if (transferRequest is null)
        {
            return GetInternalTransferRequestResult.TransferRequestNotFound();
        }

        return GetInternalTransferRequestResult.Succeeded(
            new InternalTransferDetailsResult(
                transferRequest.TransferRequestId,
                transferRequest.BirdId,
                transferRequest.BirdName,
                transferRequest.BirdSex,
                transferRequest.RingNumber,
                transferRequest.BirdStatus,
                transferRequest.SourceBreedingFarmId,
                transferRequest.SourceBreedingFarmName,
                transferRequest.DestinationBreedingFarmId,
                transferRequest.DestinationBreedingFarmName,
                transferRequest.Status,
                transferRequest.CreatedAtUtc,
                transferRequest.UpdatedAtUtc));
    }

    private sealed record InternalTransferDetailsProjection(
        Guid TransferRequestId,
        Guid BirdId,
        string BirdName,
        CriatorioVirtual.Domain.Birds.BirdSex BirdSex,
        string? RingNumber,
        CriatorioVirtual.Domain.Birds.BirdStatus BirdStatus,
        Guid SourceBreedingFarmId,
        string SourceBreedingFarmName,
        Guid DestinationBreedingFarmId,
        string DestinationBreedingFarmName,
        CriatorioVirtual.Domain.Transfers.InternalTransferRequestStatus Status,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);
}
