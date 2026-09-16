using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

/// <summary>Reads a bounded genealogy graph with a constant number of database round trips.</summary>
public sealed class GetBirdGenealogyQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<GetBirdGenealogyQuery, GetBirdGenealogyResult>
{
    public async Task<GetBirdGenealogyResult> Handle(
        GetBirdGenealogyQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(query.MaxGenerations);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.MaxGenerations, BirdGenealogyLimits.MaxGenerations);

        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);
        if (user is null) return GetBirdGenealogyResult.UserNotFound();
        if (user.SelectedBreedingFarmId is null) return GetBirdGenealogyResult.BreedingFarmNotSelected();

        var breedingFarmId = user.SelectedBreedingFarmId.Value;
        var membership = await dbContext.BreedingFarmUsers.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.BreedingFarmId == breedingFarmId && candidate.UserId == query.UserId && candidate.IsActive,
            cancellationToken);
        if (membership is null) return GetBirdGenealogyResult.BreedingFarmNotFound();

        var birds = await dbContext.Birds.AsNoTracking()
            .Where(candidate => candidate.BreedingFarmId == breedingFarmId)
            .Select(candidate => new BirdProjection(
                candidate.Id, candidate.Name, candidate.Sex, candidate.BirthDate, candidate.RingNumber, candidate.Status,
                candidate.FatherBirdId, candidate.ExternalFatherName, candidate.ExternalFatherSex,
                candidate.MotherBirdId, candidate.ExternalMotherName, candidate.ExternalMotherSex))
            .ToArrayAsync(cancellationToken);
        var birdById = birds.ToDictionary(bird => bird.BirdId);
        if (!birdById.TryGetValue(query.BirdId, out var rootBird)) return GetBirdGenealogyResult.BirdNotFound();

        var genealogyRootIds = await dbContext.GenealogyNodes.AsNoTracking()
            .Where(node => node.BreedingFarmId == breedingFarmId && node.IsRoot)
            .Select(node => node.GenealogyRootId)
            .ToArrayAsync(cancellationToken);
        var genealogyNodes = genealogyRootIds.Length == 0
            ? []
            : await dbContext.GenealogyNodes.AsNoTracking()
                .Where(node => genealogyRootIds.Contains(node.GenealogyRootId))
                .Select(node => new GenealogyNodeProjection(
                    node.BirdId, node.GenealogyRootId, node.Position, node.LinkedBirdId, node.SnapshotName,
                    node.SnapshotSex, node.SnapshotBirthDate, node.SnapshotRingNumber, node.SnapshotStatus, node.IsRoot))
                .ToArrayAsync(cancellationToken);
        var externalNodes = genealogyRootIds.Length == 0
            ? []
            : await dbContext.ExternalGenealogyNodes.AsNoTracking()
                .Where(node => genealogyRootIds.Contains(node.GenealogyRootId))
                .Select(node => new ExternalNodeProjection(
                    node.Id,
                    node.GenealogyRootId,
                    node.Name,
                    node.Sex,
                    node.IsBirdSnapshot,
                    node.SnapshotSourceBirdId,
                    node.SnapshotBirthDate,
                    node.SnapshotRingNumber,
                    node.SnapshotStatus,
                    node.CanNavigateToSourceBird))
                .ToArrayAsync(cancellationToken);
        var externalLinks = genealogyRootIds.Length == 0
            ? []
            : await dbContext.ExternalGenealogyParentLinks.AsNoTracking()
                .Where(link => genealogyRootIds.Contains(link.GenealogyRootId))
                .Select(link => new ExternalLinkProjection(
                    link.GenealogyRootId, link.ChildBirdId, link.ChildExternalNodeId, link.Position,
                    link.ParentBirdId, link.ParentExternalNodeId, link.ParentSnapshotName, link.ParentSnapshotSex,
                    link.ParentSnapshotBirthDate, link.ParentSnapshotRingNumber, link.ParentSnapshotStatus))
                .ToArrayAsync(cancellationToken);

        var rootIdByBirdId = genealogyNodes.Where(node => node.IsRoot).ToDictionary(node => node.BirdId, node => node.GenealogyRootId);
        var snapshotByRootAndPosition = genealogyNodes.Where(node => !node.IsRoot).ToDictionary(
            node => new SnapshotKey(node.GenealogyRootId, node.Position), node => node);
        var externalById = externalNodes.ToDictionary(node => node.Id);
        var externalLinksByChild = externalLinks
            .GroupBy(link => new ChildKey(link.GenealogyRootId, link.ChildBirdId, link.ChildExternalNodeId))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<string, ExternalLinkProjection>)group.ToDictionary(link => link.Position));

        var nodes = new Dictionary<string, BirdGenealogyNodeResult>(StringComparer.Ordinal);
        var edges = new List<BirdGenealogyEdgeResult>();
        var edgeKeys = new HashSet<(string ChildNodeKey, string ParentNodeKey, string Position)>();
        var expandedBirdIds = new HashSet<Guid> { rootBird.BirdId };
        var expandedExternalIds = new HashSet<Guid>();
        var pending = new Queue<Expansion>();
        var rootNodeKey = BirdNodeKey(rootBird.BirdId);
        nodes.Add(rootNodeKey, CreatePrivateNode(rootBird, rootNodeKey, GenealogyNode.RootPosition, 0));
        pending.Enqueue(Expansion.ForBird(rootBird, rootNodeKey, 0));

        var isTruncated = false;
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            var parentReferences = current.Bird is not null
                ? GetBirdParentReferences(current.Bird, rootIdByBirdId, snapshotByRootAndPosition, externalLinksByChild)
                : GetExternalParentReferences(current.External!, externalLinksByChild);
            if (current.Generation >= query.MaxGenerations)
            {
                isTruncated |= parentReferences.Count > 0;
                continue;
            }

            foreach (var parent in parentReferences)
            {
                var resolved = ResolveParent(
                    current, parent, birdById, rootIdByBirdId, snapshotByRootAndPosition, externalById,
                    canEdit: membership.Role == BreedingFarmRole.Owner);
                if (resolved is null) continue;

                nodes.TryAdd(resolved.Node.NodeKey, resolved.Node);
                if (edgeKeys.Add((current.NodeKey, resolved.Node.NodeKey, parent.Position)))
                    edges.Add(new BirdGenealogyEdgeResult(current.NodeKey, resolved.Node.NodeKey, parent.Position));

                if (resolved.Bird is not null && expandedBirdIds.Add(resolved.Bird.BirdId))
                    pending.Enqueue(Expansion.ForBird(resolved.Bird, resolved.Node.NodeKey, current.Generation + 1));
                else if (resolved.External is not null && expandedExternalIds.Add(resolved.External.Id))
                    pending.Enqueue(Expansion.ForExternal(resolved.External, resolved.Node.NodeKey, current.Generation + 1));
            }
        }

        return GetBirdGenealogyResult.Succeeded(new BirdGenealogyResult(
            breedingFarmId, rootBird.BirdId, query.MaxGenerations, isTruncated, nodes.Values.ToArray(), edges));
    }

    private static ResolvedParent? ResolveParent(
        Expansion current,
        ParentReference parent,
        IReadOnlyDictionary<Guid, BirdProjection> birdById,
        IReadOnlyDictionary<Guid, Guid> rootIdByBirdId,
        IReadOnlyDictionary<SnapshotKey, GenealogyNodeProjection> snapshotByRootAndPosition,
        IReadOnlyDictionary<Guid, ExternalNodeProjection> externalById,
        bool canEdit)
    {
        var generation = current.Generation + 1;
        if (parent.ExternalNodeId is { } externalNodeId)
        {
            if (!externalById.TryGetValue(externalNodeId, out var external))
            {
                return null;
            }

            if (external.IsBirdSnapshot)
            {
                var isAccessible = external.CanNavigateToSourceBird &&
                    external.SnapshotSourceBirdId is { } sourceBirdId &&
                    birdById.ContainsKey(sourceBirdId);
                var snapshotBirdId = isAccessible ? external.SnapshotSourceBirdId : null;
                var snapshot = CreateSnapshotNode(
                    SnapshotExternalNodeKey(external.Id),
                    parent.Position,
                    generation,
                    snapshotBirdId,
                    external.Name,
                    external.Sex,
                    external.SnapshotBirthDate,
                    external.SnapshotRingNumber,
                    external.SnapshotStatus,
                    isAccessible);
                return new ResolvedParent(snapshot, null, external);
            }

            return new ResolvedParent(CreateExternalNode(external, parent.Position, generation, canEdit), null, external);
        }

        if (parent.SnapshotName is not null)
        {
            return new ResolvedParent(
                CreateSnapshotNode(
                    SnapshotNodeKey(current.NodeKey, parent.Position), parent.Position, generation, null,
                    parent.SnapshotName, parent.SnapshotSex, parent.SnapshotBirthDate,
                    parent.SnapshotRingNumber, parent.SnapshotStatus), null, null);
        }

        if (parent.LinkedBirdId is { } linkedBirdId)
        {
            if (birdById.TryGetValue(linkedBirdId, out var accessibleBird))
            {
                var snapshot = current.Bird is not null &&
                    rootIdByBirdId.TryGetValue(current.Bird.BirdId, out var rootId) &&
                    snapshotByRootAndPosition.TryGetValue(new SnapshotKey(rootId, parent.Position), out var candidate) &&
                    candidate.LinkedBirdId == linkedBirdId && IsUsableSnapshot(candidate) ? candidate : null;
                var node = snapshot is not null
                    ? CreateSnapshotNode(
                        BirdNodeKey(linkedBirdId), parent.Position, generation, linkedBirdId,
                        snapshot.SnapshotName!, snapshot.SnapshotSex, snapshot.SnapshotBirthDate,
                        snapshot.SnapshotRingNumber, snapshot.SnapshotStatus, isAccessible: true)
                    : CreatePrivateNode(accessibleBird, BirdNodeKey(linkedBirdId), parent.Position, generation);
                return new ResolvedParent(node, accessibleBird, null);
            }

            if (current.Bird is not null &&
                rootIdByBirdId.TryGetValue(current.Bird.BirdId, out var sourceRootId) &&
                snapshotByRootAndPosition.TryGetValue(new SnapshotKey(sourceRootId, parent.Position), out var inaccessibleSnapshot) &&
                IsUsableSnapshot(inaccessibleSnapshot))
            {
                return new ResolvedParent(
                    CreateSnapshotNode(
                        SnapshotNodeKey(current.NodeKey, parent.Position), parent.Position, generation, null,
                        inaccessibleSnapshot.SnapshotName!, inaccessibleSnapshot.SnapshotSex,
                        inaccessibleSnapshot.SnapshotBirthDate, inaccessibleSnapshot.SnapshotRingNumber,
                        inaccessibleSnapshot.SnapshotStatus), null, null);
            }

            return null;
        }

        return string.IsNullOrWhiteSpace(parent.ExternalName)
            ? null
            : new ResolvedParent(
                new BirdGenealogyNodeResult(
                    ExternalNodeKey(current.NodeKey, parent.Position), null, parent.Position, generation,
                    parent.ExternalName, parent.ExternalSex, null, null, null,
                    BirdGenealogyNodeSource.External, true, false, false, canEdit), null, null);
    }

    private static IReadOnlyCollection<ParentReference> GetBirdParentReferences(
        BirdProjection bird,
        IReadOnlyDictionary<Guid, Guid> rootIdByBirdId,
        IReadOnlyDictionary<SnapshotKey, GenealogyNodeProjection> snapshotByRootAndPosition,
        IReadOnlyDictionary<ChildKey, IReadOnlyDictionary<string, ExternalLinkProjection>> linksByChild)
    {
        var references = new List<ParentReference>(2);
        AddBirdParentReference(references, bird, ExternalGenealogyParentLink.FatherPosition, bird.FatherBirdId,
            bird.ExternalFatherName, bird.ExternalFatherSex, rootIdByBirdId, snapshotByRootAndPosition, linksByChild);
        AddBirdParentReference(references, bird, ExternalGenealogyParentLink.MotherPosition, bird.MotherBirdId,
            bird.ExternalMotherName, bird.ExternalMotherSex, rootIdByBirdId, snapshotByRootAndPosition, linksByChild);
        return references;
    }

    private static void AddBirdParentReference(
        ICollection<ParentReference> references,
        BirdProjection bird,
        string position,
        Guid? linkedBirdId,
        string? externalName,
        BirdSex? externalSex,
        IReadOnlyDictionary<Guid, Guid> rootIdByBirdId,
        IReadOnlyDictionary<SnapshotKey, GenealogyNodeProjection> snapshotByRootAndPosition,
        IReadOnlyDictionary<ChildKey, IReadOnlyDictionary<string, ExternalLinkProjection>> linksByChild)
    {
        if (rootIdByBirdId.TryGetValue(bird.BirdId, out var rootId) &&
            linksByChild.TryGetValue(new ChildKey(rootId, bird.BirdId, null), out var birdLinks) &&
            birdLinks.TryGetValue(position, out var link))
        {
            references.Add(ToParentReference(link));
            return;
        }

        if (linkedBirdId is not null || !string.IsNullOrWhiteSpace(externalName))
        {
            references.Add(new ParentReference(position, linkedBirdId, externalName, externalSex));
            return;
        }

        if (rootIdByBirdId.TryGetValue(bird.BirdId, out rootId) &&
            snapshotByRootAndPosition.TryGetValue(new SnapshotKey(rootId, position), out var snapshot) &&
            IsUsableSnapshot(snapshot) && snapshot.LinkedBirdId is { } snapshotLinkedBirdId)
        {
            references.Add(new ParentReference(position, snapshotLinkedBirdId, null, null));
        }
    }

    private static IReadOnlyCollection<ParentReference> GetExternalParentReferences(
        ExternalNodeProjection external,
        IReadOnlyDictionary<ChildKey, IReadOnlyDictionary<string, ExternalLinkProjection>> linksByChild) =>
        linksByChild.TryGetValue(new ChildKey(external.GenealogyRootId, null, external.Id), out var links)
            ? links.Values.Select(ToParentReference).ToArray()
            : [];

    private static ParentReference ToParentReference(ExternalLinkProjection link) =>
        link.ParentExternalNodeId is { } externalNodeId
            ? new ParentReference(link.Position, null, null, null, externalNodeId)
            : link.ParentSnapshotName is not null
                ? new ParentReference(link.Position, link.ParentBirdId, null, null, null,
                    link.ParentSnapshotName, link.ParentSnapshotSex, link.ParentSnapshotBirthDate,
                    link.ParentSnapshotRingNumber, link.ParentSnapshotStatus)
                : new ParentReference(link.Position, link.ParentBirdId, null, null);

    private static bool IsUsableSnapshot(GenealogyNodeProjection snapshot) =>
        !string.IsNullOrWhiteSpace(snapshot.SnapshotName) && snapshot.SnapshotSex is not null;

    private static BirdGenealogyNodeResult CreatePrivateNode(BirdProjection bird, string nodeKey, string position, int generation) =>
        new(nodeKey, bird.BirdId, position, generation, bird.Name, bird.Sex, bird.BirthDate, bird.RingNumber, bird.Status,
            BirdGenealogyNodeSource.Private, false, true, true);

    private static BirdGenealogyNodeResult CreateSnapshotNode(
        string nodeKey,
        string position,
        int generation,
        Guid? birdId,
        string name,
        BirdSex? sex,
        DateOnly? birthDate,
        string? ringNumber,
        BirdStatus? status,
        bool isAccessible = false) =>
        new(nodeKey, birdId, position, generation, name, sex, birthDate, ringNumber, status,
            BirdGenealogyNodeSource.Snapshot, true, isAccessible, isAccessible);

    private static BirdGenealogyNodeResult CreateExternalNode(
        ExternalNodeProjection external,
        string position,
        int generation,
        bool canEdit) =>
        new(ExternalNodeKey(external.Id), null, position, generation, external.Name, external.Sex, null, null, null,
            BirdGenealogyNodeSource.External, true, false, false, canEdit);

    private static string BirdNodeKey(Guid birdId) => $"bird:{birdId:D}";
    private static string SnapshotNodeKey(string sourceNodeKey, string position) => $"snapshot:{sourceNodeKey}:{position}";
    private static string SnapshotExternalNodeKey(Guid externalNodeId) => $"snapshot:external:{externalNodeId:D}";
    private static string ExternalNodeKey(Guid externalNodeId) => $"external:{externalNodeId:D}";
    private static string ExternalNodeKey(string sourceNodeKey, string position) => $"external:{sourceNodeKey}:{position}";

    private sealed record BirdProjection(
        Guid BirdId, string Name, BirdSex Sex, DateOnly? BirthDate, string? RingNumber, BirdStatus Status,
        Guid? FatherBirdId, string? ExternalFatherName, BirdSex? ExternalFatherSex,
        Guid? MotherBirdId, string? ExternalMotherName, BirdSex? ExternalMotherSex);

    private sealed record GenealogyNodeProjection(
        Guid BirdId, Guid GenealogyRootId, string Position, Guid? LinkedBirdId, string? SnapshotName,
        BirdSex? SnapshotSex, DateOnly? SnapshotBirthDate, string? SnapshotRingNumber, BirdStatus? SnapshotStatus, bool IsRoot);

    private sealed record ExternalNodeProjection(
        Guid Id,
        Guid GenealogyRootId,
        string Name,
        BirdSex Sex,
        bool IsBirdSnapshot,
        Guid? SnapshotSourceBirdId,
        DateOnly? SnapshotBirthDate,
        string? SnapshotRingNumber,
        BirdStatus? SnapshotStatus,
        bool CanNavigateToSourceBird);

    private sealed record ExternalLinkProjection(
        Guid GenealogyRootId, Guid? ChildBirdId, Guid? ChildExternalNodeId, string Position,
        Guid? ParentBirdId, Guid? ParentExternalNodeId, string? ParentSnapshotName, BirdSex? ParentSnapshotSex,
        DateOnly? ParentSnapshotBirthDate, string? ParentSnapshotRingNumber, BirdStatus? ParentSnapshotStatus);

    private sealed record ParentReference(
        string Position, Guid? LinkedBirdId, string? ExternalName, BirdSex? ExternalSex,
        Guid? ExternalNodeId = null, string? SnapshotName = null, BirdSex? SnapshotSex = null,
        DateOnly? SnapshotBirthDate = null, string? SnapshotRingNumber = null, BirdStatus? SnapshotStatus = null);

    private sealed record Expansion(BirdProjection? Bird, ExternalNodeProjection? External, string NodeKey, int Generation)
    {
        public static Expansion ForBird(BirdProjection bird, string nodeKey, int generation) => new(bird, null, nodeKey, generation);
        public static Expansion ForExternal(ExternalNodeProjection external, string nodeKey, int generation) => new(null, external, nodeKey, generation);
    }

    private sealed record ResolvedParent(BirdGenealogyNodeResult Node, BirdProjection? Bird, ExternalNodeProjection? External);
    private readonly record struct SnapshotKey(Guid GenealogyRootId, string Position);
    private readonly record struct ChildKey(Guid GenealogyRootId, Guid? BirdId, Guid? ExternalNodeId);
}
