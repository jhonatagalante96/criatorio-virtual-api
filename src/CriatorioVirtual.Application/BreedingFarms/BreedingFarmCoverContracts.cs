using System.Text.Json;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.BreedingFarms;

namespace CriatorioVirtual.Application.BreedingFarms;

public static class BreedingFarmCoverUploadLimits
{
    public const long MaxFileLength = 8 * 1024 * 1024;

    public const int MaxCanonicalFileLength = 20 * 1024 * 1024;

    public const long MaxRequestLength = MaxFileLength + (64 * 1024);

    public const int MinimumWidth = 1200;

    public const int MinimumHeight = 400;

    public const int CanonicalWidth = 1920;

    public const int CanonicalHeight = 640;

    public const int MaxDimension = 8192;

    public const long MaxPixelCount = 40_000_000;
}

public sealed record BreedingFarmCoverCanvas(int Width, int Height, string AspectRatio);

public sealed record BreedingFarmCoverSafeArea(
    double X,
    double Y,
    double Width,
    double Height,
    string Description);

public sealed record BreedingFarmCoverTemplateCatalogItem(
    string Id,
    string Name,
    string Version,
    string PreviewUrl,
    BreedingFarmCoverCanvas Canvas,
    BreedingFarmCoverSafeArea SafeArea,
    IReadOnlyList<string> SupportedOptions,
    IReadOnlyDictionary<string, JsonElement> Defaults);

public sealed record GetBreedingFarmCoverTemplatesQuery
    : IQuery<IReadOnlyList<BreedingFarmCoverTemplateCatalogItem>>;

public sealed record GetBreedingFarmCoverTemplatePreviewQuery(string TemplateId, string Version)
    : IQuery<GetBreedingFarmCoverTemplatePreviewResult>;

public enum GetBreedingFarmCoverTemplatePreviewStatus
{
    Available,
    NotFound,
    Inactive,
    VersionUnavailable
}

public sealed record GetBreedingFarmCoverTemplatePreviewResult(
    GetBreedingFarmCoverTemplatePreviewStatus Status,
    byte[]? PreviewImage,
    string? ContentType);

public enum BreedingFarmCoverAccessStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotFound
}

public sealed record BreedingFarmCoverMetadata(
    BreedingFarmCoverSource Source,
    string FileName,
    string ContentType,
    long Length,
    DateTimeOffset UpdatedAtUtc,
    string? TemplateModelId,
    string? TemplateVersion,
    string? TemplateConfiguration);

public sealed record GetBreedingFarmCoverQuery(Guid UserId, Guid BreedingFarmId)
    : IQuery<GetBreedingFarmCoverResult>;

public sealed record GetBreedingFarmCoverResult(
    BreedingFarmCoverAccessStatus Status,
    Guid? BreedingFarmId,
    BreedingFarmCoverMetadata? Cover);

public sealed record GetBreedingFarmCoverContentQuery(Guid UserId, Guid BreedingFarmId)
    : IQuery<GetBreedingFarmCoverContentResult>;

public enum GetBreedingFarmCoverContentStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotFound,
    CoverNotFound,
    StorageUnavailable
}

public sealed record GetBreedingFarmCoverContentResult(
    GetBreedingFarmCoverContentStatus Status,
    string? FileName,
    string? ContentType,
    long? Length,
    Stream? Content);

public sealed record UploadBreedingFarmCoverCommand(
    Guid UserId,
    Guid BreedingFarmId,
    string FileName,
    string ContentType,
    long Length,
    Stream Content) : ICommand<UploadBreedingFarmCoverResult>;

public enum UploadBreedingFarmCoverStatus
{
    Uploaded,
    UserNotFound,
    BreedingFarmNotFound,
    InvalidData,
    RenderingUnavailable,
    StorageUnavailable
}

public sealed record UploadBreedingFarmCoverResult(
    UploadBreedingFarmCoverStatus Status,
    Guid? BreedingFarmId,
    BreedingFarmCoverMetadata? Cover,
    BreedingFarmCoverCleanup? PreviousAsset);

public sealed record RemoveBreedingFarmCoverCommand(Guid UserId, Guid BreedingFarmId)
    : ICommand<RemoveBreedingFarmCoverResult>;

public enum RemoveBreedingFarmCoverStatus
{
    Removed,
    UserNotFound,
    BreedingFarmNotFound
}

public sealed record RemoveBreedingFarmCoverResult(
    RemoveBreedingFarmCoverStatus Status,
    Guid? BreedingFarmId,
    BreedingFarmCoverCleanup? PreviousAsset);

public sealed record BreedingFarmCoverCleanup(Guid BreedingFarmId, string ObjectKey);

public sealed record PreviewBreedingFarmCoverTemplateQuery(
    Guid UserId,
    string TemplateId,
    string Version,
    JsonElement Configuration)
    : IQuery<PreviewBreedingFarmCoverTemplateResult>;

public enum PreviewBreedingFarmCoverTemplateStatus
{
    PreviewReady,
    UserNotFound,
    BreedingFarmNotFound,
    TemplateNotFound,
    TemplateInactive,
    VersionUnavailable,
    InvalidConfiguration,
    RenderingUnavailable
}

public sealed record PreviewBreedingFarmCoverTemplateResult(
    PreviewBreedingFarmCoverTemplateStatus Status,
    byte[]? PngContent);

public sealed record ApplyBreedingFarmCoverTemplateCommand(
    Guid UserId,
    Guid BreedingFarmId,
    string TemplateId,
    string Version,
    JsonElement Configuration)
    : ICommand<ApplyBreedingFarmCoverTemplateResult>;

public enum ApplyBreedingFarmCoverTemplateStatus
{
    Applied,
    UserNotFound,
    BreedingFarmNotFound,
    TemplateNotFound,
    TemplateInactive,
    VersionUnavailable,
    InvalidConfiguration,
    RenderingUnavailable,
    StorageUnavailable
}

public sealed record ApplyBreedingFarmCoverTemplateResult(
    ApplyBreedingFarmCoverTemplateStatus Status,
    Guid? BreedingFarmId,
    BreedingFarmCoverMetadata? Cover,
    BreedingFarmCoverCleanup? PreviousAsset);
