using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Infrastructure.Persistence;

namespace CriatorioVirtual.Infrastructure.Birds;

public sealed class UploadBirdAttachmentCommandHandler(
    CriatorioVirtualDbContext dbContext,
    BirdAttachmentUploadSession session)
    : ICommandHandler<UploadBirdAttachmentCommand, UploadBirdAttachmentResult>
{
    public Task<UploadBirdAttachmentResult> Handle(
        UploadBirdAttachmentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (session.Status != UploadBirdAttachmentStatus.Created || session.StoredObject is null)
        {
            return Task.FromResult(session.Status switch
            {
                UploadBirdAttachmentStatus.UserNotFound => UploadBirdAttachmentResult.UserNotFound(),
                UploadBirdAttachmentStatus.BreedingFarmNotSelected => UploadBirdAttachmentResult.BreedingFarmNotSelected(),
                UploadBirdAttachmentStatus.BreedingFarmNotFound => UploadBirdAttachmentResult.BreedingFarmNotFound(),
                UploadBirdAttachmentStatus.BirdNotFound => UploadBirdAttachmentResult.BirdNotFound(),
                UploadBirdAttachmentStatus.StorageUnavailable => UploadBirdAttachmentResult.StorageUnavailable(),
                _ => UploadBirdAttachmentResult.InvalidData()
            });
        }

        var storedObject = session.StoredObject;
        var now = DateTimeOffset.UtcNow;
        var attachment = new BirdAttachment(
            Guid.NewGuid(),
            now,
            storedObject.BreedingFarmId,
            command.BirdId,
            storedObject.ObjectKey,
            command.FileName,
            storedObject.ContentType,
            storedObject.Length);
        dbContext.BirdAttachments.Add(attachment);

        return Task.FromResult(UploadBirdAttachmentResult.Created(ToResult(attachment)));
    }

    private static BirdAttachmentResult ToResult(BirdAttachment attachment) =>
        new(
            attachment.Id,
            attachment.BirdId,
            attachment.FileName,
            attachment.ContentType,
            attachment.Length,
            attachment.CreatedAtUtc,
            false);
}
