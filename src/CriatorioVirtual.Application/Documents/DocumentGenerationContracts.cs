using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.Documents;

namespace CriatorioVirtual.Application.Documents;

public sealed record GenerateBirdDocumentCommand(
    Guid UserId,
    Guid BirdId,
    BirdDocumentType Type,
    BadgeModelId? ModelId,
    BadgePrintSize? PrintSize,
    IReadOnlyCollection<DocumentField>? SelectedFields) : ICommand<GenerateBirdDocumentResult>;

public enum GenerateBirdDocumentStatus
{
    Generated,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound,
    InvalidData,
    StorageUnavailable
}

public sealed record GenerateBirdDocumentResult(
    GenerateBirdDocumentStatus Status,
    BirdDocumentResult? Document)
{
    public static GenerateBirdDocumentResult Generated(BirdDocumentResult document) =>
        new(GenerateBirdDocumentStatus.Generated, document);

    public static GenerateBirdDocumentResult UserNotFound() =>
        new(GenerateBirdDocumentStatus.UserNotFound, null);

    public static GenerateBirdDocumentResult BreedingFarmNotSelected() =>
        new(GenerateBirdDocumentStatus.BreedingFarmNotSelected, null);

    public static GenerateBirdDocumentResult BreedingFarmNotFound() =>
        new(GenerateBirdDocumentStatus.BreedingFarmNotFound, null);

    public static GenerateBirdDocumentResult BirdNotFound() =>
        new(GenerateBirdDocumentStatus.BirdNotFound, null);

    public static GenerateBirdDocumentResult InvalidData() =>
        new(GenerateBirdDocumentStatus.InvalidData, null);

    public static GenerateBirdDocumentResult StorageUnavailable() =>
        new(GenerateBirdDocumentStatus.StorageUnavailable, null);
}

public sealed record BirdDocumentResult(
    Guid DocumentId,
    Guid BirdId,
    BirdDocumentType Type,
    BadgeModelId? ModelId,
    BadgePrintSize? PrintSize,
    IReadOnlyCollection<DocumentField> SelectedFields,
    string FileName,
    string ContentType,
    long Length,
    int PageCount,
    double WidthMillimeters,
    double HeightMillimeters,
    DateTimeOffset GeneratedAtUtc);

public sealed record GetBirdDocumentContentQuery(
    Guid UserId,
    Guid BirdId,
    Guid DocumentId) : IQuery<GetBirdDocumentContentResult>;

public sealed record ListBirdDocumentsQuery(
    Guid UserId,
    Guid BirdId) : IQuery<ListBirdDocumentsResult>;

public enum ListBirdDocumentsStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound
}

public sealed record ListBirdDocumentsResult(
    ListBirdDocumentsStatus Status,
    Guid? BreedingFarmId,
    Guid? BirdId,
    IReadOnlyCollection<BirdDocumentListItem>? Documents)
{
    public static ListBirdDocumentsResult Succeeded(
        Guid breedingFarmId,
        Guid birdId,
        IReadOnlyCollection<BirdDocumentListItem> documents) =>
        new(ListBirdDocumentsStatus.Success, breedingFarmId, birdId, documents);

    public static ListBirdDocumentsResult UserNotFound() =>
        new(ListBirdDocumentsStatus.UserNotFound, null, null, null);

    public static ListBirdDocumentsResult BreedingFarmNotSelected() =>
        new(ListBirdDocumentsStatus.BreedingFarmNotSelected, null, null, null);

    public static ListBirdDocumentsResult BreedingFarmNotFound() =>
        new(ListBirdDocumentsStatus.BreedingFarmNotFound, null, null, null);

    public static ListBirdDocumentsResult BirdNotFound() =>
        new(ListBirdDocumentsStatus.BirdNotFound, null, null, null);
}

public sealed record BirdDocumentListItem(
    Guid DocumentId,
    Guid BirdId,
    BirdDocumentType Type,
    BadgeModelId? ModelId,
    BadgePrintSize? PrintSize,
    IReadOnlyCollection<DocumentField> SelectedFields,
    string FileName,
    string ContentType,
    long Length,
    DateTimeOffset GeneratedAtUtc);

public sealed record ReissueBirdDocumentCommand(
    Guid UserId,
    Guid BirdId,
    Guid DocumentId,
    BadgeModelId? ModelId,
    BadgePrintSize? PrintSize,
    IReadOnlyCollection<DocumentField>? SelectedFields) : ICommand<ReissueBirdDocumentResult>;

public enum ReissueBirdDocumentStatus
{
    Reissued,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound,
    DocumentNotFound,
    InvalidData,
    StorageUnavailable
}

public sealed record ReissueBirdDocumentResult(
    ReissueBirdDocumentStatus Status,
    BirdDocumentResult? Document)
{
    public static ReissueBirdDocumentResult Reissued(BirdDocumentResult document) =>
        new(ReissueBirdDocumentStatus.Reissued, document);

    public static ReissueBirdDocumentResult UserNotFound() =>
        new(ReissueBirdDocumentStatus.UserNotFound, null);

    public static ReissueBirdDocumentResult BreedingFarmNotSelected() =>
        new(ReissueBirdDocumentStatus.BreedingFarmNotSelected, null);

    public static ReissueBirdDocumentResult BreedingFarmNotFound() =>
        new(ReissueBirdDocumentStatus.BreedingFarmNotFound, null);

    public static ReissueBirdDocumentResult BirdNotFound() =>
        new(ReissueBirdDocumentStatus.BirdNotFound, null);

    public static ReissueBirdDocumentResult DocumentNotFound() =>
        new(ReissueBirdDocumentStatus.DocumentNotFound, null);

    public static ReissueBirdDocumentResult InvalidData() =>
        new(ReissueBirdDocumentStatus.InvalidData, null);

    public static ReissueBirdDocumentResult StorageUnavailable() =>
        new(ReissueBirdDocumentStatus.StorageUnavailable, null);
}

public enum GetBirdDocumentContentStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound,
    DocumentNotFound,
    StorageUnavailable
}

public sealed record GetBirdDocumentContentResult(
    GetBirdDocumentContentStatus Status,
    BirdDocumentContent? Content)
{
    public static GetBirdDocumentContentResult Succeeded(BirdDocumentContent content) =>
        new(GetBirdDocumentContentStatus.Success, content);

    public static GetBirdDocumentContentResult UserNotFound() =>
        new(GetBirdDocumentContentStatus.UserNotFound, null);

    public static GetBirdDocumentContentResult BreedingFarmNotSelected() =>
        new(GetBirdDocumentContentStatus.BreedingFarmNotSelected, null);

    public static GetBirdDocumentContentResult BreedingFarmNotFound() =>
        new(GetBirdDocumentContentStatus.BreedingFarmNotFound, null);

    public static GetBirdDocumentContentResult BirdNotFound() =>
        new(GetBirdDocumentContentStatus.BirdNotFound, null);

    public static GetBirdDocumentContentResult DocumentNotFound() =>
        new(GetBirdDocumentContentStatus.DocumentNotFound, null);

    public static GetBirdDocumentContentResult StorageUnavailable() =>
        new(GetBirdDocumentContentStatus.StorageUnavailable, null);
}

public sealed record BirdDocumentContent(
    Guid DocumentId,
    string FileName,
    string ContentType,
    long Length,
    Stream Content);
