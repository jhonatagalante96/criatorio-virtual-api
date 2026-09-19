using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Transfers;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

public sealed class DeleteExternalGenealogyParentCommandHandler(
    CriatorioVirtualDbContext dbContext,
    IBirdLockCoordinator birdLockCoordinator)
    : ICommandHandler<DeleteExternalGenealogyParentCommand, DeleteExternalGenealogyParentResult>
{
    public async Task<DeleteExternalGenealogyParentResult> Handle(
        DeleteExternalGenealogyParentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var position = command.Position?.Trim().ToLowerInvariant();
        if (position is not (ExternalGenealogyParentLink.FatherPosition or ExternalGenealogyParentLink.MotherPosition))
        {
            return DeleteExternalGenealogyParentResult.InvalidData();
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return DeleteExternalGenealogyParentResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return DeleteExternalGenealogyParentResult.BreedingFarmNotSelected();
        }

        var breedingFarmId = user.SelectedBreedingFarmId.Value;
        var membership = await dbContext.BreedingFarmUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.BreedingFarmId == breedingFarmId &&
                    candidate.UserId == command.UserId &&
                    candidate.IsActive,
                cancellationToken);
        if (membership is null)
        {
            return DeleteExternalGenealogyParentResult.BreedingFarmNotFound();
        }

        if (membership.Role != BreedingFarmRole.Owner)
        {
            return DeleteExternalGenealogyParentResult.Forbidden();
        }

        await birdLockCoordinator.AcquireLockAsync(command.BirdId, breedingFarmId, cancellationToken);

        var bird = await dbContext.Birds
            .SingleOrDefaultAsync(
                candidate => candidate.Id == command.BirdId && candidate.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (bird is null)
        {
            return DeleteExternalGenealogyParentResult.BirdNotFound();
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
            return DeleteExternalGenealogyParentResult.TransferPending();
        }

        var root = await dbContext.GenealogyNodes
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.BirdId == command.BirdId &&
                    candidate.IsRoot &&
                    candidate.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (root is null)
        {
            return DeleteExternalGenealogyParentResult.BirdNotFound();
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM app.genealogy_nodes WHERE \"Id\" = {root.Id} FOR UPDATE",
            cancellationToken);

        var ancestorExists = await dbContext.ExternalGenealogyNodes
            .AsNoTracking()
            .AnyAsync(
                candidate =>
                    candidate.Id == command.AncestorId &&
                    candidate.BreedingFarmId == breedingFarmId &&
                    candidate.GenealogyRootId == root.Id &&
                    !candidate.IsBirdSnapshot,
                cancellationToken);
        if (!ancestorExists)
        {
            return DeleteExternalGenealogyParentResult.AncestorNotFound();
        }

        var link = await dbContext.ExternalGenealogyParentLinks
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.BreedingFarmId == breedingFarmId &&
                    candidate.GenealogyRootId == root.Id &&
                    candidate.ChildExternalNodeId == command.AncestorId &&
                    candidate.Position == position,
                cancellationToken);
        if (link is null)
        {
            return DeleteExternalGenealogyParentResult.AncestorNotFound();
        }

        dbContext.ExternalGenealogyParentLinks.Remove(link);
        await ExternalGenealogyTreeCleanup.PruneUnreachableAsync(
            dbContext,
            root.Id,
            command.BirdId,
            cancellationToken);
        return DeleteExternalGenealogyParentResult.Deleted();
    }
}
