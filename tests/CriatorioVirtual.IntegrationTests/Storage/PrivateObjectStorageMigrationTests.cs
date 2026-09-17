using System.Collections.Concurrent;
using System.Text;
using CriatorioVirtual.Infrastructure.Storage;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Storage;

public sealed class PrivateObjectStorageMigrationTests
{
    [Fact]
    public async Task MigrationCopiesObjectsByTenantAndIsSafeToRerun()
    {
        await using var volume = new TemporaryVolume();
        var firstTenant = Guid.NewGuid();
        var secondTenant = Guid.NewGuid();
        const string sharedKey = "birds/photo-001";
        await volume.WriteAsync(firstTenant, sharedKey, "tenant one");
        await volume.WriteAsync(secondTenant, sharedKey, "tenant two");
        await volume.WriteAsync(firstTenant, "birds/.interrupted.uploading", "partial");
        var metadata = new Dictionary<(Guid, string), string>
        {
            [(firstTenant, sharedKey)] = "image/jpeg"
        };
        var source = new FileSystemPrivateStorageMigrationSource(volume.RootPath, metadata);
        var destination = new InMemoryMigrationTarget();
        var migrator = new PrivateObjectStorageMigrator(source, destination);

        var firstRun = await migrator.MigrateAsync();
        var secondRun = await migrator.MigrateAsync();

        Assert.Equal(new PrivateStorageMigrationSummary(2, 2, 0), firstRun);
        Assert.Equal(new PrivateStorageMigrationSummary(2, 0, 2), secondRun);
        Assert.Equal("tenant one", await destination.ReadTextAsync(firstTenant, sharedKey));
        Assert.Equal("tenant two", await destination.ReadTextAsync(secondTenant, sharedKey));
        Assert.Equal("image/jpeg", destination.ContentTypes[(firstTenant, sharedKey)]);
        Assert.True(File.Exists(volume.GetPath(firstTenant, sharedKey)));
        Assert.True(File.Exists(volume.GetPath(secondTenant, sharedKey)));
    }

    [Fact]
    public async Task MigrationRepairsAnExistingPartialDestinationObject()
    {
        await using var volume = new TemporaryVolume();
        var farmId = Guid.NewGuid();
        const string objectKey = "documents/document-001.pdf";
        await volume.WriteAsync(farmId, objectKey, "complete PDF contents");
        var source = new FileSystemPrivateStorageMigrationSource(volume.RootPath);
        var destination = new InMemoryMigrationTarget();
        destination.Put(farmId, objectKey, "partial");

        var summary = await new PrivateObjectStorageMigrator(source, destination).MigrateAsync();

        Assert.Equal(new PrivateStorageMigrationSummary(1, 1, 0), summary);
        Assert.Equal("complete PDF contents", await destination.ReadTextAsync(farmId, objectKey));
        Assert.True(File.Exists(volume.GetPath(farmId, objectKey)));
    }

    [Fact]
    public async Task FailedCopyLeavesTheVolumeOriginalAvailableForRetry()
    {
        await using var volume = new TemporaryVolume();
        var farmId = Guid.NewGuid();
        const string objectKey = "birds/photo-002";
        var sourcePath = await volume.WriteAsync(farmId, objectKey, "original content");
        var destination = new InMemoryMigrationTarget { FailWrites = true };
        var migrator = new PrivateObjectStorageMigrator(
            new FileSystemPrivateStorageMigrationSource(volume.RootPath),
            destination);

        await Assert.ThrowsAsync<IOException>(() => migrator.MigrateAsync());

        Assert.True(File.Exists(sourcePath));
        Assert.Equal("original content", await File.ReadAllTextAsync(sourcePath));
    }

    private sealed class InMemoryMigrationTarget : IPrivateStorageMigrationTarget
    {
        private readonly ConcurrentDictionary<(Guid FarmId, string Key), byte[]> objects = new();

        public Dictionary<(Guid FarmId, string Key), string> ContentTypes { get; } = [];

        public bool FailWrites { get; init; }

        public Task<Stream> OpenDestinationReadAsync(
            Guid breedingFarmId,
            string objectKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!objects.TryGetValue((breedingFarmId, objectKey), out var content))
            {
                throw new FileNotFoundException("Destination object does not exist.");
            }

            return Task.FromResult<Stream>(new MemoryStream(content, writable: false));
        }

        public async Task PutMigratedObjectAsync(
            Guid breedingFarmId,
            string objectKey,
            string contentType,
            long expectedLength,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            if (FailWrites)
            {
                throw new IOException("Simulated destination failure.");
            }

            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            if (copy.Length != expectedLength)
            {
                throw new InvalidDataException("Expected and actual lengths differ.");
            }

            objects[(breedingFarmId, objectKey)] = copy.ToArray();
            ContentTypes[(breedingFarmId, objectKey)] = contentType;
        }

        public void Put(Guid breedingFarmId, string objectKey, string content) =>
            objects[(breedingFarmId, objectKey)] = Encoding.UTF8.GetBytes(content);

        public async Task<string> ReadTextAsync(Guid breedingFarmId, string objectKey)
        {
            await using var stream = await OpenDestinationReadAsync(breedingFarmId, objectKey);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }
    }

    private sealed class TemporaryVolume : IAsyncDisposable
    {
        public TemporaryVolume() => RootPath = Path.Combine(
            Path.GetTempPath(),
            "CriatorioVirtualMigrationTests",
            Guid.NewGuid().ToString("N"));

        public string RootPath { get; }

        public string GetPath(Guid farmId, string key) => Path.Combine(
            RootPath,
            farmId.ToString("N"),
            key.Replace('/', Path.DirectorySeparatorChar));

        public async Task<string> WriteAsync(Guid farmId, string key, string contents)
        {
            var path = GetPath(farmId, key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, contents);
            return path;
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
