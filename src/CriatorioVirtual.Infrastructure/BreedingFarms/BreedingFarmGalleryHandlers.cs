using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.Transfers;
using CriatorioVirtual.Infrastructure.Birds;
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
        if (query.Page <= 0 || query.PageSize <= 0 || query.PageSize > BreedingFarmGalleryLimits.MaxPageSize ||
            (long)(query.Page - 1) * query.PageSize > int.MaxValue)
        {
            return new(GetBreedingFarmGalleryStatus.InvalidData, null, [], query.Page, query.PageSize, 0);
        }

        var access = await BreedingFarmVisualIdentityAccess.FindCurrentOwnerFarmAsync(
            dbContext,
            query.UserId,
            tracking: false,
            cancellationToken);
        if (access.Status != BreedingFarmVisualIdentityAccessStatus.Success)
        {
            return new(ToGalleryStatus(access.Status), null, [], query.Page, query.PageSize, 0);
        }

        var farmId = access.Farm!.Id;
        if (query.BirdId is { } birdId &&
            !await dbContext.Birds.AsNoTracking().AnyAsync(
                bird => bird.Id == birdId && bird.BreedingFarmId == farmId,
                cancellationToken))
        {
            return new(GetBreedingFarmGalleryStatus.BirdNotFound, farmId, [], query.Page, query.PageSize, 0);
        }

        var mediaQuery = MediaQuery(farmId);
        if (query.BirdId is { } selectedBirdId)
        {
            mediaQuery = mediaQuery.Where(media => media.BirdId == selectedBirdId);
        }

        if (query.Type == BreedingFarmGalleryMediaType.Image)
        {
            mediaQuery = mediaQuery.Where(media => media.ContentType.StartsWith("image/"));
        }
        else if (query.Type == BreedingFarmGalleryMediaType.Video)
        {
            mediaQuery = mediaQuery.Where(media => media.ContentType.StartsWith("video/"));
        }

        var totalCount = await mediaQuery.CountAsync(cancellationToken);
        var items = await (
            from media in mediaQuery
            join bird in dbContext.Birds.AsNoTracking()
                on new { media.BreedingFarmId, media.BirdId }
                equals new { bird.BreedingFarmId, BirdId = (Guid?)bird.Id }
                into linkedBirds
            from bird in linkedBirds.DefaultIfEmpty()
            orderby media.CreatedAtUtc descending, media.Id descending
            select new BreedingFarmGalleryMediaResult(
                media.Id,
                media.BirdId,
                bird == null ? null : bird.Name,
                bird == null ? null : bird.RingNumber,
                media.FileName,
                media.ContentType,
                media.Length,
                media.Caption,
                media.CreatedAtUtc,
                media.UpdatedAtUtc,
                bird != null && bird.PrimaryPhotoId == media.Id))
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArrayAsync(cancellationToken);

        return new(GetBreedingFarmGalleryStatus.Success, farmId, items, query.Page, query.PageSize, totalCount);
    }

    internal static IQueryable<BirdAttachment> MediaQuery(CriatorioVirtualDbContext dbContext, Guid farmId) =>
        dbContext.BirdAttachments
            .AsNoTracking()
            .Where(media => media.BreedingFarmId == farmId &&
                media.DeletedAtUtc == null &&
                (media.ContentType.StartsWith("image/") || media.ContentType.StartsWith("video/")));

    private IQueryable<BirdAttachment> MediaQuery(Guid farmId) => MediaQuery(dbContext, farmId);

    private static GetBreedingFarmGalleryStatus ToGalleryStatus(BreedingFarmVisualIdentityAccessStatus status) =>
        status switch
        {
            BreedingFarmVisualIdentityAccessStatus.UserNotFound => GetBreedingFarmGalleryStatus.UserNotFound,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotSelected => GetBreedingFarmGalleryStatus.BreedingFarmNotSelected,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotFound => GetBreedingFarmGalleryStatus.BreedingFarmNotFound,
            _ => GetBreedingFarmGalleryStatus.InvalidData
        };
}

public sealed class GetBreedingFarmGalleryMediaContentQueryHandler(
    CriatorioVirtualDbContext dbContext,
    IPrivateObjectStorage storage)
    : IQueryHandler<GetBreedingFarmGalleryMediaContentQuery, GetBreedingFarmGalleryMediaContentResult>
{
    public async Task<GetBreedingFarmGalleryMediaContentResult> Handle(
        GetBreedingFarmGalleryMediaContentQuery query,
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
        var media = await GetBreedingFarmGalleryQueryHandler.MediaQuery(dbContext, farmId)
            .SingleOrDefaultAsync(candidate => candidate.Id == query.MediaId, cancellationToken);
        if (media is null)
        {
            return new(GetBreedingFarmGalleryMediaContentStatus.MediaNotFound, null);
        }

        try
        {
            var content = await storage.OpenReadAsync(farmId, media.ObjectKey, cancellationToken);
            return new(
                GetBreedingFarmGalleryMediaContentStatus.Success,
                new(media.FileName, media.ContentType, media.Length, content));
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

    private static GetBreedingFarmGalleryMediaContentStatus ToContentStatus(
        BreedingFarmVisualIdentityAccessStatus status) => status switch
        {
            BreedingFarmVisualIdentityAccessStatus.UserNotFound => GetBreedingFarmGalleryMediaContentStatus.UserNotFound,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotSelected => GetBreedingFarmGalleryMediaContentStatus.BreedingFarmNotSelected,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotFound => GetBreedingFarmGalleryMediaContentStatus.BreedingFarmNotFound,
            _ => GetBreedingFarmGalleryMediaContentStatus.MediaNotFound
        };

    private static GetBreedingFarmGalleryMediaContentResult StorageUnavailable() =>
        new(GetBreedingFarmGalleryMediaContentStatus.StorageUnavailable, null);
}

public sealed class UpdateBreedingFarmGalleryCaptionCommandHandler(
    CriatorioVirtualDbContext dbContext,
    IBirdLockCoordinator birdLockCoordinator)
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
            caption = BirdAttachment.NormalizeCaption(command.Caption);
        }
        catch (ArgumentException)
        {
            return new(UpdateBreedingFarmGalleryCaptionStatus.InvalidData, null);
        }

        var access = await BreedingFarmVisualIdentityAccess.FindCurrentOwnerFarmAsync(
            dbContext,
            command.UserId,
            tracking: false,
            cancellationToken);
        if (access.Status != BreedingFarmVisualIdentityAccessStatus.Success)
        {
            return new(ToCaptionStatus(access.Status), null);
        }

        var farmId = access.Farm!.Id;
        var media = await dbContext.BirdAttachments.SingleOrDefaultAsync(
            candidate => candidate.Id == command.MediaId &&
                candidate.BreedingFarmId == farmId &&
                candidate.DeletedAtUtc == null &&
                (candidate.ContentType.StartsWith("image/") || candidate.ContentType.StartsWith("video/")),
            cancellationToken);
        if (media is null)
        {
            return new(UpdateBreedingFarmGalleryCaptionStatus.MediaNotFound, null);
        }

        Bird? bird = null;
        if (media.BirdId is { } birdId)
        {
            await birdLockCoordinator.AcquireLockAsync(birdId, farmId, cancellationToken);
            bird = await dbContext.Birds.SingleOrDefaultAsync(
                candidate => candidate.Id == birdId && candidate.BreedingFarmId == farmId,
                cancellationToken);
            if (bird is null)
            {
                return new(UpdateBreedingFarmGalleryCaptionStatus.MediaNotFound, null);
            }

            if (bird.Status == BirdStatus.Transferred ||
                await dbContext.InternalTransferRequests
                    .AsNoTracking()
                    .AnyAsync(
                        transferRequest =>
                            transferRequest.BirdId == bird.Id &&
                            transferRequest.Status == InternalTransferRequestStatus.Pending,
                        cancellationToken))
            {
                return new(UpdateBreedingFarmGalleryCaptionStatus.BirdTransferPending, null);
            }
        }

        media.UpdateCaption(caption, DateTimeOffset.UtcNow);
        return new(UpdateBreedingFarmGalleryCaptionStatus.Updated, ToResult(media, bird));
    }

    internal static BreedingFarmGalleryMediaResult ToResult(BirdAttachment media, Bird? bird) =>
        new(
            media.Id,
            media.BirdId,
            bird?.Name,
            bird?.RingNumber,
            media.FileName,
            media.ContentType,
            media.Length,
            media.Caption,
            media.CreatedAtUtc,
            media.UpdatedAtUtc,
            bird?.PrimaryPhotoId == media.Id);

    private static UpdateBreedingFarmGalleryCaptionStatus ToCaptionStatus(
        BreedingFarmVisualIdentityAccessStatus status) => status switch
        {
            BreedingFarmVisualIdentityAccessStatus.UserNotFound => UpdateBreedingFarmGalleryCaptionStatus.UserNotFound,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotSelected => UpdateBreedingFarmGalleryCaptionStatus.BreedingFarmNotSelected,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotFound => UpdateBreedingFarmGalleryCaptionStatus.BreedingFarmNotFound,
            _ => UpdateBreedingFarmGalleryCaptionStatus.InvalidData
        };
}
