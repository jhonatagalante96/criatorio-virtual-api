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
