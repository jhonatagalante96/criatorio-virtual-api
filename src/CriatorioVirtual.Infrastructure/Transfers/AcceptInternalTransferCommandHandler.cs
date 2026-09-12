using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Transfers;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Transfers;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Transfers;

public sealed class AcceptInternalTransferCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<AcceptInternalTransferCommand, AcceptInternalTransferResult>
{
    public async Task<AcceptInternalTransferResult> Handle(
        AcceptInternalTransferCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return AcceptInternalTransferResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return AcceptInternalTransferResult.BreedingFarmNotSelected();
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
            return AcceptInternalTransferResult.BreedingFarmNotFound();
        }

        // Serialize acceptance attempts for this destination-side request before reading its status.
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
            return AcceptInternalTransferResult.TransferRequestNotFound();
        }

        if (transferRequest.Status != InternalTransferRequestStatus.Pending)
        {
            return AcceptInternalTransferResult.TransferNotPending();
        }

        var bird = await dbContext.Birds
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == transferRequest.BirdId &&
                    candidate.BreedingFarmId == transferRequest.SourceBreedingFarmId,
                cancellationToken);
        if (bird is null)
        {
            return AcceptInternalTransferResult.BirdNotFound();
        }

        if (bird.Status != BirdStatus.Transferred)
        {
            return AcceptInternalTransferResult.InvalidState();
        }

        var rootNode = await dbContext.GenealogyNodes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.BirdId == bird.Id && candidate.IsRoot,
                cancellationToken);
        if (rootNode is null || rootNode.BreedingFarmId is null)
        {
            return AcceptInternalTransferResult.InvalidState();
        }

        var genealogyNodes = await dbContext.GenealogyNodes
            .AsNoTracking()
            .Where(candidate => candidate.GenealogyRootId == rootNode.GenealogyRootId)
            .ToArrayAsync(cancellationToken);

        if (genealogyNodes.Any(candidate =>
                !candidate.IsRoot &&
                (candidate.BreedingFarmId is null ||
                 candidate.LinkedBirdId is null ||
                 candidate.SnapshotName is null ||
                 candidate.SnapshotSex is null ||
                 candidate.SnapshotStatus is null)))
        {
            return AcceptInternalTransferResult.InvalidState();
        }

        var now = DateTimeOffset.UtcNow;
        bird.CompleteInternalTransfer(destinationBreedingFarmId, now);
        transferRequest.Accept(now);
        rootNode.MoveRootToBreedingFarm(destinationBreedingFarmId, now);

        // The root has a composite FK containing the bird's farm. Rebuild the root inside
        // the existing command transaction so both records remain valid at every SaveChanges boundary.
        await dbContext.GenealogyNodes
            .Where(candidate => candidate.GenealogyRootId == rootNode.GenealogyRootId)
            .ExecuteDeleteAsync(cancellationToken);

        // Bird.BreedingFarmId is part of an alternate key used by the tenant-scoped foreign
        // keys, so EF Core cannot update it on a tracked entity. The genealogy dependents are
        // already removed above; move the bird with a guarded SQL update in the same transaction.
        var updatedBirds = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE app.birds
            SET "BreedingFarmId" = {destinationBreedingFarmId},
                "FatherBirdId" = NULL,
                "MotherBirdId" = NULL,
                "Status" = {(int)BirdStatus.Active},
                "UpdatedAtUtc" = {now}
            WHERE "Id" = {bird.Id}
              AND "BreedingFarmId" = {transferRequest.SourceBreedingFarmId}
              AND "Status" = {(int)BirdStatus.Transferred};
            """,
            cancellationToken);
        if (updatedBirds != 1)
        {
            throw new DbUpdateConcurrencyException(
                "The bird was changed before the internal transfer could be accepted.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.GenealogyNodes.Add(new GenealogyNode(
            rootNode.Id,
            rootNode.CreatedAtUtc,
            rootNode.BreedingFarmId!.Value,
            bird.Id));
        foreach (var node in genealogyNodes.Where(candidate => !candidate.IsRoot))
        {
            dbContext.GenealogyNodes.Add(new GenealogyNode(
                node.Id,
                node.CreatedAtUtc,
                node.BreedingFarmId!.Value,
                node.GenealogyRootId,
                node.Position,
                node.LinkedBirdId!.Value,
                node.SnapshotName!,
                node.SnapshotSex!.Value,
                node.SnapshotBirthDate,
                node.SnapshotRingNumber,
                node.SnapshotStatus!.Value));
        }

        return AcceptInternalTransferResult.Accepted(ToResult(transferRequest));
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
