using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

public sealed class SetBirdPrimaryPhotoCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<SetBirdPrimaryPhotoCommand, SetBirdPrimaryPhotoResult>
{
    public async Task<SetBirdPrimaryPhotoResult> Handle(
        SetBirdPrimaryPhotoCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.UserId == Guid.Empty ||
            command.BirdId == Guid.Empty ||
            command.AttachmentId == Guid.Empty)
        {
            return SetBirdPrimaryPhotoResult.InvalidData();
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return SetBirdPrimaryPhotoResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return SetBirdPrimaryPhotoResult.BreedingFarmNotSelected();
        }

        var breedingFarmId = user.SelectedBreedingFarmId.Value;
        var hasActiveOwnerMembership = await dbContext.BreedingFarmUsers
            .AsNoTracking()
            .AnyAsync(
                membership =>
                    membership.BreedingFarmId == breedingFarmId &&
                    membership.UserId == command.UserId &&
                    membership.IsActive &&
                    membership.Role == BreedingFarmRole.Owner,
                cancellationToken);
        if (!hasActiveOwnerMembership)
        {
            return SetBirdPrimaryPhotoResult.BreedingFarmNotFound();
        }

        // Selection and transfer both serialize on the bird row. This keeps the
        // transfer-pending guard and the primary-photo update atomic.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM app.birds WHERE \"Id\" = {command.BirdId} AND \"BreedingFarmId\" = {breedingFarmId} FOR UPDATE",
            cancellationToken);

        var bird = await dbContext.Birds
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == command.BirdId &&
                    candidate.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (bird is null)
        {
            return SetBirdPrimaryPhotoResult.BirdNotFound();
        }

        if (bird.Status == BirdStatus.Transferred)
        {
            return SetBirdPrimaryPhotoResult.TransferPending();
        }

        if (command.AttachmentId is { } attachmentId)
        {
            var attachment = await dbContext.BirdAttachments
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate =>
                        candidate.Id == attachmentId &&
                        candidate.BreedingFarmId == breedingFarmId &&
                        candidate.BirdId == bird.Id &&
                        candidate.DeletedAtUtc == null,
                    cancellationToken);
            if (attachment is null)
            {
                return SetBirdPrimaryPhotoResult.AttachmentNotFound();
            }

            if (!PrivateObjectStorageFileValidation.IsSupportedImageContentType(attachment.ContentType))
            {
                return SetBirdPrimaryPhotoResult.AttachmentNotImage();
            }
        }

        bird.SetPrimaryPhoto(command.AttachmentId, DateTimeOffset.UtcNow);
        return SetBirdPrimaryPhotoResult.Updated(bird.Id, bird.PrimaryPhotoId);
    }
}
