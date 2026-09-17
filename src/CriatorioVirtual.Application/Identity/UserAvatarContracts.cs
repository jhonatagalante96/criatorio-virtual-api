using CriatorioVirtual.Application.Messaging;
using System.IO;

namespace CriatorioVirtual.Application.Identity;

public static class UserAvatarUploadLimits
{
    public const long MaxFileLength = 10 * 1024 * 1024;

    public const long MaxRequestLength = MaxFileLength + (64 * 1024);

    public const int MaxWidth = 8192;

    public const int MaxHeight = 8192;

    public const long MaxPixelCount = 40_000_000;
}

public sealed record UploadUserAvatarCommand(
    Guid UserId,
    string FileName,
    string ContentType,
    long Length,
    Stream Content) : ICommand<UploadUserAvatarResult>;

public enum UploadUserAvatarStatus
{
    Updated,
    UserNotFound,
    InvalidData,
    StorageUnavailable
}

public sealed record UploadUserAvatarResult(
    UploadUserAvatarStatus Status,
    UserAvatarCleanup? PreviousAvatar);

public sealed record RemoveUserAvatarCommand(Guid UserId)
    : ICommand<RemoveUserAvatarResult>;

public enum RemoveUserAvatarStatus
{
    Removed,
    UserNotFound
}

public sealed record RemoveUserAvatarResult(
    RemoveUserAvatarStatus Status,
    UserAvatarCleanup? PreviousAvatar);

public sealed record GetUserAvatarContentQuery(Guid UserId)
    : IQuery<GetUserAvatarContentResult>;

public enum GetUserAvatarContentStatus
{
    Success,
    UserNotFound,
    AvatarNotFound,
    StorageUnavailable
}

public sealed record GetUserAvatarContentResult(
    GetUserAvatarContentStatus Status,
    string? ContentType,
    Stream? Content);

public sealed record UserAvatarCleanup(Guid UserId, string ObjectKey);

public sealed record UserAvatarStoredObject(string ObjectKey, string ContentType, long Length);

public interface IUserAvatarStorage
{
    Task<UserAvatarStoredObject> PutAsync(
        Guid userId,
        string objectKey,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(
        Guid userId,
        string objectKey,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid userId,
        string objectKey,
        CancellationToken cancellationToken = default);
}
