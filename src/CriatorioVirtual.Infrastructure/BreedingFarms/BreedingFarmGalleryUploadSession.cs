using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.BreedingFarms;

public sealed class BreedingFarmGalleryUploadSession(IPrivateObjectStorage storage)
    : ICommandFailureCompensator
{
    private bool compensated;

    public UploadBreedingFarmGalleryImageStatus Status { get; private set; } =
        UploadBreedingFarmGalleryImageStatus.InvalidData;

    public PrivateObjectDescriptor? StoredObject { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public void SetStatus(UploadBreedingFarmGalleryImageStatus status) => Status = status;

    public void SetStoredObject(PrivateObjectDescriptor descriptor, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        StoredObject = descriptor;
        Width = width;
        Height = height;
        Status = UploadBreedingFarmGalleryImageStatus.Created;
    }

    public async Task CompensateAsync(CancellationToken cancellationToken)
    {
        if (StoredObject is null || compensated)
        {
            return;
        }

        compensated = true;
        await BreedingFarmGalleryStorageCleanup.TryDeleteAsync(
            storage,
            StoredObject.BreedingFarmId,
            StoredObject.ObjectKey,
            cancellationToken);
    }
}

public sealed class UploadBreedingFarmGalleryImagePreProcessor(
    CriatorioVirtualDbContext dbContext,
    IPrivateObjectStorage storage,
    BreedingFarmGalleryUploadSession session)
    : ICommandPreProcessor<UploadBreedingFarmGalleryImageCommand>
{
    public async Task Process(
        UploadBreedingFarmGalleryImageCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var access = await BreedingFarmVisualIdentityAccess.FindCurrentOwnerFarmAsync(
            dbContext,
            command.UserId,
            tracking: false,
            cancellationToken);
        if (access.Status != BreedingFarmVisualIdentityAccessStatus.Success)
        {
            session.SetStatus(ToUploadStatus(access.Status));
            return;
        }

        if (command.Length <= 0 ||
            command.Length > BreedingFarmGalleryUploadLimits.MaxFileLength ||
            !command.Content.CanRead)
        {
            session.SetStatus(UploadBreedingFarmGalleryImageStatus.InvalidData);
            return;
        }

        try
        {
            _ = BreedingFarmGalleryImage.NormalizeCaption(command.Caption);
        }
        catch (ArgumentException)
        {
            session.SetStatus(UploadBreedingFarmGalleryImageStatus.InvalidData);
            return;
        }

        await using var content = new MemoryStream((int)command.Length);
        if (!await TryCopyContentAsync(command.Content, content, command.Length, cancellationToken) ||
            !content.TryGetBuffer(out var buffer) ||
            !BreedingFarmGalleryImageValidation.TryValidate(
                command.FileName,
                command.ContentType,
                buffer.AsSpan(0, (int)content.Length),
                out var width,
                out var height,
                out _))
        {
            session.SetStatus(UploadBreedingFarmGalleryImageStatus.InvalidData);
            return;
        }

        var farmId = access.Farm!.Id;
        var objectKey = $"gallery/{Guid.NewGuid():N}";
        content.Position = 0;
        try
        {
            var storedObject = await storage.PutAsync(
                new PrivateObjectUpload(farmId, objectKey, command.FileName, command.ContentType, content),
                cancellationToken);
            session.SetStoredObject(storedObject, width, height);
            if (storedObject.BreedingFarmId != farmId ||
                !string.Equals(storedObject.ObjectKey, objectKey, StringComparison.Ordinal) ||
                storedObject.Length != content.Length)
            {
                session.SetStatus(UploadBreedingFarmGalleryImageStatus.StorageUnavailable);
            }
        }
        catch (ArgumentException)
        {
            session.SetStatus(UploadBreedingFarmGalleryImageStatus.InvalidData);
        }
        catch (IOException)
        {
            session.SetStatus(UploadBreedingFarmGalleryImageStatus.StorageUnavailable);
        }
        catch (UnauthorizedAccessException)
        {
            session.SetStatus(UploadBreedingFarmGalleryImageStatus.StorageUnavailable);
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

            if (destination.Length + read > BreedingFarmGalleryUploadLimits.MaxFileLength)
            {
                return false;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
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

internal static class BreedingFarmGalleryStorageCleanup
{
    public static async Task<bool> TryDeleteAsync(
        IPrivateObjectStorage storage,
        Guid breedingFarmId,
        string objectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteAsync(breedingFarmId, objectKey, cancellationToken);
            return true;
        }
        catch (FileNotFoundException)
        {
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
