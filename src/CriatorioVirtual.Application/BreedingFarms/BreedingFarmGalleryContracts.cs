using CriatorioVirtual.Application.Messaging;

namespace CriatorioVirtual.Application.BreedingFarms;

public static class BreedingFarmGalleryUploadLimits
{
    public const int MaxImageCount = 100;
    public const long MaxFileLength = 8 * 1024 * 1024;
    public const long MaxRequestLength = MaxFileLength + (64 * 1024);
    public const int MaxWidth = 8192;
    public const int MaxHeight = 8192;
    public const long MaxPixelCount = 40_000_000;
}

public sealed record BreedingFarmGalleryLimits(
    int MaxImageCount,
    long MaxFileLength,
    int MaxWidth,
    int MaxHeight,
    long MaxPixelCount,
    int MaxCaptionLength,
    IReadOnlyCollection<string> SupportedContentTypes)
{
    public static BreedingFarmGalleryLimits Current { get; } = new(
        BreedingFarmGalleryUploadLimits.MaxImageCount,
        BreedingFarmGalleryUploadLimits.MaxFileLength,
        BreedingFarmGalleryUploadLimits.MaxWidth,
        BreedingFarmGalleryUploadLimits.MaxHeight,
        BreedingFarmGalleryUploadLimits.MaxPixelCount,
        CriatorioVirtual.Domain.BreedingFarms.BreedingFarmGalleryImage.CaptionMaxLength,
        ["image/jpeg", "image/png", "image/webp"]);
}

public sealed record GetBreedingFarmGalleryQuery(Guid UserId) : IQuery<GetBreedingFarmGalleryResult>;

public sealed record GetBreedingFarmGalleryResult(
    BreedingFarmVisualIdentityAccessStatus Status,
    Guid? BreedingFarmId,
    IReadOnlyCollection<BreedingFarmGalleryImageMetadata> Items);

public sealed record BreedingFarmGalleryImageMetadata(
    Guid ImageId,
    string FileName,
    string ContentType,
    long Length,
    int Width,
    int Height,
    string? Caption,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record UploadBreedingFarmGalleryImageCommand(
    Guid UserId,
    string FileName,
    string ContentType,
    long Length,
    string? Caption,
    Stream Content) : ICommand<UploadBreedingFarmGalleryImageResult>;

public enum UploadBreedingFarmGalleryImageStatus
{
    Created,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    LimitExceeded,
    InvalidData,
    StorageUnavailable
}

public sealed record UploadBreedingFarmGalleryImageResult(
    UploadBreedingFarmGalleryImageStatus Status,
    Guid? BreedingFarmId,
    BreedingFarmGalleryImageMetadata? Image);

public sealed record UpdateBreedingFarmGalleryCaptionCommand(
    Guid UserId,
    Guid ImageId,
    string? Caption) : ICommand<UpdateBreedingFarmGalleryCaptionResult>;

public enum UpdateBreedingFarmGalleryCaptionStatus
{
    Updated,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    ImageNotFound,
    InvalidData
}

public sealed record UpdateBreedingFarmGalleryCaptionResult(
    UpdateBreedingFarmGalleryCaptionStatus Status,
    BreedingFarmGalleryImageMetadata? Image);

public sealed record DeleteBreedingFarmGalleryImageCommand(Guid UserId, Guid ImageId)
    : ICommand<DeleteBreedingFarmGalleryImageResult>;

public enum DeleteBreedingFarmGalleryImageStatus
{
    Deleted,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    ImageNotFound,
    StorageCleanupPending
}

public sealed record BreedingFarmGalleryImageCleanup(Guid BreedingFarmId, Guid ImageId, string ObjectKey);

public sealed record DeleteBreedingFarmGalleryImageResult(
    DeleteBreedingFarmGalleryImageStatus Status,
    BreedingFarmGalleryImageCleanup? Cleanup);

public sealed record GetBreedingFarmGalleryImageContentQuery(Guid UserId, Guid ImageId)
    : IQuery<GetBreedingFarmGalleryImageContentResult>;

public enum GetBreedingFarmGalleryImageContentStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    ImageNotFound,
    StorageUnavailable
}

public sealed record BreedingFarmGalleryImageContent(
    string FileName,
    string ContentType,
    long Length,
    Stream Content);

public sealed record GetBreedingFarmGalleryImageContentResult(
    GetBreedingFarmGalleryImageContentStatus Status,
    BreedingFarmGalleryImageContent? Content);
