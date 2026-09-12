using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Transfers;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Transfers;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Transfers;

public sealed class RejectInternalTransferCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<RejectInternalTransferCommand, RejectInternalTransferResult>
{
    public async Task<RejectInternalTransferResult> Handle(
        RejectInternalTransferCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return RejectInternalTransferResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return RejectInternalTransferResult.BreedingFarmNotSelected();
        }

        var destinationBreedingFarmId = user.SelectedBreedingFarmId.Value;
        var hasActiveOwnerMembership = await dbContext.BreedingFarmUsers
            .AsNoTracking()
            .AnyAsync(
                membership =>
                    membership.BreedingFarmId == destinationBreedingFarmId &&
                    membership.UserId == command.UserId &&
                    membership.IsActive &&
                    membership.Role == BreedingFarmRole.Owner,
                cancellationToken);
        if (!hasActiveOwnerMembership)
        {
            return RejectInternalTransferResult.BreedingFarmNotFound();
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM app.internal_transfer_requests WHERE \"Id\" = {command.TransferRequestId} AND \"DestinationBreedingFarmId\" = {destinationBreedingFarmId} FOR UPDATE",
            cancellationToken);

        var transferRequest = await dbContext.InternalTransferRequests
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == command.TransferRequestId &&
                    candidate.DestinationBreedingFarmId == destinationBreedingFarmId,
                cancellationToken);
        if (transferRequest is null)
        {
            return RejectInternalTransferResult.TransferRequestNotFound();
        }

        if (transferRequest.Status != InternalTransferRequestStatus.Pending)
        {
            return RejectInternalTransferResult.TransferNotPending();
        }

        var bird = await dbContext.Birds
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == transferRequest.BirdId &&
                    candidate.BreedingFarmId == transferRequest.SourceBreedingFarmId,
                cancellationToken);
        if (bird is null)
        {
            return RejectInternalTransferResult.BirdNotFound();
        }

        if (bird.Status != BirdStatus.Transferred)
        {
            return RejectInternalTransferResult.InvalidState();
        }

        var now = DateTimeOffset.UtcNow;
        bird.CancelInternalTransfer(now);
        transferRequest.Reject(now);

        return RejectInternalTransferResult.Rejected(ToResult(transferRequest));
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
