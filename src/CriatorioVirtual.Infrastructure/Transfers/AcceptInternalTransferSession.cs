using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;

namespace CriatorioVirtual.Infrastructure.Transfers;

public sealed class AcceptInternalTransferSession(IPrivateObjectStorage storage)
    : ICommandFailureCompensator
{
    private readonly List<(Guid SourceFarmId, Guid DestinationFarmId, string ObjectKey)> movedObjects = [];
    private bool committed;

    public void TrackMovedObject(Guid sourceFarmId, Guid destinationFarmId, string objectKey)
    {
        movedObjects.Add((sourceFarmId, destinationFarmId, objectKey));
    }

    public void MarkCommitted() => committed = true;

    public async Task CompensateAsync(CancellationToken cancellationToken)
    {
        if (committed || movedObjects.Count == 0)
        {
            return;
        }

        for (var i = movedObjects.Count - 1; i >= 0; i--)
        {
            var (sourceFarmId, destinationFarmId, objectKey) = movedObjects[i];
            try
            {
                await storage.MoveAsync(destinationFarmId, sourceFarmId, objectKey, cancellationToken);
            }
            catch
            {
                // Best effort compensation
            }
        }

        movedObjects.Clear();
    }
}
