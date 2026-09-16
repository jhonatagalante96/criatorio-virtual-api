using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;

namespace CriatorioVirtual.Infrastructure.BreedingFarms;

public sealed class BreedingFarmVisualIdentityUploadSession(IPrivateObjectStorage storage)
    : ICommandFailureCompensator
{
    private bool compensated;

    public UploadBreedingFarmVisualIdentityStatus Status { get; private set; } =
        UploadBreedingFarmVisualIdentityStatus.InvalidData;

    public PrivateObjectDescriptor? StoredObject { get; private set; }

    public void SetStatus(UploadBreedingFarmVisualIdentityStatus status) => Status = status;

    public void SetStoredObject(PrivateObjectDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        StoredObject = descriptor;
        Status = UploadBreedingFarmVisualIdentityStatus.Updated;
    }

    public async Task CompensateAsync(CancellationToken cancellationToken)
    {
        if (StoredObject is null || compensated)
        {
            return;
        }

        compensated = true;
        await TryDeleteAsync(storage, StoredObject.BreedingFarmId, StoredObject.ObjectKey, cancellationToken);
    }

    internal static async Task TryDeleteAsync(
        IPrivateObjectStorage storage,
        Guid breedingFarmId,
        string objectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteAsync(breedingFarmId, objectKey, cancellationToken);
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

public sealed class UploadBreedingFarmVisualIdentityPreProcessor(
    CriatorioVirtual.Infrastructure.Persistence.CriatorioVirtualDbContext dbContext,
    IPrivateObjectStorage storage,
    BreedingFarmVisualIdentityUploadSession session)
    : ICommandPreProcessor<UploadBreedingFarmVisualIdentityCommand>
{
    public async Task Process(
        UploadBreedingFarmVisualIdentityCommand command,
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
            command.Length > BreedingFarmVisualIdentityUploadLimits.MaxFileLength ||
            !command.Content.CanRead)
        {
            session.SetStatus(UploadBreedingFarmVisualIdentityStatus.InvalidData);
            return;
        }

        await using var content = new MemoryStream((int)command.Length);
        if (!await TryCopyContentAsync(command.Content, content, command.Length, cancellationToken))
        {
            session.SetStatus(UploadBreedingFarmVisualIdentityStatus.InvalidData);
            return;
        }

        if (!content.TryGetBuffer(out var buffer) ||
            !BreedingFarmVisualIdentityImageValidation.TryValidate(
                command.FileName,
                command.ContentType,
                buffer.AsSpan(0, (int)content.Length),
                out _))
        {
            session.SetStatus(UploadBreedingFarmVisualIdentityStatus.InvalidData);
            return;
        }

        var farm = access.Farm!;
        var objectKey = $"visual-identity/{Guid.NewGuid():N}";
        content.Position = 0;
        try
        {
            var storedObject = await storage.PutAsync(
                new PrivateObjectUpload(
                    farm.Id,
                    objectKey,
                    command.FileName,
                    command.ContentType,
                    content),
                cancellationToken);
            session.SetStoredObject(storedObject);
            if (storedObject.BreedingFarmId != farm.Id ||
                !string.Equals(storedObject.ObjectKey, objectKey, StringComparison.Ordinal) ||
                storedObject.Length != content.Length)
            {
                session.SetStatus(UploadBreedingFarmVisualIdentityStatus.StorageUnavailable);
            }
        }
        catch (ArgumentException)
        {
            session.SetStatus(UploadBreedingFarmVisualIdentityStatus.InvalidData);
        }
        catch (IOException)
        {
            session.SetStatus(UploadBreedingFarmVisualIdentityStatus.StorageUnavailable);
        }
        catch (UnauthorizedAccessException)
        {
            session.SetStatus(UploadBreedingFarmVisualIdentityStatus.StorageUnavailable);
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

            if (destination.Length + read > BreedingFarmVisualIdentityUploadLimits.MaxFileLength)
            {
                return false;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static UploadBreedingFarmVisualIdentityStatus ToUploadStatus(
        BreedingFarmVisualIdentityAccessStatus status) => status switch
        {
            BreedingFarmVisualIdentityAccessStatus.UserNotFound =>
                UploadBreedingFarmVisualIdentityStatus.UserNotFound,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotSelected =>
                UploadBreedingFarmVisualIdentityStatus.BreedingFarmNotSelected,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotFound =>
                UploadBreedingFarmVisualIdentityStatus.BreedingFarmNotFound,
            _ => UploadBreedingFarmVisualIdentityStatus.InvalidData
        };
}
