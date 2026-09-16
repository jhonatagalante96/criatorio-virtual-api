using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Infrastructure.Persistence;

namespace CriatorioVirtual.Infrastructure.Birds;

/// <summary>Copies a linked bird's bounded ancestry into the destination bird's genealogy tree.</summary>
internal static class BirdGenealogySnapshotMaterializer
{
    public static async Task AddLinkedParentAsync(
        CriatorioVirtualDbContext dbContext,
        Guid userId,
        Guid breedingFarmId,
        Guid genealogyRootId,
        Guid childBirdId,
        Guid? childExternalNodeId,
        Bird parent,
        string position,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        var queryResult = await new GetBirdGenealogyQueryHandler(dbContext).Handle(
            new GetBirdGenealogyQuery(userId, parent.Id, BirdGenealogyLimits.MaxGenerations - 1),
            cancellationToken);
        if (queryResult.Status != GetBirdGenealogyStatus.Success || queryResult.Genealogy is null)
        {
            throw new InvalidOperationException("The linked bird's genealogy could not be read for snapshotting.");
        }

        var sourceGraph = queryResult.Genealogy;
        var root = sourceGraph.Nodes.SingleOrDefault(node => node.BirdId == parent.Id && node.Generation == 0)
            ?? throw new InvalidOperationException("The linked bird is missing from its genealogy result.");
        var allowNavigation = childExternalNodeId is null;
        var localNodeBySourceKey = new Dictionary<string, ExternalGenealogyNode>(StringComparer.Ordinal)
        {
            [root.NodeKey] = ExternalGenealogyNode.CreateBirdSnapshot(
                Guid.NewGuid(),
                createdAtUtc,
                breedingFarmId,
                genealogyRootId,
                parent.Name,
                parent.Sex,
                parent.Id,
                parent.BirthDate,
                parent.RingNumber,
                parent.Status,
                allowNavigation)
        };

        foreach (var sourceNode in sourceGraph.Nodes.Where(node => node.NodeKey != root.NodeKey))
        {
            var copiedNode = sourceNode.Source switch
            {
                BirdGenealogyNodeSource.External => new ExternalGenealogyNode(
                    Guid.NewGuid(), createdAtUtc, breedingFarmId, genealogyRootId,
                    sourceNode.Name, sourceNode.Sex ?? throw MissingSnapshotData()),
                BirdGenealogyNodeSource.Private or BirdGenealogyNodeSource.Snapshot =>
                    ExternalGenealogyNode.CreateBirdSnapshot(
                        Guid.NewGuid(),
                        createdAtUtc,
                        breedingFarmId,
                        genealogyRootId,
                        sourceNode.Name,
                        sourceNode.Sex ?? throw MissingSnapshotData(),
                        sourceNode.BirdId,
                        sourceNode.BirthDate,
                        sourceNode.RingNumber,
                        sourceNode.Status ?? throw MissingSnapshotData(),
                        allowNavigation && sourceNode.CanNavigate),
                _ => throw new InvalidOperationException("The linked bird's genealogy contains an unsupported node source.")
            };

            localNodeBySourceKey.Add(sourceNode.NodeKey, copiedNode);
            dbContext.ExternalGenealogyNodes.Add(copiedNode);
        }

        var parentSnapshot = localNodeBySourceKey[root.NodeKey];
        dbContext.ExternalGenealogyNodes.Add(parentSnapshot);
        dbContext.ExternalGenealogyParentLinks.Add(new ExternalGenealogyParentLink(
            Guid.NewGuid(),
            createdAtUtc,
            breedingFarmId,
            genealogyRootId,
            childExternalNodeId is null ? childBirdId : null,
            childExternalNodeId,
            position,
            null,
            parentSnapshot.Id,
            null,
            null,
            null,
            null,
            null,
            null));

        foreach (var edge in sourceGraph.Edges)
        {
            if (!localNodeBySourceKey.TryGetValue(edge.ChildNodeKey, out var child) ||
                !localNodeBySourceKey.TryGetValue(edge.ParentNodeKey, out var ancestor))
            {
                continue;
            }

            if (child.Id == ancestor.Id)
            {
                continue;
            }

            dbContext.ExternalGenealogyParentLinks.Add(new ExternalGenealogyParentLink(
                Guid.NewGuid(),
                createdAtUtc,
                breedingFarmId,
                genealogyRootId,
                null,
                child.Id,
                edge.Position,
                null,
                ancestor.Id,
                null,
                null,
                null,
                null,
                null,
                null));
        }
    }

    private static InvalidOperationException MissingSnapshotData() =>
        new("The linked bird's genealogy contains incomplete parent snapshot data.");
}
