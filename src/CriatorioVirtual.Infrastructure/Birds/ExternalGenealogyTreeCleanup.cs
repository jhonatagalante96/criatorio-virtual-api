using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

internal static class ExternalGenealogyTreeCleanup
{
    public static async Task PruneUnreachableAsync(
        CriatorioVirtualDbContext dbContext,
        Guid genealogyRootId,
        Guid rootBirdId,
        CancellationToken cancellationToken)
    {
        // The command executor owns the surrounding transaction; saving here lets the reachability
        // pass include both removed links and newly materialized nodes without exposing partial state.
        await dbContext.SaveChangesAsync(cancellationToken);

        var links = await dbContext.ExternalGenealogyParentLinks
            .AsNoTracking()
            .Where(link => link.GenealogyRootId == genealogyRootId)
            .Select(link => new LinkProjection(
                link.ChildBirdId,
                link.ChildExternalNodeId,
                link.ParentExternalNodeId))
            .ToArrayAsync(cancellationToken);
        var linksByChild = links
            .Where(link => link.ChildExternalNodeId is not null)
            .GroupBy(link => link.ChildExternalNodeId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var reachable = new HashSet<Guid>();
        var pending = new Stack<Guid>(links
            .Where(link => link.ChildBirdId == rootBirdId && link.ParentExternalNodeId is not null)
            .Select(link => link.ParentExternalNodeId!.Value));
        while (pending.TryPop(out var current))
        {
            if (!reachable.Add(current) || !linksByChild.TryGetValue(current, out var children))
            {
                continue;
            }

            foreach (var parentId in children
                         .Where(link => link.ParentExternalNodeId is not null)
                         .Select(link => link.ParentExternalNodeId!.Value))
            {
                pending.Push(parentId);
            }
        }

        var allNodeIds = await dbContext.ExternalGenealogyNodes
            .AsNoTracking()
            .Where(node => node.GenealogyRootId == genealogyRootId)
            .Select(node => node.Id)
            .ToArrayAsync(cancellationToken);
        var unreachable = allNodeIds.Where(nodeId => !reachable.Contains(nodeId)).ToArray();
        if (unreachable.Length == 0)
        {
            return;
        }

        await dbContext.ExternalGenealogyParentLinks
            .Where(link =>
                link.GenealogyRootId == genealogyRootId &&
                ((link.ChildExternalNodeId != null && unreachable.Contains(link.ChildExternalNodeId.Value)) ||
                 (link.ParentExternalNodeId != null && unreachable.Contains(link.ParentExternalNodeId.Value))))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.ExternalGenealogyNodes
            .Where(node => node.GenealogyRootId == genealogyRootId && unreachable.Contains(node.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private sealed record LinkProjection(
        Guid? ChildBirdId,
        Guid? ChildExternalNodeId,
        Guid? ParentExternalNodeId);
}
