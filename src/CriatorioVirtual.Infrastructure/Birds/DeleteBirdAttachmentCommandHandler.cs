using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

public sealed class DeleteBirdAttachmentCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<DeleteBirdAttachmentCommand, DeleteBirdAttachmentResult>
{
    public async Task<DeleteBirdAttachmentResult> Handle(
        DeleteBirdAttachmentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.UserId == Guid.Empty ||
            command.BirdId == Guid.Empty ||
            command.AttachmentId == Guid.Empty)
        {
            return DeleteBirdAttachmentResult.InvalidData();
        }

        if (!command.Confirmed)
        {
            return DeleteBirdAttachmentResult.ConfirmationRequired();
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return DeleteBirdAttachmentResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return DeleteBirdAttachmentResult.BreedingFarmNotSelected();
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
            return DeleteBirdAttachmentResult.BreedingFarmNotFound();
        }

        // Deletion and primary-photo selection both serialize on the bird row.
        // This prevents a concurrent selection from racing past the primary-photo guard.
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
            return DeleteBirdAttachmentResult.BirdNotFound();
        }

        var attachment = await dbContext.BirdAttachments
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == command.AttachmentId &&
                    candidate.BreedingFarmId == breedingFarmId &&
                    candidate.BirdId == command.BirdId,
                cancellationToken);
        if (attachment is null)
        {
            return DeleteBirdAttachmentResult.AttachmentNotFound();
        }

        if (bird.PrimaryPhotoId == attachment.Id)
        {
            return DeleteBirdAttachmentResult.PrimaryPhotoMustBeReplaced();
        }

        if (!attachment.IsDeleted)
        {
            attachment.MarkDeleted(DateTimeOffset.UtcNow);
        }

        return DeleteBirdAttachmentResult.Deleted(
            attachment.StorageCleanupPending
                ? new BirdAttachmentCleanup(
                    attachment.BreedingFarmId,
                    attachment.Id,
                    attachment.ObjectKey)
                : null);
    }
}

public sealed class DeleteBirdAttachmentStorageCleanupProcessor(
    CriatorioVirtualDbContext dbContext,
    IPrivateObjectStorage storage)
    : ICommandPostProcessor<DeleteBirdAttachmentCommand, DeleteBirdAttachmentResult>
{
    public async Task<DeleteBirdAttachmentResult> Process(
        DeleteBirdAttachmentCommand command,
        DeleteBirdAttachmentResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(result);

        if (result.Status != DeleteBirdAttachmentStatus.Deleted ||
            result.Cleanup is null)
        {
            return result;
        }

        try
        {
            await storage.DeleteAsync(
                result.Cleanup.BreedingFarmId,
                result.Cleanup.ObjectKey,
                cancellationToken);
        }
        catch (IOException)
        {
            return DeleteBirdAttachmentResult.StorageCleanupPending(result.Cleanup);
        }
        catch (UnauthorizedAccessException)
        {
            return DeleteBirdAttachmentResult.StorageCleanupPending(result.Cleanup);
        }

        var attachment = await dbContext.BirdAttachments
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == result.Cleanup.AttachmentId &&
                    candidate.BreedingFarmId == result.Cleanup.BreedingFarmId,
                CancellationToken.None);
        if (attachment is not null && attachment.StorageCleanupPending)
        {
            attachment.MarkStorageCleanupCompleted(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        return DeleteBirdAttachmentResult.Deleted();
    }
}
