using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.Documents;

namespace CriatorioVirtual.Infrastructure.Documents;

public sealed class BirdDocumentGenerationSession(IPrivateObjectStorage storage)
    : ICommandFailureCompensator
{
    private bool compensated;

    public GenerateBirdDocumentStatus Status { get; private set; } = GenerateBirdDocumentStatus.InvalidData;

    public BirdDocumentPreparation? Preparation { get; private set; }

    public PrivateObjectDescriptor? StoredObject { get; private set; }

    public PrivateObjectDescriptor? StoredVisualIdentityObject { get; private set; }

    public void SetStatus(GenerateBirdDocumentStatus status)
    {
        Status = status;
        Preparation = null;
        StoredObject = null;
        StoredVisualIdentityObject = null;
    }

    public void SetPrepared(
        BirdDocumentPreparation preparation,
        PrivateObjectDescriptor storedObject,
        PrivateObjectDescriptor? storedVisualIdentityObject = null)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(storedObject);

        Preparation = preparation;
        StoredObject = storedObject;
        StoredVisualIdentityObject = storedVisualIdentityObject;
        Status = GenerateBirdDocumentStatus.Generated;
    }

    public async Task CompensateAsync(CancellationToken cancellationToken)
    {
        if (StoredObject is null || compensated)
        {
            return;
        }

        compensated = true;
        await TryDeleteAsync(StoredObject, cancellationToken);
        await TryDeleteAsync(StoredVisualIdentityObject, cancellationToken);
    }

    private async Task TryDeleteAsync(
        PrivateObjectDescriptor? storedObject,
        CancellationToken cancellationToken)
    {
        if (storedObject is null)
        {
            return;
        }

        try
        {
            await storage.DeleteAsync(
                storedObject.BreedingFarmId,
                storedObject.ObjectKey,
                cancellationToken);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed record BirdDocumentPreparation(
    Guid DocumentId,
    Guid BirdId,
    Guid BreedingFarmId,
    BirdDocumentType Type,
    BadgeModelId? ModelId,
    BadgePrintSize? PrintSize,
    IReadOnlyCollection<DocumentField> SelectedFields,
    string SelectedFieldsJson,
    string SnapshotJson,
    string FileName,
    string ContentType,
    long Length,
    int PageCount,
    double WidthMillimeters,
    double HeightMillimeters,
    DateTimeOffset GeneratedAtUtc,
    GenealogyCertificateModelId? CertificateModelId = null);
