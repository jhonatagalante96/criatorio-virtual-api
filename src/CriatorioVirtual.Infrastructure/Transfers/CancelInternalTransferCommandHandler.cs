using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Transfers;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Transfers;
using CriatorioVirtual.Infrastructure.Birds;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Transfers;

public sealed class CancelInternalTransferCommandHandler(
    CriatorioVirtualDbContext dbContext,
    IBirdLockCoordinator birdLockCoordinator)
    : ICommandHandler<CancelInternalTransferCommand, CancelInternalTransferResult>
{
    public async Task<CancelInternalTransferResult> Handle(
        CancelInternalTransferCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return CancelInternalTransferResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return CancelInternalTransferResult.BreedingFarmNotSelected();
        }

        var sourceBreedingFarmId = user.SelectedBreedingFarmId.Value;
        var hasActiveOwnerMembership = await dbContext.BreedingFarmUsers
            .AsNoTracking()
            .AnyAsync(
                membership =>
                    membership.BreedingFarmId == sourceBreedingFarmId &&
                    membership.UserId == command.UserId &&
                    membership.IsActive &&
                    membership.Role == BreedingFarmRole.Owner,
                cancellationToken);
        if (!hasActiveOwnerMembership)
        {
            return CancelInternalTransferResult.BreedingFarmNotFound();
        }

        // Inspect transfer request metadata to resolve BirdId and SourceBreedingFarmId before locking.
        var requestInfo = await dbContext.InternalTransferRequests
            .AsNoTracking()
            .Where(
                candidate =>
                    candidate.Id == command.TransferRequestId &&
                    candidate.SourceBreedingFarmId == sourceBreedingFarmId &&
                    candidate.RequestedByUserId == command.UserId)
            .Select(
                candidate => new
                {
                    candidate.BirdId,
                    candidate.SourceBreedingFarmId
                })
            .SingleOrDefaultAsync(cancellationToken);
        if (requestInfo is null)
        {
            return CancelInternalTransferResult.TransferRequestNotFound();
        }

        // Lock bird row first, then transfer request row, maintaining unified lock order across mutations.
        await birdLockCoordinator.AcquireLockAsync(
            requestInfo.BirdId,
            sourceBreedingFarmId,
            cancellationToken);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM app.internal_transfer_requests WHERE \"Id\" = {command.TransferRequestId} AND \"SourceBreedingFarmId\" = {sourceBreedingFarmId} AND \"RequestedByUserId\" = {command.UserId} FOR UPDATE",
            cancellationToken);

        var transferRequest = await dbContext.InternalTransferRequests
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == command.TransferRequestId &&
                    candidate.SourceBreedingFarmId == sourceBreedingFarmId &&
                    candidate.RequestedByUserId == command.UserId,
                cancellationToken);
        if (transferRequest is null)
        {
            return CancelInternalTransferResult.TransferRequestNotFound();
        }

        if (transferRequest.Status != InternalTransferRequestStatus.Pending)
        {
            return CancelInternalTransferResult.TransferNotPending();
        }

        var bird = await dbContext.Birds
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == transferRequest.BirdId &&
                    candidate.BreedingFarmId == transferRequest.SourceBreedingFarmId,
                cancellationToken);
        if (bird is null)
        {
            return CancelInternalTransferResult.BirdNotFound();
        }

        if (bird.Status != BirdStatus.Transferred)
        {
            return CancelInternalTransferResult.InvalidState();
        }

        var now = DateTimeOffset.UtcNow;
        bird.CancelInternalTransfer(now);
        transferRequest.Cancel(now);

        return CancelInternalTransferResult.Cancelled(ToResult(transferRequest));
    }

    private static InternalTransferRequestResult ToResult(InternalTransferRequest transferRequest) =>
        new(
            transferRequest.Id,
            transferRequest.BirdId,
            transferRequest.SourceBreedingFarmId,
            transferRequest.DestinationBreedingFarmId,
            transferRequest.RequestedByUserId,
            transferRequest.Status.ToString(),
            transferRequest.CreatedAtUtc,
            transferRequest.UpdatedAtUtc);
}
