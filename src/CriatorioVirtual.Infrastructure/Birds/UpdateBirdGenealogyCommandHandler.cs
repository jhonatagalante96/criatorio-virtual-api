using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

public sealed class UpdateBirdGenealogyCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<UpdateBirdGenealogyCommand, UpdateBirdGenealogyResult>
{
    public async Task<UpdateBirdGenealogyResult> Handle(
        UpdateBirdGenealogyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return UpdateBirdGenealogyResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return UpdateBirdGenealogyResult.BreedingFarmNotSelected();
        }

        var breedingFarmId = user.SelectedBreedingFarmId.Value;
        var hasActiveOwnerMembership = await dbContext.BreedingFarmUsers
            .AsNoTracking()
            .AnyAsync(
                membership =>
                    membership.BreedingFarmId == breedingFarmId &&
                    membership.UserId == command.UserId &&
                    membership.IsActive &&
                    membership.Role == BreedingFarmRole.Owner,
                cancellationToken);
        if (!hasActiveOwnerMembership)
        {
            return UpdateBirdGenealogyResult.BreedingFarmNotFound();
        }

        var bird = await dbContext.Birds
            .SingleOrDefaultAsync(
                candidate => candidate.Id == command.BirdId && candidate.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (bird is null)
        {
            return UpdateBirdGenealogyResult.BirdNotFound();
        }

        if (bird.Status == BirdStatus.Transferred)
        {
            return UpdateBirdGenealogyResult.TransferPending();
        }

        if (command.FatherBirdId is not null && command.FatherBirdId == command.MotherBirdId)
        {
            return UpdateBirdGenealogyResult.DuplicateParent();
        }

        var parentIds = new[] { command.FatherBirdId, command.MotherBirdId }
            .Where(parentId => parentId is not null)
            .Select(parentId => parentId!.Value)
            .Distinct()
            .ToArray();
        var parents = parentIds.Length == 0
            ? []
            : await dbContext.Birds
                .AsNoTracking()
                .Where(candidate => candidate.BreedingFarmId == breedingFarmId && parentIds.Contains(candidate.Id))
                .ToArrayAsync(cancellationToken);

        if (parents.Length != parentIds.Length)
        {
            return UpdateBirdGenealogyResult.ParentNotFound();
        }

        if (command.FatherBirdId is not null &&
            parents.Single(parent => parent.Id == command.FatherBirdId.Value).Sex != BirdSex.Male)
        {
            return UpdateBirdGenealogyResult.ParentSexInvalid();
        }

        if (command.MotherBirdId is not null &&
            parents.Single(parent => parent.Id == command.MotherBirdId.Value).Sex != BirdSex.Female)
        {
            return UpdateBirdGenealogyResult.ParentSexInvalid();
        }

        var parentLinks = await dbContext.Birds
            .AsNoTracking()
            .Where(candidate => candidate.BreedingFarmId == breedingFarmId)
            .Select(candidate => new BirdLinkProjection(
                candidate.Id,
                candidate.FatherBirdId,
                candidate.MotherBirdId))
            .ToDictionaryAsync(candidate => candidate.BirdId, cancellationToken);
        parentLinks[bird.Id] = new BirdLinkProjection(
            bird.Id,
            command.FatherBirdId,
            command.MotherBirdId);

        if (CreatesCycle(bird.Id, command.FatherBirdId, parentLinks) ||
            CreatesCycle(bird.Id, command.MotherBirdId, parentLinks))
        {
            return UpdateBirdGenealogyResult.CycleDetected();
        }

        var normalizedExternalFatherName = Normalize(command.ExternalFatherName);
        var normalizedExternalMotherName = Normalize(command.ExternalMotherName);
        var existingRoot = await dbContext.GenealogyNodes
            .SingleOrDefaultAsync(
                candidate => candidate.BirdId == bird.Id && candidate.IsRoot,
                cancellationToken);
        if (existingRoot is null)
        {
            existingRoot = new GenealogyNode(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                breedingFarmId,
                bird.Id);
            dbContext.GenealogyNodes.Add(existingRoot);
        }

        if (existingRoot.BreedingFarmId is not null)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT \"Id\" FROM app.genealogy_nodes WHERE \"Id\" = {existingRoot.Id} FOR UPDATE",
                cancellationToken);
        }

        var existingNodes = await dbContext.GenealogyNodes
            .AsNoTracking()
            .Where(candidate => candidate.GenealogyRootId == existingRoot.Id && !candidate.IsRoot)
            .ToArrayAsync(cancellationToken);
        var existingExternalLinks = await dbContext.ExternalGenealogyParentLinks
            .AsNoTracking()
            .Where(candidate =>
                candidate.GenealogyRootId == existingRoot.Id &&
                candidate.ChildBirdId == bird.Id)
            .ToArrayAsync(cancellationToken);
        var existingExternalNodeIds = existingExternalLinks
            .Where(candidate => candidate.ParentExternalNodeId is not null)
            .Select(candidate => candidate.ParentExternalNodeId!.Value)
            .ToArray();
        var existingExternalNodes = existingExternalNodeIds.Length == 0
            ? []
            : await dbContext.ExternalGenealogyNodes
                .AsNoTracking()
                .Where(candidate => existingExternalNodeIds.Contains(candidate.Id))
                .ToArrayAsync(cancellationToken);
        var unchanged =
            bird.FatherBirdId == command.FatherBirdId &&
            bird.MotherBirdId == command.MotherBirdId &&
            string.Equals(bird.ExternalFatherName, normalizedExternalFatherName, StringComparison.Ordinal) &&
            bird.ExternalFatherSex == command.ExternalFatherSex &&
            string.Equals(bird.ExternalMotherName, normalizedExternalMotherName, StringComparison.Ordinal) &&
            bird.ExternalMotherSex == command.ExternalMotherSex &&
            HasExpectedNode(existingNodes, "father", command.FatherBirdId) &&
            HasExpectedNode(existingNodes, "mother", command.MotherBirdId) &&
            HasExpectedParentLink(
                existingExternalLinks,
                existingExternalNodes,
                ExternalGenealogyParentLink.FatherPosition,
                command.FatherBirdId,
                normalizedExternalFatherName,
                command.ExternalFatherSex) &&
            HasExpectedParentLink(
                existingExternalLinks,
                existingExternalNodes,
                ExternalGenealogyParentLink.MotherPosition,
                command.MotherBirdId,
                normalizedExternalMotherName,
                command.ExternalMotherSex);
        if (unchanged)
        {
            return UpdateBirdGenealogyResult.Updated(ToResult(bird, existingRoot, DateOnly.FromDateTime(DateTime.UtcNow)));
        }

        var now = DateTimeOffset.UtcNow;
        bird.UpdateParents(
            command.FatherBirdId,
            normalizedExternalFatherName,
            command.ExternalFatherSex,
            command.MotherBirdId,
            normalizedExternalMotherName,
            command.ExternalMotherSex,
            now);

        foreach (var selection in new[]
        {
            (Position: ExternalGenealogyParentLink.FatherPosition, ParentBirdId: command.FatherBirdId,
                ExternalName: normalizedExternalFatherName, ExternalSex: command.ExternalFatherSex),
            (Position: ExternalGenealogyParentLink.MotherPosition, ParentBirdId: command.MotherBirdId,
                ExternalName: normalizedExternalMotherName, ExternalSex: command.ExternalMotherSex)
        })
        {
            var existingLink = existingExternalLinks.SingleOrDefault(link => link.Position == selection.Position);
            var preserveLink = HasExpectedParentLink(
                existingExternalLinks,
                existingExternalNodes,
                selection.Position,
                selection.ParentBirdId,
                selection.ExternalName,
                selection.ExternalSex);
            if (!preserveLink && existingLink is not null)
            {
                await dbContext.ExternalGenealogyParentLinks
                    .Where(link => link.Id == existingLink.Id)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            if (selection.ParentBirdId is { } selectedParentId)
            {
                var existingNode = existingNodes.SingleOrDefault(node => node.Position == selection.Position);
                if (existingNode?.LinkedBirdId != selectedParentId)
                {
                    if (existingNode is not null)
                    {
                        await dbContext.GenealogyNodes
                            .Where(node => node.Id == existingNode.Id)
                            .ExecuteDeleteAsync(cancellationToken);
                    }

                    var parent = parents.Single(candidate => candidate.Id == selectedParentId);
                    dbContext.GenealogyNodes.Add(new GenealogyNode(
                        Guid.NewGuid(),
                        now,
                        breedingFarmId,
                        existingRoot.Id,
                        selection.Position,
                        parent.Id,
                        parent.Name,
                        parent.Sex,
                        parent.BirthDate,
                        parent.RingNumber,
                        parent.Status));
                }

                if (!preserveLink)
                {
                    var parent = parents.Single(candidate => candidate.Id == selectedParentId);
                    await BirdGenealogySnapshotMaterializer.AddLinkedParentAsync(
                        dbContext,
                        command.UserId,
                        breedingFarmId,
                        existingRoot.Id,
                        bird.Id,
                        null,
                        parent,
                        selection.Position,
                        now,
                        cancellationToken);
                }
            }
            else
            {
                var existingNode = existingNodes.SingleOrDefault(node => node.Position == selection.Position);
                if (existingNode is not null)
                {
                    await dbContext.GenealogyNodes
                        .Where(node => node.Id == existingNode.Id)
                        .ExecuteDeleteAsync(cancellationToken);
                }

                if (selection.ExternalName is not null && !preserveLink)
                {
                    AddExternalParentNode(
                        dbContext,
                        existingRoot,
                        breedingFarmId,
                        bird.Id,
                        selection.Position,
                        selection.ExternalName,
                        selection.ExternalSex,
                        now);
                }
            }
        }

        await ExternalGenealogyTreeCleanup.PruneUnreachableAsync(
            dbContext,
            existingRoot.Id,
            bird.Id,
            cancellationToken);

        return UpdateBirdGenealogyResult.Updated(ToResult(bird, existingRoot, DateOnly.FromDateTime(now.UtcDateTime)));
    }

    private static bool HasExpectedNode(
        IReadOnlyCollection<GenealogyNode> nodes,
        string position,
        Guid? linkedBirdId) =>
        linkedBirdId is null
            ? nodes.All(node => !string.Equals(node.Position, position, StringComparison.Ordinal))
            : nodes.Any(node =>
                string.Equals(node.Position, position, StringComparison.Ordinal) &&
                node.LinkedBirdId == linkedBirdId);

    private static bool HasExpectedParentLink(
        IReadOnlyCollection<ExternalGenealogyParentLink> links,
        IReadOnlyCollection<ExternalGenealogyNode> nodes,
        string position,
        Guid? linkedBirdId,
        string? name,
        BirdSex? sex)
    {
        var link = links.SingleOrDefault(candidate => candidate.Position == position);
        if (linkedBirdId is { } expectedBirdId)
        {
            return link?.ParentExternalNodeId is { } snapshotNodeId &&
                nodes.Any(node =>
                    node.Id == snapshotNodeId &&
                    node.IsBirdSnapshot &&
                    node.SnapshotSourceBirdId == expectedBirdId);
        }

        if (name is null)
        {
            return link is null;
        }

        return link?.ParentExternalNodeId is { } externalNodeId &&
            nodes.Any(node =>
                node.Id == externalNodeId &&
                !node.IsBirdSnapshot &&
                string.Equals(node.Name, name, StringComparison.Ordinal) &&
                node.Sex == sex);
    }

    private static void AddExternalParentNode(
        CriatorioVirtualDbContext dbContext,
        GenealogyNode rootNode,
        Guid breedingFarmId,
        Guid childBirdId,
        string position,
        string? name,
        BirdSex? sex,
        DateTimeOffset createdAtUtc)
    {
        if (name is null)
        {
            return;
        }

        var externalNode = new ExternalGenealogyNode(
            Guid.NewGuid(),
            createdAtUtc,
            breedingFarmId,
            rootNode.Id,
            name,
            sex!.Value);
        dbContext.ExternalGenealogyNodes.Add(externalNode);
        dbContext.ExternalGenealogyParentLinks.Add(new ExternalGenealogyParentLink(
            Guid.NewGuid(),
            createdAtUtc,
            breedingFarmId,
            rootNode.Id,
            childBirdId,
            null,
            position,
            null,
            externalNode.Id,
            null,
            null,
            null,
            null,
            null,
            null));
    }

    private static bool CreatesCycle(
        Guid birdId,
        Guid? parentId,
        IReadOnlyDictionary<Guid, BirdLinkProjection> parentLinks)
    {
        if (parentId is null)
        {
            return false;
        }

        var visited = new HashSet<Guid>();
        var pending = new Stack<Guid>();
        pending.Push(parentId.Value);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current == birdId)
            {
                return true;
            }

            if (!visited.Add(current) || !parentLinks.TryGetValue(current, out var links))
            {
                continue;
            }

            if (links.FatherBirdId is { } fatherBirdId)
            {
                pending.Push(fatherBirdId);
            }

            if (links.MotherBirdId is { } motherBirdId)
            {
                pending.Push(motherBirdId);
            }
        }

        return false;
    }

    private static BirdResult ToResult(Bird bird, GenealogyNode rootNode, DateOnly today) =>
        new(
            bird.Id,
            rootNode.Id,
            bird.BreedingFarmId,
            bird.Name,
            bird.SpeciesId,
            bird.Sex,
            bird.BirthDate,
            bird.DeathDate,
            bird.RingNumber,
            bird.FatherBirdId,
            bird.ExternalFatherName,
            bird.ExternalFatherSex,
            bird.MotherBirdId,
            bird.ExternalMotherName,
            bird.ExternalMotherSex,
            bird.Notes,
            bird.Status,
            bird.IdentificationPending,
            bird.CalculateAgeInYears(today),
            bird.CreatedAtUtc,
            bird.UpdatedAtUtc,
            bird.PrimaryPhotoId,
            bird.DefaultImageFileName);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record BirdLinkProjection(
        Guid BirdId,
        Guid? FatherBirdId,
        Guid? MotherBirdId);
}
