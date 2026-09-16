using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.BreedingFarms;

public sealed class GetBreedingFarmVisualIdentityQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<GetBreedingFarmVisualIdentityQuery, GetBreedingFarmVisualIdentityResult>
{
    public async Task<GetBreedingFarmVisualIdentityResult> Handle(
        GetBreedingFarmVisualIdentityQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var access = await BreedingFarmVisualIdentityAccess.FindCurrentOwnerFarmAsync(
            dbContext,
            query.UserId,
            tracking: false,
            cancellationToken);
        if (access.Status != BreedingFarmVisualIdentityAccessStatus.Success)
        {
            return new(access.Status, null, null);
        }

        return new(
            BreedingFarmVisualIdentityAccessStatus.Success,
            access.Farm!.Id,
            BreedingFarmVisualIdentityMapping.ToMetadata(access.Farm));
    }
}

public sealed class GetBreedingFarmVisualIdentityContentQueryHandler(
    CriatorioVirtualDbContext dbContext,
    IPrivateObjectStorage storage)
    : IQueryHandler<GetBreedingFarmVisualIdentityContentQuery, GetBreedingFarmVisualIdentityContentResult>
{
    public async Task<GetBreedingFarmVisualIdentityContentResult> Handle(
        GetBreedingFarmVisualIdentityContentQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var access = await BreedingFarmVisualIdentityAccess.FindCurrentOwnerFarmAsync(
            dbContext,
            query.UserId,
            tracking: false,
            cancellationToken);
        if (access.Status != BreedingFarmVisualIdentityAccessStatus.Success)
        {
            return new(ToContentStatus(access.Status), null, null, null, null);
        }

        var farm = access.Farm!;
        var identity = farm.GetVisualIdentity();
        if (identity is null ||
            identity.Source != BreedingFarmVisualIdentitySource.Upload ||
            identity.FileName is null ||
            identity.ContentType is null ||
            identity.Length is null)
        {
            return new(GetBreedingFarmVisualIdentityContentStatus.IdentityNotFound, null, null, null, null);
        }

        try
        {
            var content = await storage.OpenReadAsync(farm.Id, identity.Reference, cancellationToken);
            return new(
                GetBreedingFarmVisualIdentityContentStatus.Success,
                identity.FileName,
                identity.ContentType,
                identity.Length,
                content);
        }
        catch (FileNotFoundException)
        {
            return StorageUnavailable();
        }
        catch (DirectoryNotFoundException)
        {
            return StorageUnavailable();
        }
        catch (IOException)
        {
            return StorageUnavailable();
        }
        catch (UnauthorizedAccessException)
        {
            return StorageUnavailable();
        }
    }

    private static GetBreedingFarmVisualIdentityContentStatus ToContentStatus(
        BreedingFarmVisualIdentityAccessStatus status) => status switch
        {
            BreedingFarmVisualIdentityAccessStatus.UserNotFound =>
                GetBreedingFarmVisualIdentityContentStatus.UserNotFound,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotSelected =>
                GetBreedingFarmVisualIdentityContentStatus.BreedingFarmNotSelected,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotFound =>
                GetBreedingFarmVisualIdentityContentStatus.BreedingFarmNotFound,
            _ => GetBreedingFarmVisualIdentityContentStatus.IdentityNotFound
        };

    private static GetBreedingFarmVisualIdentityContentResult StorageUnavailable() =>
        new(GetBreedingFarmVisualIdentityContentStatus.StorageUnavailable, null, null, null, null);
}

public sealed class UploadBreedingFarmVisualIdentityCommandHandler(
    CriatorioVirtualDbContext dbContext,
    BreedingFarmVisualIdentityUploadSession session)
    : ICommandHandler<UploadBreedingFarmVisualIdentityCommand, UploadBreedingFarmVisualIdentityResult>
{
    public async Task<UploadBreedingFarmVisualIdentityResult> Handle(
        UploadBreedingFarmVisualIdentityCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (session.Status != UploadBreedingFarmVisualIdentityStatus.Updated || session.StoredObject is null)
        {
            return new(session.Status, null, null, null);
        }

        var storedObject = session.StoredObject;
        var access = await BreedingFarmVisualIdentityAccess.FindCurrentOwnerFarmAsync(
            dbContext,
            command.UserId,
            tracking: true,
            cancellationToken);
        if (access.Status != BreedingFarmVisualIdentityAccessStatus.Success)
        {
            return new(ToUploadStatus(access.Status), null, null, null);
        }

        var farm = access.Farm!;
        if (storedObject.BreedingFarmId != farm.Id)
        {
            return new(UploadBreedingFarmVisualIdentityStatus.BreedingFarmNotFound, null, null, null);
        }

        var previous = farm.GetVisualIdentity();
        var previousUpload = previous?.Source == BreedingFarmVisualIdentitySource.Upload
            ? new BreedingFarmVisualIdentityCleanup(farm.Id, previous.Reference)
            : null;
        farm.SetVisualIdentity(
            BreedingFarmVisualIdentitySource.Upload,
            storedObject.ObjectKey,
            command.FileName,
            storedObject.ContentType,
            storedObject.Length,
            DateTimeOffset.UtcNow);

        return new(
            UploadBreedingFarmVisualIdentityStatus.Updated,
            farm.Id,
            BreedingFarmVisualIdentityMapping.ToMetadata(farm),
            previousUpload);
    }

    private static UploadBreedingFarmVisualIdentityStatus ToUploadStatus(
        BreedingFarmVisualIdentityAccessStatus status) => status switch
        {
            BreedingFarmVisualIdentityAccessStatus.UserNotFound => UploadBreedingFarmVisualIdentityStatus.UserNotFound,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotSelected => UploadBreedingFarmVisualIdentityStatus.BreedingFarmNotSelected,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotFound => UploadBreedingFarmVisualIdentityStatus.BreedingFarmNotFound,
            _ => UploadBreedingFarmVisualIdentityStatus.InvalidData
        };
}

public sealed class RemoveBreedingFarmVisualIdentityCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<RemoveBreedingFarmVisualIdentityCommand, RemoveBreedingFarmVisualIdentityResult>
{
    public async Task<RemoveBreedingFarmVisualIdentityResult> Handle(
        RemoveBreedingFarmVisualIdentityCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var access = await BreedingFarmVisualIdentityAccess.FindCurrentOwnerFarmAsync(
            dbContext,
            command.UserId,
            tracking: true,
            cancellationToken);
        if (access.Status != BreedingFarmVisualIdentityAccessStatus.Success)
        {
            return new(ToRemoveStatus(access.Status), null, null);
        }

        var farm = access.Farm!;
        var previous = farm.RemoveVisualIdentity(DateTimeOffset.UtcNow);
        var previousUpload = previous?.Source == BreedingFarmVisualIdentitySource.Upload
            ? new BreedingFarmVisualIdentityCleanup(farm.Id, previous.Reference)
            : null;
        return new(RemoveBreedingFarmVisualIdentityStatus.Removed, farm.Id, previousUpload);
    }

    private static RemoveBreedingFarmVisualIdentityStatus ToRemoveStatus(
        BreedingFarmVisualIdentityAccessStatus status) => status switch
        {
            BreedingFarmVisualIdentityAccessStatus.UserNotFound => RemoveBreedingFarmVisualIdentityStatus.UserNotFound,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotSelected => RemoveBreedingFarmVisualIdentityStatus.BreedingFarmNotSelected,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotFound => RemoveBreedingFarmVisualIdentityStatus.BreedingFarmNotFound,
            _ => RemoveBreedingFarmVisualIdentityStatus.BreedingFarmNotFound
        };
}

public sealed class BreedingFarmVisualIdentityStoragePostProcessor(
    IPrivateObjectStorage storage,
    BreedingFarmVisualIdentityUploadSession uploadSession)
    : ICommandPostProcessor<UploadBreedingFarmVisualIdentityCommand, UploadBreedingFarmVisualIdentityResult>,
        ICommandPostProcessor<RemoveBreedingFarmVisualIdentityCommand, RemoveBreedingFarmVisualIdentityResult>
{
    public async Task<UploadBreedingFarmVisualIdentityResult> Process(
        UploadBreedingFarmVisualIdentityCommand command,
        UploadBreedingFarmVisualIdentityResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(result);

        if (result.Status == UploadBreedingFarmVisualIdentityStatus.Updated)
        {
            if (result.PreviousUpload is not null)
            {
                await BreedingFarmVisualIdentityUploadSession.TryDeleteAsync(
                    storage,
                    result.PreviousUpload.BreedingFarmId,
                    result.PreviousUpload.ObjectKey,
                    cancellationToken);
            }
        }
        else
        {
            // A successful pre-transaction write is removed when authorization changed before commit.
            await DeleteNewUploadAsync(cancellationToken);
        }

        return result;
    }

    public async Task<RemoveBreedingFarmVisualIdentityResult> Process(
        RemoveBreedingFarmVisualIdentityCommand command,
        RemoveBreedingFarmVisualIdentityResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status == RemoveBreedingFarmVisualIdentityStatus.Removed && result.PreviousUpload is not null)
        {
            await BreedingFarmVisualIdentityUploadSession.TryDeleteAsync(
                storage,
                result.PreviousUpload.BreedingFarmId,
                result.PreviousUpload.ObjectKey,
                cancellationToken);
        }

        return result;
    }

    private async Task DeleteNewUploadAsync(CancellationToken cancellationToken)
    {
        if (uploadSession.StoredObject is not { } storedObject)
        {
            return;
        }

        await BreedingFarmVisualIdentityUploadSession.TryDeleteAsync(
            storage,
            storedObject.BreedingFarmId,
            storedObject.ObjectKey,
            cancellationToken);
    }
}

internal static class BreedingFarmVisualIdentityMapping
{
    public static BreedingFarmVisualIdentityMetadata? ToMetadata(BreedingFarm farm)
    {
        var identity = farm.GetVisualIdentity();
        return identity is null
            ? null
            : new(
                identity.Source,
                identity.FileName,
                identity.ContentType,
                identity.Length,
                farm.UpdatedAtUtc);
    }
}
