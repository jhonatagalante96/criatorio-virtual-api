using CriatorioVirtual.Application.Identity;
using CriatorioVirtual.Application.Storage;

namespace CriatorioVirtual.Infrastructure.Identity;

public sealed class PrivateObjectStorageUserAvatarAdapter(IPrivateObjectStorage storage) : IUserAvatarStorage
{
    public async Task<UserAvatarStoredObject> PutAsync(
        Guid userId,
        string objectKey,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var stored = await storage.PutAsync(
            new PrivateObjectUpload(userId, objectKey, fileName, contentType, content),
            cancellationToken);
        if (stored.BreedingFarmId != userId)
        {
            throw new InvalidOperationException("Private storage returned an object in a different owner namespace.");
        }

        return new UserAvatarStoredObject(stored.ObjectKey, stored.ContentType, stored.Length);
    }

    public Task<Stream> OpenReadAsync(
        Guid userId,
        string objectKey,
        CancellationToken cancellationToken = default) =>
        storage.OpenReadAsync(userId, objectKey, cancellationToken);

    public Task DeleteAsync(
        Guid userId,
        string objectKey,
        CancellationToken cancellationToken = default) =>
        storage.DeleteAsync(userId, objectKey, cancellationToken);
}
