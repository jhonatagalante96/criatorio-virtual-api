using CriatorioVirtual.Application.Storage;
using Microsoft.Extensions.Options;

namespace CriatorioVirtual.Infrastructure.Storage;

public sealed class FileSystemPrivateObjectStorage : IPrivateObjectStorage
{
    private const int BufferSize = 64 * 1024;

    private readonly string rootPath;

    public FileSystemPrivateObjectStorage(IOptions<PrivateStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.PrivateRootPath);
        if (!Path.IsPathRooted(options.Value.PrivateRootPath))
        {
            throw new ArgumentException(
                "The private storage root path must be absolute.",
                nameof(options));
        }

        rootPath = Path.GetFullPath(options.Value.PrivateRootPath);
    }

    public async Task<PrivateObjectDescriptor> PutAsync(
        PrivateObjectUpload upload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        PrivateObjectStorageKeyValidation.ValidateTenant(upload.BreedingFarmId);
        PrivateObjectStorageKeyValidation.ValidateObjectKey(upload.ObjectKey);
        ValidateFileMetadata(upload.FileName, upload.ContentType);
        ArgumentNullException.ThrowIfNull(upload.Content);
        if (!upload.Content.CanRead)
        {
            throw new ArgumentException("The upload stream must be readable.", nameof(upload));
        }

        var destinationPath = ResolvePath(upload.BreedingFarmId, upload.ObjectKey);
        var directoryPath = Path.GetDirectoryName(destinationPath)!;
        Directory.CreateDirectory(directoryPath);
        var temporaryPath = Path.Combine(directoryPath, $".{Guid.NewGuid():N}.uploading");

        try
        {
            await using (var target = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await upload.Content.CopyToAsync(target, BufferSize, cancellationToken);
                await target.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, destinationPath);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }

        return new PrivateObjectDescriptor(
            upload.BreedingFarmId,
            upload.ObjectKey,
            upload.ContentType.Trim().ToLowerInvariant(),
            new FileInfo(destinationPath).Length);
    }

    public Task<Stream> OpenReadAsync(
        Guid breedingFarmId,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(breedingFarmId, objectKey);
        try
        {
            Stream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult(stream);
        }
        catch (FileNotFoundException)
        {
            throw new FileNotFoundException("The private object was not found.");
        }
        catch (DirectoryNotFoundException)
        {
            throw new FileNotFoundException("The private object was not found.");
        }
    }

    public Task DeleteAsync(
        Guid breedingFarmId,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(ResolvePath(breedingFarmId, objectKey));
        return Task.CompletedTask;
    }

    public Task MoveAsync(
        Guid sourceBreedingFarmId,
        Guid destinationBreedingFarmId,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sourceBreedingFarmId == destinationBreedingFarmId)
        {
            PrivateObjectStorageKeyValidation.ValidateTenant(sourceBreedingFarmId);
            PrivateObjectStorageKeyValidation.ValidateObjectKey(objectKey);
            return Task.CompletedTask;
        }

        var sourcePath = ResolvePath(sourceBreedingFarmId, objectKey);
        var destinationPath = ResolvePath(destinationBreedingFarmId, objectKey);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The private object was not found.");
        }

        var destinationDirectory = Path.GetDirectoryName(destinationPath)!;
        Directory.CreateDirectory(destinationDirectory);
        File.Move(sourcePath, destinationPath, overwrite: true);
        return Task.CompletedTask;
    }

    private string ResolvePath(Guid breedingFarmId, string objectKey)
    {
        PrivateObjectStorageKeyValidation.ValidateTenant(breedingFarmId);
        PrivateObjectStorageKeyValidation.ValidateObjectKey(objectKey);

        var tenantRoot = Path.Combine(rootPath, breedingFarmId.ToString("N"));
        var candidate = Path.GetFullPath(Path.Combine(
            tenantRoot,
            objectKey.Replace('/', Path.DirectorySeparatorChar)));
        var tenantRootWithSeparator = Path.TrimEndingDirectorySeparator(tenantRoot) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.StartsWith(tenantRootWithSeparator, comparison))
        {
            throw new ArgumentException("The object key must remain within its tenant namespace.", nameof(objectKey));
        }

        return candidate;
    }

    private static void ValidateFileMetadata(string fileName, string contentType)
    {
        if (!PrivateObjectStorageFileValidation.TryValidateMetadata(fileName, contentType, out var error))
        {
            throw new ArgumentException(error, nameof(fileName));
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
