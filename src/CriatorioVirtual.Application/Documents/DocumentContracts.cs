using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.Documents;

namespace CriatorioVirtual.Application.Documents;

public enum DocumentField
{
    Name = 1,
    RingNumber = 2,
    Sex = 3,
    Species = 4,
    BirthDate = 5,
    BirdPhoto = 6,
    BreedingFarmName = 7,
    GenealogyTree = 8
}

public sealed record DocumentPhotoSnapshot(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record GenealogySnapshotNode(
    string Position,
    string? Name,
    string? RingNumber,
    BirdSex? Sex,
    DateOnly? BirthDate);

public sealed record BreedingFarmDocumentSnapshot
{
    public BreedingFarmDocumentSnapshot(
        string responsibleName,
        string contactEmail,
        string? contactPhone,
        string? officialRegistrationNumber)
    {
        ResponsibleName = RequireText(responsibleName, nameof(responsibleName), 200);
        ContactEmail = RequireText(contactEmail, nameof(contactEmail), 320);
        ContactPhone = Normalize(contactPhone, nameof(contactPhone), 32);
        OfficialRegistrationNumber = Normalize(officialRegistrationNumber, nameof(officialRegistrationNumber), 100);
    }

    public string ResponsibleName { get; }

    public string ContactEmail { get; }

    public string? ContactPhone { get; }

    public string? OfficialRegistrationNumber { get; }

    private static string RequireText(string value, string parameterName, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength)
        {
            throw new ArgumentException($"The {parameterName} value is required and cannot exceed {maxLength} characters.", parameterName);
        }

        return normalized;
    }

    private static string? Normalize(string? value, string parameterName, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maxLength)
        {
            throw new ArgumentException($"The {parameterName} value cannot exceed {maxLength} characters.", parameterName);
        }

        return normalized;
    }
}

public sealed record BirdDocumentSnapshot
{
    public BirdDocumentSnapshot(
        Guid birdId,
        string name,
        string? ringNumber,
        BirdSex sex,
        string species,
        DateOnly? birthDate,
        string breedingFarmName,
        DocumentPhotoSnapshot? photo = null,
        IReadOnlyCollection<GenealogySnapshotNode>? genealogy = null,
        BreedingFarmDocumentSnapshot? breedingFarmDetails = null,
        DateTimeOffset? issuedAtUtc = null,
        string? internalDocumentIdentifier = null)
    {
        if (birdId == Guid.Empty)
        {
            throw new ArgumentException("The bird identifier cannot be empty.", nameof(birdId));
        }

        BirdId = birdId;
        Name = RequireText(name, nameof(name), 100);
        RingNumber = string.IsNullOrWhiteSpace(ringNumber) ? null : ringNumber.Trim();
        if (RingNumber is not null && (RingNumber.Length != 6 || RingNumber.Any(character => !char.IsAsciiDigit(character))))
        {
            throw new ArgumentException("A ring number must contain exactly six digits.", nameof(ringNumber));
        }

        if (!Enum.IsDefined(sex))
        {
            throw new ArgumentOutOfRangeException(nameof(sex), "The bird sex is invalid.");
        }

        Sex = sex;
        Species = RequireText(species, nameof(species), 200);
        BirthDate = birthDate;
        BreedingFarmName = RequireText(breedingFarmName, nameof(breedingFarmName), 200);
        Photo = photo;
        Genealogy = genealogy?.ToArray() ?? [];
        BreedingFarmDetails = breedingFarmDetails;
        if (issuedAtUtc is { } issuedAt && issuedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The issue timestamp must be expressed in UTC.", nameof(issuedAtUtc));
        }

        IssuedAtUtc = issuedAtUtc;
        InternalDocumentIdentifier = NormalizeIdentifier(internalDocumentIdentifier);
    }

    public Guid BirdId { get; }

    public string Name { get; }

    public string? RingNumber { get; }

    public BirdSex Sex { get; }

    public string Species { get; }

    public DateOnly? BirthDate { get; }

    public string BreedingFarmName { get; }

    public DocumentPhotoSnapshot? Photo { get; }

    public IReadOnlyCollection<GenealogySnapshotNode> Genealogy { get; }

    public BreedingFarmDocumentSnapshot? BreedingFarmDetails { get; }

    public DateTimeOffset? IssuedAtUtc { get; }

    public string? InternalDocumentIdentifier { get; }

    private static string RequireText(string value, string parameterName, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength)
        {
            throw new ArgumentException($"The {parameterName} value is required and cannot exceed {maxLength} characters.", parameterName);
        }

        return normalized;
    }

    private static string? NormalizeIdentifier(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > 100)
        {
            throw new ArgumentException(
                "The internal document identifier cannot exceed 100 characters.",
                nameof(value));
        }

        return normalized;
    }
}

public sealed record BadgeRenderConfiguration
{
    public BadgeRenderConfiguration(
        BadgeModelId modelId,
        BadgePrintSize printSize,
        IEnumerable<DocumentField> selectedFields)
    {
        if (!Enum.IsDefined(modelId))
        {
            throw new ArgumentOutOfRangeException(nameof(modelId), "The badge model is invalid.");
        }

        if (!Enum.IsDefined(printSize))
        {
            throw new ArgumentOutOfRangeException(nameof(printSize), "The badge print size is invalid.");
        }

        ArgumentNullException.ThrowIfNull(selectedFields);
        var fields = selectedFields.ToArray();
        if (fields.Length == 0)
        {
            throw new ArgumentException("A badge must select at least one field.", nameof(selectedFields));
        }

        if (fields.Any(field => !Enum.IsDefined(field)) || fields.Distinct().Count() != fields.Length)
        {
            throw new ArgumentException("Badge fields must be allowed and unique.", nameof(selectedFields));
        }

        ModelId = modelId;
        PrintSize = printSize;
        SelectedFields = fields;
    }

    public BadgeModelId ModelId { get; }

    public BadgePrintSize PrintSize { get; }

    public IReadOnlyCollection<DocumentField> SelectedFields { get; }
}

public sealed record GenealogyCertificateRenderConfiguration
{
    public const GenealogyCertificateModelId DefaultModelId = GenealogyCertificateModelId.Institutional;

    public GenealogyCertificateRenderConfiguration(GenealogyCertificateModelId modelId)
    {
        if (!Enum.IsDefined(modelId))
        {
            throw new ArgumentOutOfRangeException(nameof(modelId), "The genealogy certificate model is invalid.");
        }

        ModelId = modelId;
    }

    public GenealogyCertificateModelId ModelId { get; }
}

public sealed record DocumentRenderRequest
{
    public DocumentRenderRequest(
        BirdDocumentType type,
        BirdDocumentSnapshot snapshot,
        BadgeRenderConfiguration? badge = null,
        GenealogyCertificateRenderConfiguration? certificate = null)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), "The document type is invalid.");
        }

        ArgumentNullException.ThrowIfNull(snapshot);
        if (type == BirdDocumentType.Badge && badge is null)
        {
            throw new ArgumentException("A badge document requires a badge configuration.", nameof(badge));
        }

        if (type == BirdDocumentType.Badge && certificate is not null)
        {
            throw new ArgumentException("A badge document cannot define a genealogy certificate configuration.", nameof(certificate));
        }

        if (type == BirdDocumentType.GenealogyCertificate && badge is not null)
        {
            throw new ArgumentException("A genealogy certificate cannot define a badge configuration.", nameof(badge));
        }

        if (type == BirdDocumentType.ProvenanceDocument && (badge is not null || certificate is not null))
        {
            throw new ArgumentException("A provenance document cannot define a document model configuration.", nameof(badge));
        }

        Type = type;
        Snapshot = snapshot;
        Badge = badge;
        Certificate = type == BirdDocumentType.GenealogyCertificate
            ? certificate ?? new GenealogyCertificateRenderConfiguration(
                GenealogyCertificateRenderConfiguration.DefaultModelId)
            : null;
    }

    public BirdDocumentType Type { get; }

    public BirdDocumentSnapshot Snapshot { get; }

    public BadgeRenderConfiguration? Badge { get; }

    public GenealogyCertificateRenderConfiguration? Certificate { get; }
}

public sealed record RenderedDocument(
    byte[] Content,
    string FileName,
    string ContentType,
    int PageCount,
    double WidthMillimeters,
    double HeightMillimeters);

public interface IDocumentRenderer
{
    Task<RenderedDocument> RenderAsync(
        DocumentRenderRequest request,
        CancellationToken cancellationToken = default);
}

public interface IPdfDocumentAssembler
{
    RenderedDocument Assemble(
        IReadOnlyCollection<RenderedDocument> documents,
        string fileName);
}
