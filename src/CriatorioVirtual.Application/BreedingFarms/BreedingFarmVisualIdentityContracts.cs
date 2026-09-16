using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.BreedingFarms;

namespace CriatorioVirtual.Application.BreedingFarms;

public static class BreedingFarmVisualIdentityUploadLimits
{
    public const long MaxFileLength = 10 * 1024 * 1024;

    public const long MaxRequestLength = MaxFileLength + (64 * 1024);

    public const int MaxWidth = 8192;

    public const int MaxHeight = 8192;

    public const long MaxPixelCount = 40_000_000;
}

public sealed record GetBreedingFarmVisualIdentityQuery(Guid UserId)
    : IQuery<GetBreedingFarmVisualIdentityResult>;

public enum BreedingFarmVisualIdentityAccessStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound
}

public sealed record GetBreedingFarmVisualIdentityResult(
    BreedingFarmVisualIdentityAccessStatus Status,
    Guid? BreedingFarmId,
    BreedingFarmVisualIdentityMetadata? Identity);

public sealed record BreedingFarmVisualIdentityMetadata(
    BreedingFarmVisualIdentitySource Source,
    string? FileName,
    string? ContentType,
    long? Length,
    DateTimeOffset UpdatedAtUtc);

public sealed record GetBreedingFarmVisualIdentityContentQuery(Guid UserId)
    : IQuery<GetBreedingFarmVisualIdentityContentResult>;

public enum GetBreedingFarmVisualIdentityContentStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    IdentityNotFound,
    StorageUnavailable
}

public sealed record GetBreedingFarmVisualIdentityContentResult(
    GetBreedingFarmVisualIdentityContentStatus Status,
    string? FileName,
    string? ContentType,
    long? Length,
    Stream? Content);

public sealed record UploadBreedingFarmVisualIdentityCommand(
    Guid UserId,
    string FileName,
    string ContentType,
    long Length,
    Stream Content) : ICommand<UploadBreedingFarmVisualIdentityResult>;

public enum UploadBreedingFarmVisualIdentityStatus
{
    Updated,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    InvalidData,
    StorageUnavailable
}

public sealed record UploadBreedingFarmVisualIdentityResult(
    UploadBreedingFarmVisualIdentityStatus Status,
    Guid? BreedingFarmId,
    BreedingFarmVisualIdentityMetadata? Identity,
    BreedingFarmVisualIdentityCleanup? PreviousUpload);

public sealed record RemoveBreedingFarmVisualIdentityCommand(Guid UserId)
    : ICommand<RemoveBreedingFarmVisualIdentityResult>;

public enum RemoveBreedingFarmVisualIdentityStatus
{
    Removed,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound
}

public sealed record RemoveBreedingFarmVisualIdentityResult(
    RemoveBreedingFarmVisualIdentityStatus Status,
    Guid? BreedingFarmId,
    BreedingFarmVisualIdentityCleanup? PreviousUpload);

public sealed record BreedingFarmVisualIdentityCleanup(Guid BreedingFarmId, string ObjectKey);
