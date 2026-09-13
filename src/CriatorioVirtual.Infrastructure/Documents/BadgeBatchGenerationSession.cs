using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.Documents;

namespace CriatorioVirtual.Infrastructure.Documents;

public sealed class BadgeBatchGenerationSession(IPrivateObjectStorage storage)
    : ICommandFailureCompensator
{
    private readonly List<PrivateObjectDescriptor> storedObjects = [];
    private bool compensated;

    public GenerateBadgeBatchStatus Status { get; private set; } = GenerateBadgeBatchStatus.InvalidData;

    public Guid BreedingFarmId { get; private set; }

    public IReadOnlyCollection<GenerateBadgeBatchItemResult> Items { get; private set; } = [];

    public IReadOnlyCollection<BadgeBatchDocumentPreparation> Documents { get; private set; } = [];

    public RenderedDocument? AggregatePdf { get; private set; }

    public void SetStatus(GenerateBadgeBatchStatus status)
    {
        Status = status;
        BreedingFarmId = Guid.Empty;
        Items = [];
        Documents = [];
        AggregatePdf = null;
    }

    public void SetItems(
        Guid breedingFarmId,
        IReadOnlyCollection<GenerateBadgeBatchItemResult> items,
        GenerateBadgeBatchStatus status)
    {
        BreedingFarmId = breedingFarmId;
        Items = items;
        Documents = [];
        AggregatePdf = null;
        Status = status;
    }

    public void AddStoredObject(PrivateObjectDescriptor storedObject)
    {
        ArgumentNullException.ThrowIfNull(storedObject);
        storedObjects.Add(storedObject);
    }

    public void SetPrepared(
        Guid breedingFarmId,
        IReadOnlyCollection<GenerateBadgeBatchItemResult> items,
        IReadOnlyCollection<BadgeBatchDocumentPreparation> documents,
        RenderedDocument aggregatePdf)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(aggregatePdf);

        BreedingFarmId = breedingFarmId;
        Items = items;
        Documents = documents;
        AggregatePdf = aggregatePdf;
        Status = GenerateBadgeBatchStatus.Generated;
    }

    public async Task CompensateAsync(CancellationToken cancellationToken)
    {
        if (compensated)
        {
            return;
        }

        compensated = true;
        foreach (var storedObject in storedObjects)
        {
            try
            {
                await storage.DeleteAsync(
                    storedObject.BreedingFarmId,
                    storedObject.ObjectKey,
                    cancellationToken);
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

public sealed record BadgeBatchDocumentPreparation(
    Guid DocumentId,
    Guid BirdId,
    Guid BreedingFarmId,
    BadgeModelId ModelId,
    BadgePrintSize PrintSize,
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
    PrivateObjectDescriptor StoredObject);
