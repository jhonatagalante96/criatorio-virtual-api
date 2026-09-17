using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.BreedingFarms;

public sealed class GetBreedingFarmGalleryQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<GetBreedingFarmGalleryQuery, GetBreedingFarmGalleryResult>
{
    public async Task<GetBreedingFarmGalleryResult> Handle(
        GetBreedingFarmGalleryQuery query,
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
            return new(access.Status, null, []);
        }

        var farmId = access.Farm!.Id;
        var images = await dbContext.BreedingFarmGalleryImages
            .AsNoTracking()
            .Where(image => image.BreedingFarmId == farmId && image.DeletedAtUtc == null)
            .OrderByDescending(image => image.CreatedAtUtc)
            .ThenByDescending(image => image.Id)
            .Select(image => ToMetadata(image))
            .ToArrayAsync(cancellationToken);

        return new(BreedingFarmVisualIdentityAccessStatus.Success, farmId, images);
    }

    internal static BreedingFarmGalleryImageMetadata ToMetadata(BreedingFarmGalleryImage image) =>
        new(
            image.Id,
            image.FileName,
            image.ContentType,
            image.Length,
            image.Width,
            image.Height,
            image.Caption,
            image.CreatedAtUtc,
            image.UpdatedAtUtc);
}

public sealed class UploadBreedingFarmGalleryImageCommandHandler(
    CriatorioVirtualDbContext dbContext,
    BreedingFarmGalleryUploadSession session)
    : ICommandHandler<UploadBreedingFarmGalleryImageCommand, UploadBreedingFarmGalleryImageResult>
{
    public async Task<UploadBreedingFarmGalleryImageResult> Handle(
        UploadBreedingFarmGalleryImageCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (session.Status != UploadBreedingFarmGalleryImageStatus.Created || session.StoredObject is null)
        {
            return new(session.Status, null, null);
        }

        var access = await BreedingFarmVisualIdentityAccess.FindCurrentOwnerFarmAsync(
            dbContext,
            command.UserId,
            tracking: false,
            cancellationToken);
        if (access.Status != BreedingFarmVisualIdentityAccessStatus.Success)
        {
            return new(ToUploadStatus(access.Status), null, null);
        }

        var farmId = access.Farm!.Id;
        var storedObject = session.StoredObject;
        if (storedObject.BreedingFarmId != farmId)
        {
            return new(UploadBreedingFarmGalleryImageStatus.BreedingFarmNotFound, null, null);
        }

        // Serialize count-and-insert so concurrent uploads cannot exceed the per-farm limit.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM app.breeding_farms WHERE \"Id\" = {farmId} FOR UPDATE",
            cancellationToken);
        var currentCount = await dbContext.BreedingFarmGalleryImages
            .CountAsync(image => image.BreedingFarmId == farmId && image.DeletedAtUtc == null, cancellationToken);
        if (currentCount >= BreedingFarmGalleryUploadLimits.MaxImageCount)
        {
            return new(UploadBreedingFarmGalleryImageStatus.LimitExceeded, farmId, null);
        }

        var image = new BreedingFarmGalleryImage(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            farmId,
            storedObject.ObjectKey,
            command.FileName,
            storedObject.ContentType,
            storedObject.Length,
            session.Width,
            session.Height,
            command.Caption);
        dbContext.BreedingFarmGalleryImages.Add(image);
        return new(
            UploadBreedingFarmGalleryImageStatus.Created,
            farmId,
            GetBreedingFarmGalleryQueryHandler.ToMetadata(image));
    }

    private static UploadBreedingFarmGalleryImageStatus ToUploadStatus(
        BreedingFarmVisualIdentityAccessStatus status) => status switch
        {
            BreedingFarmVisualIdentityAccessStatus.UserNotFound => UploadBreedingFarmGalleryImageStatus.UserNotFound,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotSelected => UploadBreedingFarmGalleryImageStatus.BreedingFarmNotSelected,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotFound => UploadBreedingFarmGalleryImageStatus.BreedingFarmNotFound,
            _ => UploadBreedingFarmGalleryImageStatus.InvalidData
        };
}

public sealed class UploadBreedingFarmGalleryImageStoragePostProcessor(
    IPrivateObjectStorage storage,
    BreedingFarmGalleryUploadSession session)
    : ICommandPostProcessor<UploadBreedingFarmGalleryImageCommand, UploadBreedingFarmGalleryImageResult>
{
    public async Task<UploadBreedingFarmGalleryImageResult> Process(
        UploadBreedingFarmGalleryImageCommand command,
        UploadBreedingFarmGalleryImageResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status != UploadBreedingFarmGalleryImageStatus.Created && session.StoredObject is { } storedObject)
        {
            await BreedingFarmGalleryStorageCleanup.TryDeleteAsync(
                storage,
                storedObject.BreedingFarmId,
                storedObject.ObjectKey,
                cancellationToken);
        }

        return result;
    }
}

public sealed class UpdateBreedingFarmGalleryCaptionCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<UpdateBreedingFarmGalleryCaptionCommand, UpdateBreedingFarmGalleryCaptionResult>
{
    public async Task<UpdateBreedingFarmGalleryCaptionResult> Handle(
        UpdateBreedingFarmGalleryCaptionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        string? caption;
        try
        {
            caption = BreedingFarmGalleryImage.NormalizeCaption(command.Caption);
        }
        catch (ArgumentException)
        {
            return new(UpdateBreedingFarmGalleryCaptionStatus.InvalidData, null);
        }

        var access = await BreedingFarmVisualIdentityAccess.FindCurrentOwnerFarmAsync(
            dbContext,
            command.UserId,
            tracking: true,
            cancellationToken);
        if (access.Status != BreedingFarmVisualIdentityAccessStatus.Success)
        {
            return new(ToCaptionStatus(access.Status), null);
        }

        var image = await dbContext.BreedingFarmGalleryImages.SingleOrDefaultAsync(
            candidate => candidate.Id == command.ImageId &&
                candidate.BreedingFarmId == access.Farm!.Id &&
                candidate.DeletedAtUtc == null,
            cancellationToken);
        if (image is null)
        {
            return new(UpdateBreedingFarmGalleryCaptionStatus.ImageNotFound, null);
        }

        image.UpdateCaption(caption, DateTimeOffset.UtcNow);
        return new(UpdateBreedingFarmGalleryCaptionStatus.Updated, GetBreedingFarmGalleryQueryHandler.ToMetadata(image));
    }

    private static UpdateBreedingFarmGalleryCaptionStatus ToCaptionStatus(
        BreedingFarmVisualIdentityAccessStatus status) => status switch
        {
            BreedingFarmVisualIdentityAccessStatus.UserNotFound => UpdateBreedingFarmGalleryCaptionStatus.UserNotFound,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotSelected => UpdateBreedingFarmGalleryCaptionStatus.BreedingFarmNotSelected,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotFound => UpdateBreedingFarmGalleryCaptionStatus.BreedingFarmNotFound,
            _ => UpdateBreedingFarmGalleryCaptionStatus.InvalidData
        };
}

public sealed class DeleteBreedingFarmGalleryImageCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<DeleteBreedingFarmGalleryImageCommand, DeleteBreedingFarmGalleryImageResult>
{
    public async Task<DeleteBreedingFarmGalleryImageResult> Handle(
        DeleteBreedingFarmGalleryImageCommand command,
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
            return new(ToDeleteStatus(access.Status), null);
        }

        var farmId = access.Farm!.Id;
        var image = await dbContext.BreedingFarmGalleryImages.SingleOrDefaultAsync(
            candidate => candidate.Id == command.ImageId && candidate.BreedingFarmId == farmId,
            cancellationToken);
        if (image is null)
        {
            return new(DeleteBreedingFarmGalleryImageStatus.ImageNotFound, null);
        }

        if (!image.IsDeleted)
        {
            image.MarkDeleted(DateTimeOffset.UtcNow);
        }

        var cleanup = image.StorageCleanupPending
            ? new BreedingFarmGalleryImageCleanup(farmId, image.Id, image.ObjectKey)
            : null;
        return new(DeleteBreedingFarmGalleryImageStatus.Deleted, cleanup);
    }

    private static DeleteBreedingFarmGalleryImageStatus ToDeleteStatus(
        BreedingFarmVisualIdentityAccessStatus status) => status switch
        {
            BreedingFarmVisualIdentityAccessStatus.UserNotFound => DeleteBreedingFarmGalleryImageStatus.UserNotFound,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotSelected => DeleteBreedingFarmGalleryImageStatus.BreedingFarmNotSelected,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotFound => DeleteBreedingFarmGalleryImageStatus.BreedingFarmNotFound,
            _ => DeleteBreedingFarmGalleryImageStatus.ImageNotFound
        };
}

public sealed class DeleteBreedingFarmGalleryImageStoragePostProcessor(
    CriatorioVirtualDbContext dbContext,
    IPrivateObjectStorage storage)
    : ICommandPostProcessor<DeleteBreedingFarmGalleryImageCommand, DeleteBreedingFarmGalleryImageResult>
{
    public async Task<DeleteBreedingFarmGalleryImageResult> Process(
        DeleteBreedingFarmGalleryImageCommand command,
        DeleteBreedingFarmGalleryImageResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status != DeleteBreedingFarmGalleryImageStatus.Deleted || result.Cleanup is null)
        {
            return result;
        }

        var cleanup = result.Cleanup;
        if (!await BreedingFarmGalleryStorageCleanup.TryDeleteAsync(
                storage,
                cleanup.BreedingFarmId,
                cleanup.ObjectKey,
                cancellationToken))
        {
            return new(DeleteBreedingFarmGalleryImageStatus.StorageCleanupPending, cleanup);
        }

        var image = await dbContext.BreedingFarmGalleryImages.SingleOrDefaultAsync(
            candidate => candidate.Id == cleanup.ImageId && candidate.BreedingFarmId == cleanup.BreedingFarmId,
            CancellationToken.None);
        if (image is not null && image.StorageCleanupPending)
        {
            image.MarkStorageCleanupCompleted(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        return new(DeleteBreedingFarmGalleryImageStatus.Deleted, null);
    }
}

public sealed class GetBreedingFarmGalleryImageContentQueryHandler(
    CriatorioVirtualDbContext dbContext,
    IPrivateObjectStorage storage)
    : IQueryHandler<GetBreedingFarmGalleryImageContentQuery, GetBreedingFarmGalleryImageContentResult>
{
    public async Task<GetBreedingFarmGalleryImageContentResult> Handle(
        GetBreedingFarmGalleryImageContentQuery query,
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
            return new(ToContentStatus(access.Status), null);
        }

        var farmId = access.Farm!.Id;
        var image = await dbContext.BreedingFarmGalleryImages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == query.ImageId &&
                    candidate.BreedingFarmId == farmId &&
                    candidate.DeletedAtUtc == null,
                cancellationToken);
        if (image is null)
        {
            return new(GetBreedingFarmGalleryImageContentStatus.ImageNotFound, null);
        }

        try
        {
            var content = await storage.OpenReadAsync(farmId, image.ObjectKey, cancellationToken);
            return new(
                GetBreedingFarmGalleryImageContentStatus.Success,
                new(image.FileName, image.ContentType, image.Length, content));
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

    private static GetBreedingFarmGalleryImageContentStatus ToContentStatus(
        BreedingFarmVisualIdentityAccessStatus status) => status switch
        {
            BreedingFarmVisualIdentityAccessStatus.UserNotFound => GetBreedingFarmGalleryImageContentStatus.UserNotFound,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotSelected => GetBreedingFarmGalleryImageContentStatus.BreedingFarmNotSelected,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotFound => GetBreedingFarmGalleryImageContentStatus.BreedingFarmNotFound,
            _ => GetBreedingFarmGalleryImageContentStatus.ImageNotFound
        };

    private static GetBreedingFarmGalleryImageContentResult StorageUnavailable() =>
        new(GetBreedingFarmGalleryImageContentStatus.StorageUnavailable, null);
}
