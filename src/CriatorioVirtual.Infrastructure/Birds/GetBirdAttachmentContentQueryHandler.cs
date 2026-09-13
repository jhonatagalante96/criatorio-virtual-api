using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

public sealed class GetBirdAttachmentContentQueryHandler(
    CriatorioVirtualDbContext dbContext,
    IPrivateObjectStorage storage)
    : IQueryHandler<GetBirdAttachmentContentQuery, GetBirdAttachmentContentResult>
{
    public async Task<GetBirdAttachmentContentResult> Handle(
        GetBirdAttachmentContentQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);
        if (user is null)
        {
            return GetBirdAttachmentContentResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return GetBirdAttachmentContentResult.BreedingFarmNotSelected();
        }

        var breedingFarmId = user.SelectedBreedingFarmId.Value;
        var hasActiveMembership = await dbContext.BreedingFarmUsers
            .AsNoTracking()
            .AnyAsync(
                membership =>
                    membership.BreedingFarmId == breedingFarmId &&
                    membership.UserId == query.UserId &&
                    membership.IsActive,
                cancellationToken);
        if (!hasActiveMembership)
        {
            return GetBirdAttachmentContentResult.BreedingFarmNotFound();
        }

        var birdExists = await dbContext.Birds
            .AsNoTracking()
            .AnyAsync(
                bird => bird.Id == query.BirdId && bird.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (!birdExists)
        {
            return GetBirdAttachmentContentResult.BirdNotFound();
        }

        var attachment = await dbContext.BirdAttachments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == query.AttachmentId &&
                    candidate.BreedingFarmId == breedingFarmId &&
                    candidate.BirdId == query.BirdId &&
                    candidate.DeletedAtUtc == null,
                cancellationToken);
        if (attachment is null)
        {
            return GetBirdAttachmentContentResult.AttachmentNotFound();
        }

        try
        {
            var content = await storage.OpenReadAsync(
                breedingFarmId,
                attachment.ObjectKey,
                cancellationToken);
            return GetBirdAttachmentContentResult.Succeeded(
                new BirdAttachmentContent(
                    attachment.Id,
                    attachment.FileName,
                    attachment.ContentType,
                    attachment.Length,
                    content));
        }
        catch (FileNotFoundException)
        {
            return GetBirdAttachmentContentResult.StorageUnavailable();
        }
        catch (DirectoryNotFoundException)
        {
            return GetBirdAttachmentContentResult.StorageUnavailable();
        }
        catch (IOException)
        {
            return GetBirdAttachmentContentResult.StorageUnavailable();
        }
        catch (UnauthorizedAccessException)
        {
            return GetBirdAttachmentContentResult.StorageUnavailable();
        }
    }
}
