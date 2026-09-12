namespace CriatorioVirtual.Application.Storage;

public sealed record PrivateObjectUpload(
    Guid BreedingFarmId,
    string ObjectKey,
    string FileName,
    string ContentType,
    Stream Content);

public sealed record PrivateObjectDescriptor(
    Guid BreedingFarmId,
    string ObjectKey,
    string ContentType,
    long Length);

public interface IPrivateObjectStorage
{
    Task<PrivateObjectDescriptor> PutAsync(
        PrivateObjectUpload upload,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(
        Guid breedingFarmId,
        string objectKey,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid breedingFarmId,
        string objectKey,
        CancellationToken cancellationToken = default);
}
