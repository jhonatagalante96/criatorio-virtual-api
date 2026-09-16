using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

public sealed class UpdateExternalGenealogyParentCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<UpdateExternalGenealogyParentCommand, UpdateExternalGenealogyParentResult>
{
    public async Task<UpdateExternalGenealogyParentResult> Handle(
        UpdateExternalGenealogyParentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var position = NormalizePosition(command.Position);
        var externalName = Normalize(command.ExternalName);
        if (position is null ||
            (command.LinkedBirdId is null) == (externalName is null) ||
            command.LinkedBirdId == Guid.Empty ||
            externalName?.Length > ExternalGenealogyNode.NameMaxLength)
        {
            return UpdateExternalGenealogyParentResult.InvalidData();
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return UpdateExternalGenealogyParentResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return UpdateExternalGenealogyParentResult.BreedingFarmNotSelected();
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
            return UpdateExternalGenealogyParentResult.BreedingFarmNotFound();
        }

        if (membership.Role != BreedingFarmRole.Owner)
        {
            return UpdateExternalGenealogyParentResult.Forbidden();
        }

        var bird = await dbContext.Birds
            .SingleOrDefaultAsync(
                candidate => candidate.Id == command.BirdId && candidate.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (bird is null)
        {
            return UpdateExternalGenealogyParentResult.BirdNotFound();
        }

        if (bird.Status == BirdStatus.Transferred)
        {
            return UpdateExternalGenealogyParentResult.TransferPending();
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
            return UpdateExternalGenealogyParentResult.BirdNotFound();
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM app.genealogy_nodes WHERE \"Id\" = {root.Id} FOR UPDATE",
            cancellationToken);

        var ancestor = await dbContext.ExternalGenealogyNodes
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == command.AncestorId &&
                    candidate.BreedingFarmId == breedingFarmId &&
                    candidate.GenealogyRootId == root.Id &&
                    !candidate.IsBirdSnapshot,
                cancellationToken);
        if (ancestor is null)
        {
            return UpdateExternalGenealogyParentResult.AncestorNotFound();
        }

        var existingLink = await dbContext.ExternalGenealogyParentLinks
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.BreedingFarmId == breedingFarmId &&
                    candidate.GenealogyRootId == root.Id &&
                    candidate.ChildExternalNodeId == ancestor.Id &&
                    candidate.Position == position,
                cancellationToken);

        var expectedSex = position == ExternalGenealogyParentLink.FatherPosition
            ? BirdSex.Male
            : BirdSex.Female;

        if (command.LinkedBirdId is { } linkedBirdId)
        {
            if (linkedBirdId == command.BirdId)
            {
                return UpdateExternalGenealogyParentResult.CycleDetected();
            }

            var parentBird = await dbContext.Birds
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == linkedBirdId && candidate.BreedingFarmId == breedingFarmId,
                    cancellationToken);
            if (parentBird is null)
            {
                return UpdateExternalGenealogyParentResult.ParentNotFound();
            }

            if (parentBird.Sex != expectedSex)
            {
                return UpdateExternalGenealogyParentResult.ParentSexInvalid();
            }

            if (existingLink?.ParentExternalNodeId is { } existingSnapshotId &&
                await dbContext.ExternalGenealogyNodes.AnyAsync(
                    candidate => candidate.Id == existingSnapshotId &&
                        candidate.IsBirdSnapshot &&
                        candidate.SnapshotSourceBirdId == parentBird.Id,
                    cancellationToken))
            {
                return UpdateExternalGenealogyParentResult.Updated();
            }

            if (await CreatesCycleAsync(
                    ancestor.Id,
                    linkedBirdId,
                    breedingFarmId,
                    root.Id,
                    cancellationToken))
            {
                return UpdateExternalGenealogyParentResult.CycleDetected();
            }

            if (existingLink?.ParentBirdId == parentBird.Id &&
                existingLink.ParentExternalNodeId is null &&
                existingLink.ParentSnapshotName == parentBird.Name &&
                existingLink.ParentSnapshotSex == parentBird.Sex &&
                existingLink.ParentSnapshotBirthDate == parentBird.BirthDate &&
                existingLink.ParentSnapshotRingNumber == parentBird.RingNumber &&
                existingLink.ParentSnapshotStatus == parentBird.Status)
            {
                return UpdateExternalGenealogyParentResult.Updated();
            }

            if (existingLink is not null)
            {
                await dbContext.ExternalGenealogyParentLinks
                    .Where(link => link.Id == existingLink.Id)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            await BirdGenealogySnapshotMaterializer.AddLinkedParentAsync(
                dbContext,
                command.UserId,
                breedingFarmId,
                root.Id,
                command.BirdId,
                ancestor.Id,
                parentBird,
                position,
                DateTimeOffset.UtcNow,
                cancellationToken);
            await ExternalGenealogyTreeCleanup.PruneUnreachableAsync(
                dbContext,
                root.Id,
                command.BirdId,
                cancellationToken);
            return UpdateExternalGenealogyParentResult.Updated();
        }

        if (existingLink?.ParentExternalNodeId is { } existingExternalId &&
            await dbContext.ExternalGenealogyNodes.AnyAsync(
                candidate => candidate.Id == existingExternalId &&
                    !candidate.IsBirdSnapshot &&
                    candidate.Name == externalName,
                cancellationToken))
        {
            return UpdateExternalGenealogyParentResult.Updated();
        }

        if (existingLink is not null)
        {
            await dbContext.ExternalGenealogyParentLinks
                .Where(link => link.Id == existingLink.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }

        var newExternalNode = new ExternalGenealogyNode(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            breedingFarmId,
            root.Id,
            externalName!,
            expectedSex);
        dbContext.ExternalGenealogyNodes.Add(newExternalNode);
        dbContext.ExternalGenealogyParentLinks.Add(new ExternalGenealogyParentLink(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            breedingFarmId,
            root.Id,
            null,
            ancestor.Id,
            position,
            null,
            newExternalNode.Id,
            null,
            null,
            null,
            null,
            null,
            null));

        await ExternalGenealogyTreeCleanup.PruneUnreachableAsync(
            dbContext,
            root.Id,
            command.BirdId,
            cancellationToken);

        return UpdateExternalGenealogyParentResult.Updated();
    }

    private static string? NormalizePosition(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is ExternalGenealogyParentLink.FatherPosition or ExternalGenealogyParentLink.MotherPosition
            ? normalized
            : null;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<bool> CreatesCycleAsync(
        Guid ancestorId,
        Guid linkedBirdId,
        Guid breedingFarmId,
        Guid genealogyRootId,
        CancellationToken cancellationToken)
    {
        var birds = await dbContext.Birds
            .AsNoTracking()
            .Where(candidate => candidate.BreedingFarmId == breedingFarmId)
            .Select(candidate => new BirdParentProjection(
                candidate.Id,
                candidate.FatherBirdId,
                candidate.MotherBirdId))
            .ToDictionaryAsync(candidate => candidate.BirdId, cancellationToken);
        var links = await dbContext.ExternalGenealogyParentLinks
            .AsNoTracking()
            .Where(candidate => candidate.GenealogyRootId == genealogyRootId)
            .Select(candidate => new ExternalParentLinkProjection(
                candidate.ChildBirdId,
                candidate.ChildExternalNodeId,
                candidate.Position,
                candidate.ParentBirdId,
                candidate.ParentExternalNodeId))
            .ToArrayAsync(cancellationToken);
        var linksByBird = links
            .Where(link => link.ChildBirdId is not null)
            .GroupBy(link => link.ChildBirdId!.Value)
            .ToDictionary(group => group.Key, group => group.ToDictionary(link => link.Position));
        var linksByExternal = links
            .Where(link => link.ChildExternalNodeId is not null)
            .GroupBy(link => link.ChildExternalNodeId!.Value)
            .ToDictionary(group => group.Key, group => group.ToDictionary(link => link.Position));

        var pending = new Stack<GenealogyReference>();
        var visited = new HashSet<GenealogyReference>();
        pending.Push(GenealogyReference.ForBird(linkedBirdId));
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            if (current.ExternalNodeId == ancestorId)
            {
                return true;
            }

            if (current.BirdId is { } birdId)
            {
                if (birds.TryGetValue(birdId, out var bird))
                {
                    linksByBird.TryGetValue(birdId, out var birdLinks);
                    AddBirdParent(pending, birdLinks, ExternalGenealogyParentLink.FatherPosition, bird.FatherBirdId);
                    AddBirdParent(pending, birdLinks, ExternalGenealogyParentLink.MotherPosition, bird.MotherBirdId);
                }

                continue;
            }

            if (current.ExternalNodeId is { } externalNodeId &&
                linksByExternal.TryGetValue(externalNodeId, out var externalLinks))
            {
                AddLinkParent(pending, externalLinks, ExternalGenealogyParentLink.FatherPosition);
                AddLinkParent(pending, externalLinks, ExternalGenealogyParentLink.MotherPosition);
            }
        }

        return false;
    }

    private static void AddLinkParent(
        Stack<GenealogyReference> pending,
        IReadOnlyDictionary<string, ExternalParentLinkProjection> links,
        string position)
    {
        if (!links.TryGetValue(position, out var link))
        {
            return;
        }

        if (link.ParentBirdId is { } parentBirdId)
        {
            pending.Push(GenealogyReference.ForBird(parentBirdId));
        }
        else if (link.ParentExternalNodeId is { } parentExternalNodeId)
        {
            pending.Push(GenealogyReference.ForExternal(parentExternalNodeId));
        }
    }

    private static void AddBirdParent(
        Stack<GenealogyReference> pending,
        IReadOnlyDictionary<string, ExternalParentLinkProjection>? links,
        string position,
        Guid? fallbackBirdId)
    {
        if (links is not null && links.ContainsKey(position))
        {
            AddLinkParent(pending, links, position);
        }
        else if (fallbackBirdId is { } birdId)
        {
            pending.Push(GenealogyReference.ForBird(birdId));
        }
    }

    private sealed record BirdParentProjection(Guid BirdId, Guid? FatherBirdId, Guid? MotherBirdId);

    private sealed record ExternalParentLinkProjection(
        Guid? ChildBirdId,
        Guid? ChildExternalNodeId,
        string Position,
        Guid? ParentBirdId,
        Guid? ParentExternalNodeId);

    private readonly record struct GenealogyReference(Guid? BirdId, Guid? ExternalNodeId)
    {
        public static GenealogyReference ForBird(Guid birdId) => new(birdId, null);
        public static GenealogyReference ForExternal(Guid externalNodeId) => new(null, externalNodeId);
    }
}
