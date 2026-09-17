using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace CriatorioVirtual.Infrastructure.Storage;

public sealed record PrivateStorageMigrationObject(
    Guid BreedingFarmId,
    string ObjectKey,
    string ContentType,
    long Length,
    string SourcePath);

public sealed record PrivateStorageMigrationSummary(int Discovered, int Copied, int AlreadyPresent);

public interface IPrivateStorageMigrationTarget
{
    Task<Stream> OpenDestinationReadAsync(
        Guid breedingFarmId,
        string objectKey,
        CancellationToken cancellationToken = default);

    Task PutMigratedObjectAsync(
        Guid breedingFarmId,
        string objectKey,
        string contentType,
        long expectedLength,
        Stream content,
        CancellationToken cancellationToken = default);
}

public sealed class FileSystemPrivateStorageMigrationSource
{
    private const string InProgressSuffix = ".uploading";
    private readonly string rootPath;
    private readonly IReadOnlyDictionary<(Guid BreedingFarmId, string ObjectKey), string> contentTypes;

    public FileSystemPrivateStorageMigrationSource(
        string rootPath,
        IReadOnlyDictionary<(Guid BreedingFarmId, string ObjectKey), string>? contentTypes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (!Path.IsPathRooted(rootPath))
        {
            throw new ArgumentException("The source root path must be absolute.", nameof(rootPath));
        }

        this.rootPath = Path.GetFullPath(rootPath);
        this.contentTypes = contentTypes ?? new Dictionary<(Guid, string), string>();
    }

    public async IAsyncEnumerable<PrivateStorageMigrationObject> EnumerateAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException("The configured legacy private-storage volume is not mounted.");
        }

        if (File.GetAttributes(rootPath).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidDataException("The legacy storage root must not be a linked directory.");
        }

        foreach (var tenantDirectory in Directory.EnumerateDirectories(rootPath).OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tenantName = Path.GetFileName(tenantDirectory);
            if (!Guid.TryParseExact(tenantName, "N", out var breedingFarmId) || breedingFarmId == Guid.Empty)
            {
                throw new InvalidDataException($"The legacy storage contains an invalid tenant directory '{tenantName}'.");
            }

            var tenantAttributes = File.GetAttributes(tenantDirectory);
            if (tenantAttributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidDataException("The legacy storage volume must not contain linked tenant directories.");
            }

            foreach (var sourcePath in EnumerateSourceFiles(tenantDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Path.GetFileName(sourcePath).EndsWith(InProgressSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var objectKey = Path.GetRelativePath(tenantDirectory, sourcePath)
                    .Replace(Path.DirectorySeparatorChar, '/');
                if (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar)
                {
                    objectKey = objectKey.Replace(Path.AltDirectorySeparatorChar, '/');
                }

                PrivateObjectStorageKeyValidation.ValidateObjectKey(objectKey);
                var info = new FileInfo(sourcePath);
                var contentType = contentTypes.TryGetValue((breedingFarmId, objectKey), out var storedContentType)
                    ? storedContentType
                    : "application/octet-stream";

                yield return new PrivateStorageMigrationObject(
                    breedingFarmId,
                    objectKey,
                    contentType,
                    info.Length,
                    sourcePath);
                await Task.Yield();
            }
        }
    }

    private static IEnumerable<string> EnumerateSourceFiles(string tenantDirectory)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(tenantDirectory);

        while (pendingDirectories.TryPop(out var directory))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory)
                         .OrderBy(entry => entry, StringComparer.Ordinal))
            {
                var attributes = File.GetAttributes(path);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidDataException("The legacy storage volume must not contain linked files or directories.");
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    pendingDirectories.Push(path);
                }
                else
                {
                    yield return path;
                }
            }
        }
    }

    public Task<Stream> OpenReadAsync(
        PrivateStorageMigrationObject source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(new FileStream(
            source.SourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan));
    }
}

public sealed class PrivateObjectStorageMigrator(
    FileSystemPrivateStorageMigrationSource source,
    IPrivateStorageMigrationTarget destination)
{
    public async Task<PrivateStorageMigrationSummary> MigrateAsync(
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        var discovered = 0;
        var copied = 0;
        var alreadyPresent = 0;

        await foreach (var file in source.EnumerateAsync(cancellationToken))
        {
            discovered++;
            await using var sourceContent = await source.OpenReadAsync(file, cancellationToken);
            var sourceFingerprint = await FingerprintAsync(sourceContent, cancellationToken);
            if (sourceContent.Length != file.Length)
            {
                throw new InvalidDataException($"The legacy object '{file.ObjectKey}' changed during migration.");
            }

            var existingMatches = await DestinationMatchesAsync(file, sourceContent, sourceFingerprint, cancellationToken);
            if (!existingMatches)
            {
                sourceContent.Position = 0;
                await destination.PutMigratedObjectAsync(
                    file.BreedingFarmId,
                    file.ObjectKey,
                    file.ContentType,
                    file.Length,
                    sourceContent,
                    cancellationToken);

                if (!await DestinationMatchesAsync(file, sourceContent, sourceFingerprint, cancellationToken))
                {
                    throw new InvalidDataException($"The migrated object '{file.ObjectKey}' failed integrity validation.");
                }

                copied++;
            }
            else
            {
                alreadyPresent++;
            }
        }

        return new PrivateStorageMigrationSummary(discovered, copied, alreadyPresent);
    }

    private async Task<bool> DestinationMatchesAsync(
        PrivateStorageMigrationObject source,
        Stream sourceContent,
        StreamFingerprint sourceFingerprint,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var destinationContent = await destination.OpenDestinationReadAsync(
                source.BreedingFarmId,
                source.ObjectKey,
                cancellationToken);
            var destinationFingerprint = await FingerprintAsync(destinationContent, cancellationToken);
            sourceContent.Position = 0;
            return destinationFingerprint.Length == source.Length &&
                CryptographicOperations.FixedTimeEquals(destinationFingerprint.Hash, sourceFingerprint.Hash);
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    private static async Task<StreamFingerprint> FingerprintAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long length = 0;
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, bytesRead);
            length += bytesRead;
        }

        return new StreamFingerprint(hash.GetHashAndReset(), length);
    }

    private sealed record StreamFingerprint(byte[] Hash, long Length);
}
