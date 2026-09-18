using Amazon.S3;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Application.Transfers;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Transfers;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Transfers;

public sealed class AcceptInternalTransferCommandHandler(
    CriatorioVirtualDbContext dbContext,
    IPrivateObjectStorage storage,
    AcceptInternalTransferSession session)
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

        // Serialize operations on the bird row across tenants.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM app.birds WHERE \"Id\" = {transferRequest.BirdId} AND \"BreedingFarmId\" = {transferRequest.SourceBreedingFarmId} FOR UPDATE",
            cancellationToken);

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
        var externalGenealogyNodes = await dbContext.ExternalGenealogyNodes
            .AsNoTracking()
            .Where(candidate => candidate.GenealogyRootId == rootNode.GenealogyRootId)
            .ToArrayAsync(cancellationToken);
        var externalParentLinks = await dbContext.ExternalGenealogyParentLinks
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

        var primaryPhotoId = bird.PrimaryPhotoId;
        var attachments = await dbContext.BirdAttachments
            .AsNoTracking()
            .Where(candidate =>
                candidate.BreedingFarmId == transferRequest.SourceBreedingFarmId &&
                candidate.BirdId == bird.Id)
            .ToArrayAsync(cancellationToken);

        // Move private storage objects to the destination tenant prefix.
        foreach (var attachment in attachments)
        {
            if (attachment.DeletedAtUtc is null)
            {
                try
                {
                    await storage.MoveAsync(
                        transferRequest.SourceBreedingFarmId,
                        destinationBreedingFarmId,
                        attachment.ObjectKey,
                        cancellationToken);
                    session.TrackMovedObject(
                        transferRequest.SourceBreedingFarmId,
                        destinationBreedingFarmId,
                        attachment.ObjectKey);
                }
                catch (Exception exception) when (IsStorageException(exception))
                {
                    await session.CompensateAsync(cancellationToken);
                    return AcceptInternalTransferResult.StorageUnavailable();
                }
            }
        }

        var now = DateTimeOffset.UtcNow;
        bird.CompleteInternalTransfer(destinationBreedingFarmId, now);
        transferRequest.Accept(now);
        rootNode.MoveRootToBreedingFarm(destinationBreedingFarmId, now);

        // The root has a composite FK containing the bird's farm. Rebuild the root inside
        // the existing command transaction so both records remain valid at every SaveChanges boundary.
        await dbContext.ExternalGenealogyParentLinks
            .Where(candidate => candidate.GenealogyRootId == rootNode.GenealogyRootId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.ExternalGenealogyNodes
            .Where(candidate => candidate.GenealogyRootId == rootNode.GenealogyRootId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.GenealogyNodes
            .Where(candidate => candidate.GenealogyRootId == rootNode.GenealogyRootId)
            .ExecuteDeleteAsync(cancellationToken);

        // If the bird has a primary photo, temporarily clear it so the composite FK from birds
        // to bird_attachments does not fail while the bird and attachments are being moved.
        if (primaryPhotoId is not null)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE app.birds
                SET "PrimaryPhotoId" = NULL
                WHERE "Id" = {bird.Id}
                  AND "BreedingFarmId" = {transferRequest.SourceBreedingFarmId};
                """,
                cancellationToken);
        }

        // Temporarily detach BirdId on the bird's attachments so the composite FK from bird_attachments
        // to birds does not fail when the bird's BreedingFarmId is updated.
        var attachmentIds = attachments.Select(candidate => candidate.Id).ToArray();
        if (attachmentIds.Length > 0)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE app.bird_attachments
                SET "BirdId" = NULL
                WHERE "BreedingFarmId" = {transferRequest.SourceBreedingFarmId}
                  AND "BirdId" = {bird.Id};
                """,
                cancellationToken);
        }

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

        // Move the bird's attachments to destination breeding farm and re-link BirdId.
        if (attachmentIds.Length > 0)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE app.bird_attachments
                SET "BreedingFarmId" = {destinationBreedingFarmId},
                    "BirdId" = {bird.Id},
                    "UpdatedAtUtc" = {now}
                WHERE "BreedingFarmId" = {transferRequest.SourceBreedingFarmId}
                  AND "Id" = ANY({attachmentIds});
                """,
                cancellationToken);
        }

        // Restore the primary photo on the bird with the new breeding farm scope.
        if (primaryPhotoId is not null)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE app.birds
                SET "PrimaryPhotoId" = {primaryPhotoId}
                WHERE "Id" = {bird.Id}
                  AND "BreedingFarmId" = {destinationBreedingFarmId};
                """,
                cancellationToken);
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

        foreach (var node in externalGenealogyNodes)
        {
            dbContext.ExternalGenealogyNodes.Add(node.IsBirdSnapshot
                ? ExternalGenealogyNode.CreateBirdSnapshot(
                    node.Id,
                    node.CreatedAtUtc,
                    destinationBreedingFarmId,
                    node.GenealogyRootId,
                    node.Name,
                    node.Sex,
                    node.SnapshotSourceBirdId,
                    node.SnapshotBirthDate,
                    node.SnapshotRingNumber,
                    node.SnapshotStatus!.Value,
                    node.CanNavigateToSourceBird)
                : new ExternalGenealogyNode(
                    node.Id,
                    node.CreatedAtUtc,
                    destinationBreedingFarmId,
                    node.GenealogyRootId,
                    node.Name,
                    node.Sex));
        }

        foreach (var link in externalParentLinks)
        {
            dbContext.ExternalGenealogyParentLinks.Add(new ExternalGenealogyParentLink(
                link.Id,
                link.CreatedAtUtc,
                destinationBreedingFarmId,
                link.GenealogyRootId,
                link.ChildBirdId,
                link.ChildExternalNodeId,
                link.Position,
                link.ParentBirdId,
                link.ParentExternalNodeId,
                link.ParentSourceBreedingFarmId,
                link.ParentSnapshotName,
                link.ParentSnapshotSex,
                link.ParentSnapshotBirthDate,
                link.ParentSnapshotRingNumber,
                link.ParentSnapshotStatus));
        }

        session.MarkCommitted();
        return AcceptInternalTransferResult.Accepted(ToResult(transferRequest));
    }

    private static bool IsStorageException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or FileNotFoundException or AmazonS3Exception;

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
