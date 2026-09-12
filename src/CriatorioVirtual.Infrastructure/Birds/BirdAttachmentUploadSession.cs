using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

public sealed class BirdAttachmentUploadSession(IPrivateObjectStorage storage)
    : ICommandFailureCompensator
{
    private bool compensated;

    public UploadBirdAttachmentStatus Status { get; private set; } = UploadBirdAttachmentStatus.InvalidData;

    public PrivateObjectDescriptor? StoredObject { get; private set; }

    public void SetStatus(UploadBirdAttachmentStatus status) => Status = status;

    public void SetStoredObject(PrivateObjectDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        StoredObject = descriptor;
        Status = UploadBirdAttachmentStatus.Created;
    }

    public async Task CompensateAsync(CancellationToken cancellationToken)
    {
        if (StoredObject is null || compensated)
        {
            return;
        }

        compensated = true;
        try
        {
            await storage.DeleteAsync(
                StoredObject.BreedingFarmId,
                StoredObject.ObjectKey,
                cancellationToken);
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed class UploadBirdAttachmentPreProcessor(
    CriatorioVirtualDbContext dbContext,
    IPrivateObjectStorage storage,
    BirdAttachmentUploadSession session)
    : ICommandPreProcessor<UploadBirdAttachmentCommand>
{
    public async Task Process(
        UploadBirdAttachmentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            session.SetStatus(UploadBirdAttachmentStatus.UserNotFound);
            return;
        }

        if (user.SelectedBreedingFarmId is null)
        {
            session.SetStatus(UploadBirdAttachmentStatus.BreedingFarmNotSelected);
            return;
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
            session.SetStatus(UploadBirdAttachmentStatus.BreedingFarmNotFound);
            return;
        }

        var birdExists = await dbContext.Birds
            .AsNoTracking()
            .AnyAsync(
                bird => bird.Id == command.BirdId && bird.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (!birdExists)
        {
            session.SetStatus(UploadBirdAttachmentStatus.BirdNotFound);
            return;
        }

        if (command.Length <= 0 ||
            command.Length > BirdAttachmentUploadLimits.MaxFileLength ||
            !PrivateObjectStorageFileValidation.TryValidateMetadata(
                command.FileName,
                command.ContentType,
                out _) ||
            !command.Content.CanRead)
        {
            session.SetStatus(UploadBirdAttachmentStatus.InvalidData);
            return;
        }

        var objectKey = $"birds/{command.BirdId:N}/attachments/{Guid.NewGuid():N}";
        try
        {
            var descriptor = await storage.PutAsync(
                new PrivateObjectUpload(
                    breedingFarmId,
                    objectKey,
                    command.FileName,
                    command.ContentType,
                    command.Content),
                cancellationToken);
            session.SetStoredObject(descriptor);
        }
        catch (ArgumentException)
        {
            session.SetStatus(UploadBirdAttachmentStatus.InvalidData);
        }
        catch (IOException)
        {
            session.SetStatus(UploadBirdAttachmentStatus.StorageUnavailable);
        }
        catch (UnauthorizedAccessException)
        {
            session.SetStatus(UploadBirdAttachmentStatus.StorageUnavailable);
        }
    }
}
