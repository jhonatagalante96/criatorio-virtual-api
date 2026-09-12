using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;
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

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);
        if (user is null)
        {
            return GetBirdGenealogyResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return GetBirdGenealogyResult.BreedingFarmNotSelected();
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
            return GetBirdGenealogyResult.BreedingFarmNotFound();
        }

        // Load the selected tenant once. Traversal below is in-memory, so adding generations
        // changes the response size but not the number of database queries.
        var birds = await dbContext.Birds
            .AsNoTracking()
            .Where(candidate => candidate.BreedingFarmId == breedingFarmId)
            .Select(candidate => new BirdProjection(
                candidate.Id,
                candidate.Name,
                candidate.Sex,
                candidate.BirthDate,
                candidate.RingNumber,
                candidate.Status,
                candidate.FatherBirdId,
                candidate.ExternalFatherName,
                candidate.ExternalFatherSex,
                candidate.MotherBirdId,
                candidate.ExternalMotherName,
                candidate.ExternalMotherSex))
            .ToArrayAsync(cancellationToken);
        var birdById = birds.ToDictionary(bird => bird.BirdId);
        if (!birdById.TryGetValue(query.BirdId, out var rootBird))
        {
            return GetBirdGenealogyResult.BirdNotFound();
        }

        // A transferred bird owns the genealogy root in the destination farm, while
        // snapshot nodes keep the source farm provenance for inaccessible parents.
        // Resolve roots in the selected tenant first, then load only their complete
        // snapshot graphs so no unrelated tenant tree becomes visible.
        var genealogyRootIds = await dbContext.GenealogyNodes
            .AsNoTracking()
            .Where(node => node.BreedingFarmId == breedingFarmId && node.IsRoot)
            .Select(node => node.GenealogyRootId)
            .ToArrayAsync(cancellationToken);
        var genealogyNodes = genealogyRootIds.Length == 0
            ? []
            : await dbContext.GenealogyNodes
                .AsNoTracking()
                .Where(node => genealogyRootIds.Contains(node.GenealogyRootId))
                .Select(node => new GenealogyNodeProjection(
                    node.BirdId,
                    node.GenealogyRootId,
                    node.Position,
                    node.LinkedBirdId,
                    node.SnapshotName,
                    node.SnapshotSex,
                    node.SnapshotBirthDate,
                    node.SnapshotRingNumber,
                    node.SnapshotStatus,
                    node.IsRoot))
                .ToArrayAsync(cancellationToken);

        var rootIdByBirdId = genealogyNodes
            .Where(node => node.IsRoot)
            .ToDictionary(node => node.BirdId, node => node.GenealogyRootId);
        var snapshotByRootAndPosition = genealogyNodes
            .Where(node => !node.IsRoot)
            .ToDictionary(
                node => new SnapshotKey(node.GenealogyRootId, node.Position),
                node => node);

        var nodes = new Dictionary<string, BirdGenealogyNodeResult>(StringComparer.Ordinal);
        var edges = new List<BirdGenealogyEdgeResult>();
        var edgeKeys = new HashSet<(string ChildNodeKey, string ParentNodeKey, string Position)>();
        var expandedBirdIds = new HashSet<Guid> { rootBird.BirdId };
        var pending = new Queue<Expansion>();
        var rootNodeKey = BirdNodeKey(rootBird.BirdId);

        nodes.Add(
            rootNodeKey,
            CreatePrivateNode(rootBird, rootNodeKey, GenealogyNode.RootPosition, 0));
        pending.Enqueue(new Expansion(rootBird, rootNodeKey, 0));

        var isTruncated = false;
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            var parentReferences = GetParentReferences(
                current.Bird,
                rootIdByBirdId,
                snapshotByRootAndPosition);
            if (current.Generation >= query.MaxGenerations)
            {
                isTruncated |= parentReferences.Count > 0;
                continue;
            }

            foreach (var parent in parentReferences)
            {
                var resolved = ResolveParent(
                    current.Bird,
                    current.Generation + 1,
                    parent,
                    birdById,
                    rootIdByBirdId,
                    snapshotByRootAndPosition);
                if (resolved is null)
                {
                    continue;
                }

                nodes.TryAdd(resolved.Node.NodeKey, resolved.Node);
                if (edgeKeys.Add((current.NodeKey, resolved.Node.NodeKey, parent.Position)))
                {
                    edges.Add(new BirdGenealogyEdgeResult(current.NodeKey, resolved.Node.NodeKey, parent.Position));
                }

                if (resolved.AccessibleBird is not null && expandedBirdIds.Add(resolved.AccessibleBird.BirdId))
                {
                    pending.Enqueue(new Expansion(
                        resolved.AccessibleBird,
                        resolved.Node.NodeKey,
                        current.Generation + 1));
                }
            }
        }

        return GetBirdGenealogyResult.Succeeded(
            new BirdGenealogyResult(
                breedingFarmId,
                rootBird.BirdId,
                query.MaxGenerations,
                isTruncated,
                nodes.Values.ToArray(),
                edges));
    }

    private static ResolvedParent? ResolveParent(
        BirdProjection sourceBird,
        int generation,
        ParentReference parent,
        IReadOnlyDictionary<Guid, BirdProjection> birdById,
        IReadOnlyDictionary<Guid, Guid> rootIdByBirdId,
        IReadOnlyDictionary<SnapshotKey, GenealogyNodeProjection> snapshotByRootAndPosition)
    {
        if (parent.LinkedBirdId is { } linkedBirdId)
        {
            if (birdById.TryGetValue(linkedBirdId, out var accessibleBird))
            {
                var snapshot = rootIdByBirdId.TryGetValue(sourceBird.BirdId, out var rootId) &&
                    snapshotByRootAndPosition.TryGetValue(new SnapshotKey(rootId, parent.Position), out var candidate)
                    ? candidate
                    : null;
                var node = snapshot is not null &&
                    snapshot.LinkedBirdId == linkedBirdId &&
                    IsUsableSnapshot(snapshot)
                        ? CreateSnapshotNode(
                            BirdNodeKey(linkedBirdId),
                            parent.Position,
                            generation,
                            linkedBirdId,
                            snapshot,
                            isAccessible: true)
                        : CreatePrivateNode(
                            accessibleBird,
                            BirdNodeKey(linkedBirdId),
                            parent.Position,
                            generation);
                return new ResolvedParent(node, accessibleBird);
            }

            // The linked identifier is intentionally omitted when the bird is no longer
            // accessible in this tenant. The stored snapshot remains readable instead.
            if (rootIdByBirdId.TryGetValue(sourceBird.BirdId, out var sourceRootId) &&
                snapshotByRootAndPosition.TryGetValue(
                    new SnapshotKey(sourceRootId, parent.Position),
                    out var inaccessibleSnapshot) &&
                IsUsableSnapshot(inaccessibleSnapshot))
            {
                return new ResolvedParent(
                    CreateSnapshotNode(
                        SnapshotNodeKey(sourceBird.BirdId, parent.Position),
                        parent.Position,
                        generation,
                        birdId: null,
                        inaccessibleSnapshot,
                        isAccessible: false),
                    AccessibleBird: null);
            }

            return null;
        }

        if (string.IsNullOrWhiteSpace(parent.ExternalName))
        {
            return null;
        }

        return new ResolvedParent(
            new BirdGenealogyNodeResult(
                ExternalNodeKey(sourceBird.BirdId, parent.Position),
                BirdId: null,
                parent.Position,
                generation,
                parent.ExternalName,
                parent.ExternalSex,
                BirthDate: null,
                RingNumber: null,
                Status: null,
                BirdGenealogyNodeSource.External,
                IsSnapshot: true,
                IsAccessible: false,
                CanNavigate: false),
            AccessibleBird: null);
    }

    private static bool IsUsableSnapshot(GenealogyNodeProjection snapshot) =>
        !string.IsNullOrWhiteSpace(snapshot.SnapshotName) && snapshot.SnapshotSex is not null;

    private static BirdGenealogyNodeResult CreatePrivateNode(
        BirdProjection bird,
        string nodeKey,
        string position,
        int generation) =>
        new(
            nodeKey,
            bird.BirdId,
            position,
            generation,
            bird.Name,
            bird.Sex,
            bird.BirthDate,
            bird.RingNumber,
            bird.Status,
            BirdGenealogyNodeSource.Private,
            IsSnapshot: false,
            IsAccessible: true,
            CanNavigate: true);

    private static BirdGenealogyNodeResult CreateSnapshotNode(
        string nodeKey,
        string position,
        int generation,
        Guid? birdId,
        GenealogyNodeProjection snapshot,
        bool isAccessible) =>
        new(
            nodeKey,
            birdId,
            position,
            generation,
            snapshot.SnapshotName!,
            snapshot.SnapshotSex,
            snapshot.SnapshotBirthDate,
            snapshot.SnapshotRingNumber,
            snapshot.SnapshotStatus,
            BirdGenealogyNodeSource.Snapshot,
            IsSnapshot: true,
            isAccessible,
            CanNavigate: isAccessible);

    private static IReadOnlyCollection<ParentReference> GetParentReferences(
        BirdProjection bird,
        IReadOnlyDictionary<Guid, Guid> rootIdByBirdId,
        IReadOnlyDictionary<SnapshotKey, GenealogyNodeProjection> snapshotByRootAndPosition) =>
    new ParentReference?[]
    {
        GetParentReference(
            bird.BirdId,
            "father",
            bird.FatherBirdId,
            bird.ExternalFatherName,
            bird.ExternalFatherSex,
            rootIdByBirdId,
            snapshotByRootAndPosition),
        GetParentReference(
            bird.BirdId,
            "mother",
            bird.MotherBirdId,
            bird.ExternalMotherName,
            bird.ExternalMotherSex,
            rootIdByBirdId,
            snapshotByRootAndPosition)
    }
    .Where(reference => reference is not null)
    .Select(reference => reference!)
    .ToArray();

    private static ParentReference? GetParentReference(
        Guid birdId,
        string position,
        Guid? linkedBirdId,
        string? externalName,
        BirdSex? externalSex,
        IReadOnlyDictionary<Guid, Guid> rootIdByBirdId,
        IReadOnlyDictionary<SnapshotKey, GenealogyNodeProjection> snapshotByRootAndPosition)
    {
        if (linkedBirdId is not null || !string.IsNullOrWhiteSpace(externalName))
        {
            return new ParentReference(position, linkedBirdId, externalName, externalSex);
        }

        if (rootIdByBirdId.TryGetValue(birdId, out var rootId) &&
            snapshotByRootAndPosition.TryGetValue(new SnapshotKey(rootId, position), out var snapshot) &&
            IsUsableSnapshot(snapshot) &&
            snapshot.LinkedBirdId is { } snapshotLinkedBirdId)
        {
            return new ParentReference(position, snapshotLinkedBirdId, null, null);
        }

        return null;
    }

    private static string BirdNodeKey(Guid birdId) => $"bird:{birdId:D}";

    private static string SnapshotNodeKey(Guid sourceBirdId, string position) =>
        $"snapshot:{sourceBirdId:D}:{position}";

    private static string ExternalNodeKey(Guid sourceBirdId, string position) =>
        $"external:{sourceBirdId:D}:{position}";

    private sealed record BirdProjection(
        Guid BirdId,
        string Name,
        BirdSex Sex,
        DateOnly? BirthDate,
        string? RingNumber,
        BirdStatus Status,
        Guid? FatherBirdId,
        string? ExternalFatherName,
        BirdSex? ExternalFatherSex,
        Guid? MotherBirdId,
        string? ExternalMotherName,
        BirdSex? ExternalMotherSex);

    private sealed record GenealogyNodeProjection(
        Guid BirdId,
        Guid GenealogyRootId,
        string Position,
        Guid? LinkedBirdId,
        string? SnapshotName,
        BirdSex? SnapshotSex,
        DateOnly? SnapshotBirthDate,
        string? SnapshotRingNumber,
        BirdStatus? SnapshotStatus,
        bool IsRoot);

    private sealed record ParentReference(
        string Position,
        Guid? LinkedBirdId,
        string? ExternalName,
        BirdSex? ExternalSex);

    private sealed record Expansion(
        BirdProjection Bird,
        string NodeKey,
        int Generation);

    private sealed record ResolvedParent(
        BirdGenealogyNodeResult Node,
        BirdProjection? AccessibleBird);

    private readonly record struct SnapshotKey(Guid GenealogyRootId, string Position);
}
