using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Transfers;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Transfers;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Transfers;

public sealed class RequestInternalTransferCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<RequestInternalTransferCommand, RequestInternalTransferResult>
{
    public async Task<RequestInternalTransferResult> Handle(
        RequestInternalTransferCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.BirdId is null ||
            command.BirdId == Guid.Empty ||
            command.DestinationBreedingFarmId is null ||
            command.DestinationBreedingFarmId == Guid.Empty)
        {
            return RequestInternalTransferResult.InvalidData();
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return RequestInternalTransferResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return RequestInternalTransferResult.BreedingFarmNotSelected();
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
            return RequestInternalTransferResult.BreedingFarmNotFound();
        }

        if (!command.Confirmed)
        {
            return RequestInternalTransferResult.ConfirmationRequired();
        }

        var destinationBreedingFarmId = command.DestinationBreedingFarmId.Value;
        if (destinationBreedingFarmId == sourceBreedingFarmId)
        {
            return RequestInternalTransferResult.SameBreedingFarm();
        }

        if (!await dbContext.BreedingFarms
                .AsNoTracking()
                .AnyAsync(farm => farm.Id == destinationBreedingFarmId, cancellationToken))
        {
            return RequestInternalTransferResult.DestinationBreedingFarmNotFound();
        }

        // Serialize internal requests with external completion on the bird row.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM app.birds WHERE \"Id\" = {command.BirdId.Value} AND \"BreedingFarmId\" = {sourceBreedingFarmId} FOR UPDATE",
            cancellationToken);

        var bird = await dbContext.Birds
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == command.BirdId.Value &&
                    candidate.BreedingFarmId == sourceBreedingFarmId,
                cancellationToken);
        if (bird is null)
        {
            return RequestInternalTransferResult.BirdNotFound();
        }

        if (bird.Status == BirdStatus.Transferred ||
            await dbContext.InternalTransferRequests
                .AsNoTracking()
                .AnyAsync(
                    transferRequest =>
                        transferRequest.BirdId == bird.Id &&
                        transferRequest.Status == InternalTransferRequestStatus.Pending,
                    cancellationToken))
        {
            return RequestInternalTransferResult.TransferPending();
        }

        if (!BirdEligibility.Evaluate(bird.RingNumber, bird.Status).IsEligible)
        {
            return RequestInternalTransferResult.BirdNotEligible();
        }

        var snapshotName = bird.Name;
        var snapshotSex = bird.Sex;
        var snapshotRingNumber = bird.RingNumber;
        var snapshotStatus = bird.Status;

        var now = DateTimeOffset.UtcNow;
        try
        {
            bird.MarkTransferPending(now);
        }
        catch (InvalidOperationException)
        {
            return RequestInternalTransferResult.TransferPending();
        }

        var transferRequest = new InternalTransferRequest(
            Guid.NewGuid(),
            now,
            sourceBreedingFarmId,
            destinationBreedingFarmId,
            bird.Id,
            command.UserId,
            snapshotName,
            snapshotSex,
            snapshotRingNumber,
            snapshotStatus);
        dbContext.InternalTransferRequests.Add(transferRequest);

        return RequestInternalTransferResult.Created(ToResult(transferRequest));
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
