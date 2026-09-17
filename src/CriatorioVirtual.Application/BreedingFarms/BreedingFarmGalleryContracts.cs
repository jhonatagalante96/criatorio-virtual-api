using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;

namespace CriatorioVirtual.Application.BreedingFarms;

public static class BreedingFarmGalleryLimits
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public static BreedingFarmGalleryContractLimits Current { get; } = new(
        DefaultPageSize,
        MaxPageSize,
        BirdAttachmentUploadLimits.MaxFileLength,
        BirdAttachmentUploadLimits.MaxVideoFileLength,
        CriatorioVirtual.Domain.Birds.BirdAttachment.CaptionMaxLength,
        ["image/gif", "image/heic", "image/heif", "image/jpeg", "image/png", "image/webp"],
        ["video/mp4", "video/webm"]);
}

public sealed record BreedingFarmGalleryContractLimits(
    int DefaultPageSize,
    int MaxPageSize,
    long MaxImageFileLength,
    long MaxVideoFileLength,
    int MaxCaptionLength,
    IReadOnlyCollection<string> SupportedImageContentTypes,
    IReadOnlyCollection<string> SupportedVideoContentTypes);

public enum BreedingFarmGalleryMediaType
{
    Image,
    Video
}

public sealed record GetBreedingFarmGalleryQuery(
    Guid UserId,
    int Page,
    int PageSize,
    BreedingFarmGalleryMediaType? Type,
    Guid? BirdId) : IQuery<GetBreedingFarmGalleryResult>;

public enum GetBreedingFarmGalleryStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound,
    InvalidData
}

public sealed record BreedingFarmGalleryMediaResult(
    Guid MediaId,
    Guid? BirdId,
    string? BirdName,
    string? BirdRingNumber,
    string FileName,
    string ContentType,
    long Length,
    string? Caption,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool IsPrimary);

public sealed record GetBreedingFarmGalleryResult(
    GetBreedingFarmGalleryStatus Status,
    Guid? BreedingFarmId,
    IReadOnlyCollection<BreedingFarmGalleryMediaResult> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record GetBreedingFarmGalleryMediaContentQuery(Guid UserId, Guid MediaId)
    : IQuery<GetBreedingFarmGalleryMediaContentResult>;

public enum GetBreedingFarmGalleryMediaContentStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    MediaNotFound,
    StorageUnavailable
}

public sealed record BreedingFarmGalleryMediaContent(
    string FileName,
    string ContentType,
    long Length,
    Stream Content);

public sealed record GetBreedingFarmGalleryMediaContentResult(
    GetBreedingFarmGalleryMediaContentStatus Status,
    BreedingFarmGalleryMediaContent? Content);

public sealed record UpdateBreedingFarmGalleryCaptionCommand(
    Guid UserId,
    Guid MediaId,
    string? Caption) : ICommand<UpdateBreedingFarmGalleryCaptionResult>;

public enum UpdateBreedingFarmGalleryCaptionStatus
{
    Updated,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    MediaNotFound,
    BirdTransferPending,
    InvalidData
}

public sealed record UpdateBreedingFarmGalleryCaptionResult(
    UpdateBreedingFarmGalleryCaptionStatus Status,
    BreedingFarmGalleryMediaResult? Media);
