using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.Transfers;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

public sealed class UploadBirdAttachmentCommandHandler(
    CriatorioVirtualDbContext dbContext,
    BirdAttachmentUploadSession session,
    IBirdLockCoordinator birdLockCoordinator)
    : ICommandHandler<UploadBirdAttachmentCommand, UploadBirdAttachmentResult>
{
    public async Task<UploadBirdAttachmentResult> Handle(
        UploadBirdAttachmentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (session.Status != UploadBirdAttachmentStatus.Created || session.StoredObject is null)
        {
            return session.Status switch
            {
                UploadBirdAttachmentStatus.UserNotFound => UploadBirdAttachmentResult.UserNotFound(),
                UploadBirdAttachmentStatus.BreedingFarmNotSelected => UploadBirdAttachmentResult.BreedingFarmNotSelected(),
                UploadBirdAttachmentStatus.BreedingFarmNotFound => UploadBirdAttachmentResult.BreedingFarmNotFound(),
                UploadBirdAttachmentStatus.BirdNotFound => UploadBirdAttachmentResult.BirdNotFound(),
                UploadBirdAttachmentStatus.BirdTransferPending => new(UploadBirdAttachmentStatus.BirdTransferPending, null),
                UploadBirdAttachmentStatus.StorageUnavailable => UploadBirdAttachmentResult.StorageUnavailable(),
                _ => UploadBirdAttachmentResult.InvalidData()
            };
        }

        var storedObject = session.StoredObject;
        Bird? associatedBird = null;
        if (command.BirdId is { } birdId && PrivateObjectStorageFileValidation.IsSupportedMediaContentType(command.ContentType))
        {
            await birdLockCoordinator.AcquireLockAsync(birdId, storedObject.BreedingFarmId, cancellationToken);

            associatedBird = await dbContext.Birds.SingleOrDefaultAsync(
                candidate => candidate.Id == birdId && candidate.BreedingFarmId == storedObject.BreedingFarmId,
                cancellationToken);
            if (associatedBird is null)
            {
                await session.CompensateAsync(cancellationToken);
                return UploadBirdAttachmentResult.BirdNotFound();
            }

            if (associatedBird.Status == BirdStatus.Transferred ||
                await dbContext.InternalTransferRequests
                    .AsNoTracking()
                    .AnyAsync(
                        transferRequest =>
                            transferRequest.BirdId == associatedBird.Id &&
                            transferRequest.Status == InternalTransferRequestStatus.Pending,
                        cancellationToken))
            {
                await session.CompensateAsync(cancellationToken);
                return new(UploadBirdAttachmentStatus.BirdTransferPending, null);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var attachment = new BirdAttachment(
            Guid.NewGuid(),
            now,
            storedObject.BreedingFarmId,
            command.BirdId,
            storedObject.ObjectKey,
            command.FileName,
            storedObject.ContentType,
            storedObject.Length,
            command.Caption);
        dbContext.BirdAttachments.Add(attachment);

        return UploadBirdAttachmentResult.Created(ToResult(attachment, associatedBird));
    }

    private static BirdAttachmentResult ToResult(BirdAttachment attachment, Bird? bird) =>
        new(
            attachment.Id,
            attachment.BirdId,
            attachment.FileName,
            attachment.ContentType,
            attachment.Length,
            attachment.CreatedAtUtc,
            false,
            attachment.Caption,
            bird?.Name,
            bird?.RingNumber);
}
