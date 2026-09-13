using CriatorioVirtual.Domain.Primitives;

namespace CriatorioVirtual.Domain.Documents;

public sealed class BirdDocument : Entity
{
    private BirdDocument()
        : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
    {
        ObjectKey = null!;
        FileName = null!;
        ContentType = null!;
        SelectedFieldsJson = null!;
        SnapshotJson = null!;
    }

    private BirdDocument(
        Guid id,
        DateTimeOffset createdAtUtc,
        Guid birdId,
        Guid createdByBreedingFarmId,
        BirdDocumentType type,
        string objectKey,
        string fileName,
        string contentType,
        long length,
        DateTimeOffset generatedAtUtc,
        BadgeModelId? modelId,
        BadgePrintSize? printSize,
        string selectedFieldsJson,
        string snapshotJson)
        : base(id, createdAtUtc)
    {
        if (birdId == Guid.Empty)
        {
            throw new ArgumentException("The bird identifier cannot be empty.", nameof(birdId));
        }

        if (createdByBreedingFarmId == Guid.Empty)
        {
            throw new ArgumentException(
                "The document provenance breeding farm identifier cannot be empty.",
                nameof(createdByBreedingFarmId));
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), "The document type is invalid.");
        }

        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "A document must contain at least one byte.");
        }

        EnsureUtc(generatedAtUtc, nameof(generatedAtUtc));

        if (type == BirdDocumentType.Badge)
        {
            if (modelId is null || !Enum.IsDefined(modelId.Value))
            {
                throw new ArgumentException("A badge document requires a valid model.", nameof(modelId));
            }

            if (printSize is null || !Enum.IsDefined(printSize.Value))
            {
                throw new ArgumentException("A badge document requires a valid print size.", nameof(printSize));
            }
        }
        else if (modelId is not null || printSize is not null)
        {
            throw new ArgumentException("Only badge documents can define a model and print size.");
        }

        ObjectKey = RequireObjectKey(objectKey);
        FileName = RequireFileName(fileName);
        ContentType = RequireContentType(contentType);
        SelectedFieldsJson = RequireJson(selectedFieldsJson, nameof(selectedFieldsJson));
        SnapshotJson = RequireJson(snapshotJson, nameof(snapshotJson));
        BirdId = birdId;
        CreatedByBreedingFarmId = createdByBreedingFarmId;
        Type = type;
        Length = length;
        GeneratedAtUtc = generatedAtUtc;
        ModelId = modelId;
        PrintSize = printSize;
    }

    public static BirdDocument CreateBadge(
        Guid id,
        DateTimeOffset createdAtUtc,
        Guid birdId,
        Guid createdByBreedingFarmId,
        BadgeModelId modelId,
        BadgePrintSize printSize,
        string objectKey,
        string fileName,
        string contentType,
        long length,
        DateTimeOffset generatedAtUtc,
        string selectedFieldsJson,
        string snapshotJson) =>
        new(
            id,
            createdAtUtc,
            birdId,
            createdByBreedingFarmId,
            BirdDocumentType.Badge,
            objectKey,
            fileName,
            contentType,
            length,
            generatedAtUtc,
            modelId,
            printSize,
            selectedFieldsJson,
            snapshotJson);

    public static BirdDocument CreateInternalRecord(
        Guid id,
        DateTimeOffset createdAtUtc,
        Guid birdId,
        Guid createdByBreedingFarmId,
        string objectKey,
        string fileName,
        string contentType,
        long length,
        DateTimeOffset generatedAtUtc,
        string selectedFieldsJson,
        string snapshotJson) =>
        new(
            id,
            createdAtUtc,
            birdId,
            createdByBreedingFarmId,
            BirdDocumentType.InternalRecord,
            objectKey,
            fileName,
            contentType,
            length,
            generatedAtUtc,
            null,
            null,
            selectedFieldsJson,
            snapshotJson);

    public Guid BirdId { get; private set; }

    /// <summary>
    /// Identifies the farm that authorized and created the snapshot. It is provenance,
    /// not the authorization boundary for future reads after a bird transfer.
    /// </summary>
    public Guid CreatedByBreedingFarmId { get; private set; }

    public BirdDocumentType Type { get; private set; }

    public BadgeModelId? ModelId { get; private set; }

    public BadgePrintSize? PrintSize { get; private set; }

    public string ObjectKey { get; private set; }

    public string FileName { get; private set; }

    public string ContentType { get; private set; }

    public long Length { get; private set; }

    public DateTimeOffset GeneratedAtUtc { get; private set; }

    public string SelectedFieldsJson { get; private set; }

    public string SnapshotJson { get; private set; }

    private static string RequireObjectKey(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 500)
        {
            throw new ArgumentException("A document object key is required and cannot exceed 500 characters.", nameof(value));
        }

        if (normalized.Contains('\0') ||
            normalized.Contains('\\') ||
            normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Contains(':', StringComparison.Ordinal) ||
            normalized.Split('/', StringSplitOptions.None)
                .Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new ArgumentException("A document object key must be a safe relative path.", nameof(value));
        }

        return normalized;
    }

    private static string RequireFileName(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 255 ||
            normalized is "." or ".." || normalized.Contains('\0') ||
            normalized.Contains('/') || normalized.Contains('\\') ||
            normalized.Contains('\r') || normalized.Contains('\n'))
        {
            throw new ArgumentException("A document file name must not contain path traversal or control characters.", nameof(value));
        }

        return normalized;
    }

    private static string RequireContentType(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 100)
        {
            throw new ArgumentException("A document content type is required and cannot exceed 100 characters.", nameof(value));
        }

        if (normalized != "application/pdf")
        {
            throw new ArgumentException("A bird document must be stored as a PDF.", nameof(value));
        }

        return normalized;
    }

    private static string RequireJson(string value, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 1_000_000)
        {
            throw new ArgumentException("A document JSON snapshot is required and cannot exceed 1 MB.", parameterName);
        }

        return normalized;
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamps must be expressed in UTC.", parameterName);
        }
    }
}
