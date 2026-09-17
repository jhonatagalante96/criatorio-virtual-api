using Amazon.Runtime;
using CriatorioVirtual.Application.Identity;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Identity;

public sealed class UserAvatarUploadSession(IUserAvatarStorage storage) : ICommandFailureCompensator
{
    public Guid? StoredUserId { get; private set; }

    public UploadUserAvatarStatus Status { get; private set; } = UploadUserAvatarStatus.InvalidData;

    public UserAvatarStoredObject? StoredObject { get; private set; }

    public void SetStatus(UploadUserAvatarStatus status) => Status = status;

    public void SetStoredObject(Guid userId, UserAvatarStoredObject descriptor)
    {
        StoredUserId = userId;
        StoredObject = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        Status = UploadUserAvatarStatus.Updated;
    }

    public async Task CompensateAsync(CancellationToken cancellationToken)
    {
        if (StoredObject is { } storedObject)
        {
            await UserAvatarStorageCleanup.TryDeleteAsync(
                storage,
                StoredUserId!.Value,
                storedObject.ObjectKey,
                cancellationToken);
        }
    }
}

public sealed class UploadUserAvatarPreProcessor(
    CriatorioVirtualDbContext dbContext,
    IUserAvatarStorage storage,
    UserAvatarUploadSession session) : ICommandPreProcessor<UploadUserAvatarCommand>
{
    public async Task Process(UploadUserAvatarCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await dbContext.Users.AsNoTracking().AnyAsync(user => user.Id == command.UserId, cancellationToken))
        {
            session.SetStatus(UploadUserAvatarStatus.UserNotFound);
            return;
        }

        if (command.Length is <= 0 or > UserAvatarUploadLimits.MaxFileLength || !command.Content.CanRead)
        {
            session.SetStatus(UploadUserAvatarStatus.InvalidData);
            return;
        }

        await using var content = new MemoryStream((int)command.Length);
        if (!await TryCopyContentAsync(command.Content, content, command.Length, cancellationToken) ||
            !content.TryGetBuffer(out var buffer) ||
            !UserAvatarImageValidation.TryValidate(
                command.FileName,
                command.ContentType,
                buffer.AsSpan(0, (int)content.Length),
                out _))
        {
            session.SetStatus(UploadUserAvatarStatus.InvalidData);
            return;
        }

        var objectKey = $"user-avatars/{Guid.NewGuid():N}";
        content.Position = 0;
        try
        {
            // The authenticated user is the storage namespace owner for this asset; no farm is associated with it.
            var storedObject = await storage.PutAsync(
                command.UserId,
                objectKey,
                command.FileName,
                command.ContentType.Trim().ToLowerInvariant(),
                content,
                cancellationToken);
            session.SetStoredObject(command.UserId, storedObject);
            if (!string.Equals(storedObject.ObjectKey, objectKey, StringComparison.Ordinal) ||
                storedObject.Length != content.Length)
            {
                session.SetStatus(UploadUserAvatarStatus.StorageUnavailable);
            }
        }
        catch (ArgumentException)
        {
            session.SetStatus(UploadUserAvatarStatus.InvalidData);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            session.SetStatus(UploadUserAvatarStatus.StorageUnavailable);
        }
    }

    private static async Task<bool> TryCopyContentAsync(
        Stream source,
        MemoryStream destination,
        long expectedLength,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                return destination.Length > 0 && destination.Length == expectedLength;
            }

            if (destination.Length + read > UserAvatarUploadLimits.MaxFileLength)
            {
                return false;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static bool IsStorageException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or AmazonServiceException;
}

public sealed class UploadUserAvatarCommandHandler(
    CriatorioVirtualDbContext dbContext,
    UserAvatarUploadSession session)
    : ICommandHandler<UploadUserAvatarCommand, UploadUserAvatarResult>
{
    public async Task<UploadUserAvatarResult> Handle(
        UploadUserAvatarCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (session.Status != UploadUserAvatarStatus.Updated || session.StoredObject is not { } storedObject)
        {
            return new(session.Status, null);
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.Id == command.UserId,
            cancellationToken);
        if (user is null)
        {
            return new(UploadUserAvatarStatus.UserNotFound, null);
        }

        var previousAvatar = user.AvatarObjectKey is { Length: > 0 } previousKey
            ? new UserAvatarCleanup(user.Id, previousKey)
            : null;
        user.AvatarObjectKey = storedObject.ObjectKey;
        user.AvatarContentType = storedObject.ContentType;
        user.ConcurrencyStamp = Guid.NewGuid().ToString("N");

        return new(UploadUserAvatarStatus.Updated, previousAvatar);
    }
}

public sealed class RemoveUserAvatarCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<RemoveUserAvatarCommand, RemoveUserAvatarResult>
{
    public async Task<RemoveUserAvatarResult> Handle(
        RemoveUserAvatarCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var user = await dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.Id == command.UserId,
            cancellationToken);
        if (user is null)
        {
            return new(RemoveUserAvatarStatus.UserNotFound, null);
        }

        var previousAvatar = user.AvatarObjectKey is { Length: > 0 } previousKey
            ? new UserAvatarCleanup(user.Id, previousKey)
            : null;
        user.AvatarObjectKey = null;
        user.AvatarContentType = null;
        user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        return new(RemoveUserAvatarStatus.Removed, previousAvatar);
    }
}

public sealed class GetUserAvatarContentQueryHandler(
    CriatorioVirtualDbContext dbContext,
    IUserAvatarStorage storage)
    : IQueryHandler<GetUserAvatarContentQuery, GetUserAvatarContentResult>
{
    public async Task<GetUserAvatarContentResult> Handle(
        GetUserAvatarContentQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == query.UserId,
            cancellationToken);
        if (user is null)
        {
            return new(GetUserAvatarContentStatus.UserNotFound, null, null);
        }

        if (string.IsNullOrWhiteSpace(user.AvatarObjectKey) || string.IsNullOrWhiteSpace(user.AvatarContentType))
        {
            return new(GetUserAvatarContentStatus.AvatarNotFound, null, null);
        }

        try
        {
            var content = await storage.OpenReadAsync(user.Id, user.AvatarObjectKey, cancellationToken);
            return new(GetUserAvatarContentStatus.Success, user.AvatarContentType, content);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return new(GetUserAvatarContentStatus.StorageUnavailable, null, null);
        }
    }

    private static bool IsStorageException(Exception exception) =>
        exception is FileNotFoundException or DirectoryNotFoundException or IOException or
            UnauthorizedAccessException or AmazonServiceException;
}

public sealed class UserAvatarStoragePostProcessor(
    IUserAvatarStorage storage,
    UserAvatarUploadSession uploadSession)
    : ICommandPostProcessor<UploadUserAvatarCommand, UploadUserAvatarResult>,
        ICommandPostProcessor<RemoveUserAvatarCommand, RemoveUserAvatarResult>
{
    public async Task<UploadUserAvatarResult> Process(
        UploadUserAvatarCommand command,
        UploadUserAvatarResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status == UploadUserAvatarStatus.Updated)
        {
            if (result.PreviousAvatar is { } previousAvatar)
            {
                await UserAvatarStorageCleanup.TryDeleteAsync(
                    storage,
                    previousAvatar.UserId,
                    previousAvatar.ObjectKey,
                    cancellationToken);
            }
        }
        else if (uploadSession.StoredObject is { } storedObject)
        {
            await UserAvatarStorageCleanup.TryDeleteAsync(
                storage,
                uploadSession.StoredUserId!.Value,
                storedObject.ObjectKey,
                cancellationToken);
        }

        return result;
    }

    public async Task<RemoveUserAvatarResult> Process(
        RemoveUserAvatarCommand command,
        RemoveUserAvatarResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status == RemoveUserAvatarStatus.Removed && result.PreviousAvatar is { } previousAvatar)
        {
            await UserAvatarStorageCleanup.TryDeleteAsync(
                storage,
                previousAvatar.UserId,
                previousAvatar.ObjectKey,
                cancellationToken);
        }

        return result;
    }
}

internal static class UserAvatarStorageCleanup
{
    public static async Task TryDeleteAsync(
        IUserAvatarStorage storage,
        Guid userId,
        string objectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteAsync(userId, objectKey, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or AmazonServiceException)
        {
            // Object cleanup is best effort after the database has committed.
        }
    }
}
