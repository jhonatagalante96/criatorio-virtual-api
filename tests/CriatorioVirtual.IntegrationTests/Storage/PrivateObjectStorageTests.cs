using System.Net;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Storage;

public sealed class PrivateObjectStorageTests
{
    [Fact]
    public async Task StoresObjectsInsideTenantNamespaceAndDoesNotExposeThePhysicalLocation()
    {
        await using var temporary = new TemporaryStorage();
        var storage = CreateStorage(temporary.RootPath);
        var sourceFarmId = Guid.NewGuid();
        var objectKey = "birds/attachment-001";
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("private content"));

        var descriptor = await storage.PutAsync(new PrivateObjectUpload(
            sourceFarmId,
            objectKey,
            "bird.jpg",
            "image/jpeg",
            content));

        Assert.Equal(sourceFarmId, descriptor.BreedingFarmId);
        Assert.Equal(objectKey, descriptor.ObjectKey);
        Assert.Equal("image/jpeg", descriptor.ContentType);
        Assert.Equal("private content".Length, descriptor.Length);
        Assert.DoesNotContain(
            nameof(PrivateStorageOptions.PrivateRootPath),
            typeof(PrivateObjectDescriptor).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain(
            "Url",
            typeof(PrivateObjectDescriptor).GetProperties().Select(property => property.Name));

        var expectedPath = Path.Combine(
            temporary.RootPath,
            sourceFarmId.ToString("N"),
            "birds",
            "attachment-001");
        Assert.True(File.Exists(expectedPath));

        await using var stored = await storage.OpenReadAsync(sourceFarmId, objectKey);
        using var reader = new StreamReader(stored, Encoding.UTF8);
        Assert.Equal("private content", await reader.ReadToEndAsync());

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            storage.OpenReadAsync(Guid.NewGuid(), objectKey));
    }

    [Theory]
    [InlineData("bird.pdf", "image/jpeg")]
    [InlineData("bird.jpg", "application/pdf")]
    [InlineData("bird.png", "image/webp")]
    public async Task RejectsMimeAndExtensionMismatches(string fileName, string contentType)
    {
        await using var temporary = new TemporaryStorage();
        var storage = CreateStorage(temporary.RootPath);
        using var content = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<ArgumentException>(() => storage.PutAsync(new PrivateObjectUpload(
            Guid.NewGuid(),
            "birds/attachment-002",
            fileName,
            contentType,
            content)));
    }

    [Fact]
    public async Task RejectsPathTraversalForObjectKeysAndFileNames()
    {
        await using var temporary = new TemporaryStorage();
        var storage = CreateStorage(temporary.RootPath);
        var farmId = Guid.NewGuid();

        using var keyContent = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<ArgumentException>(() => storage.PutAsync(new PrivateObjectUpload(
            farmId,
            "birds/../outside",
            "bird.jpg",
            "image/jpeg",
            keyContent)));

        using var fileNameContent = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<ArgumentException>(() => storage.PutAsync(new PrivateObjectUpload(
            farmId,
            "birds/attachment-003",
            "../bird.jpg",
            "image/jpeg",
            fileNameContent)));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.OpenReadAsync(farmId, "../../outside"));
    }

    [Fact]
    public async Task DeleteRemovesOnlyTheRequestedTenantObject()
    {
        await using var temporary = new TemporaryStorage();
        var storage = CreateStorage(temporary.RootPath);
        var farmId = Guid.NewGuid();
        var otherFarmId = Guid.NewGuid();
        const string objectKey = "birds/attachment-004";

        using var content = new MemoryStream([4, 5, 6]);
        await storage.PutAsync(new PrivateObjectUpload(
            farmId,
            objectKey,
            "bird.png",
            "image/png",
            content));
        using var otherContent = new MemoryStream([7, 8, 9]);
        await storage.PutAsync(new PrivateObjectUpload(
            otherFarmId,
            objectKey,
            "bird.png",
            "image/png",
            otherContent));

        await storage.DeleteAsync(farmId, objectKey);

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            storage.OpenReadAsync(farmId, objectKey));
        await using var otherStored = await storage.OpenReadAsync(otherFarmId, objectKey);
        Assert.Equal(3, otherStored.Length);
    }

    [Fact]
    public async Task MoveAsyncRelocatesObjectAcrossTenantsAndRevokesOriginAccess()
    {
        await using var temporary = new TemporaryStorage();
        var storage = CreateStorage(temporary.RootPath);
        var sourceFarmId = Guid.NewGuid();
        var destinationFarmId = Guid.NewGuid();
        const string objectKey = "birds/attachment-005";
        var payload = "attachment binary payload"u8.ToArray();

        using var content = new MemoryStream(payload);
        await storage.PutAsync(new PrivateObjectUpload(
            sourceFarmId,
            objectKey,
            "bird.jpg",
            "image/jpeg",
            content));

        await storage.MoveAsync(sourceFarmId, destinationFarmId, objectKey);

        // Origin loses access
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            storage.OpenReadAsync(sourceFarmId, objectKey));

        // Destination acquires access with identical content
        await using var destinationStream = await storage.OpenReadAsync(destinationFarmId, objectKey);
        using var memory = new MemoryStream();
        await destinationStream.CopyToAsync(memory);
        Assert.Equal(payload, memory.ToArray());
    }

    [Fact]
    public async Task MoveAsyncRejectsMissingSourceAndValidatesKeys()
    {
        await using var temporary = new TemporaryStorage();
        var storage = CreateStorage(temporary.RootPath);
        var sourceFarmId = Guid.NewGuid();
        var destinationFarmId = Guid.NewGuid();

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            storage.MoveAsync(sourceFarmId, destinationFarmId, "birds/nonexistent"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.MoveAsync(Guid.Empty, destinationFarmId, "birds/valid"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.MoveAsync(sourceFarmId, Guid.Empty, "birds/valid"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.MoveAsync(sourceFarmId, destinationFarmId, "../invalid/key"));
    }

    [Fact]
    public void ProductionRequiresS3Configuration()
    {
        var configuration = new ConfigurationBuilder().Build();
        var environment = new TestHostEnvironment("Production");
        using var provider = BuildProvider(configuration, environment);

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<PrivateStorageOptions>>().Value);

        Assert.Contains("Storage:Provider", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TestingDefaultsToAPrivateAbsoluteStorageRoot()
    {
        var configuration = new ConfigurationBuilder().Build();
        var environment = new TestHostEnvironment("Testing");
        using var provider = BuildProvider(configuration, environment);

        var options = provider.GetRequiredService<IOptions<PrivateStorageOptions>>().Value;

        Assert.True(Path.IsPathRooted(options.PrivateRootPath));
    }

    [Fact]
    public void ProductionAcceptsS3SettingsWithoutASeparateFilesystemRoot()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Provider"] = "S3",
                ["Storage:S3:Endpoint"] = "https://t3.storageapi.dev",
                ["Storage:S3:Bucket"] = "production-private-bucket",
                ["Storage:S3:Region"] = "auto",
                ["Storage:S3:AccessKeyId"] = "test-access-key",
                ["Storage:S3:SecretAccessKey"] = "test-secret-key"
            })
            .Build();
        var environment = new TestHostEnvironment("Production");
        using var provider = BuildProvider(configuration, environment);

        var options = provider.GetRequiredService<IOptions<PrivateStorageOptions>>().Value;

        Assert.Equal("S3", options.Provider);
        Assert.Equal("production-private-bucket", options.S3.Bucket);
        Assert.Empty(options.PrivateRootPath);
    }

    [Fact]
    public void S3RejectsCredentialsOnNonHttpsOrUserInfoEndpoints()
    {
        var environment = new TestHostEnvironment("Production");
        var validator = new PrivateStorageOptionsValidator(environment);
        var options = new PrivateStorageOptions
        {
            Provider = "S3",
            S3 = new S3PrivateStorageOptions
            {
                Endpoint = "http://access:secret@storage.example.test",
                Bucket = "private-bucket",
                Region = "auto",
                AccessKeyId = "access",
                SecretAccessKey = "secret"
            }
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("HTTPS endpoint", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task S3StorageScopesKeysAndPreservesContentTypeAndLength()
    {
        using var client = new InMemoryS3Client();
        var options = Options.Create(new PrivateStorageOptions
        {
            Provider = "S3",
            S3 = new S3PrivateStorageOptions { Bucket = "private-bucket" }
        });
        var storage = new S3PrivateObjectStorage(client, options);
        var farmId = Guid.NewGuid();
        const string objectKey = "birds/photo-001";
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("private photo"));

        var descriptor = await storage.PutAsync(new PrivateObjectUpload(
            farmId,
            objectKey,
            "bird.jpg",
            "image/jpeg",
            content));

        Assert.Equal(farmId, descriptor.BreedingFarmId);
        Assert.Equal(objectKey, descriptor.ObjectKey);
        Assert.Equal("image/jpeg", descriptor.ContentType);
        Assert.Equal("private photo".Length, descriptor.Length);
        Assert.Equal($"{farmId:N}/{objectKey}", client.LastPutRequest?.Key);
        Assert.Equal("image/jpeg", client.LastPutRequest?.ContentType);
        Assert.Null(client.LastPutRequest?.CannedACL);

        await using var stored = await storage.OpenReadAsync(farmId, objectKey);
        using var reader = new StreamReader(stored, Encoding.UTF8);
        Assert.Equal("private photo", await reader.ReadToEndAsync());

        await Assert.ThrowsAsync<FileNotFoundException>(() => storage.OpenReadAsync(Guid.NewGuid(), objectKey));
        await storage.DeleteAsync(farmId, objectKey);
        await Assert.ThrowsAsync<FileNotFoundException>(() => storage.OpenReadAsync(farmId, objectKey));
    }

    [Fact]
    public async Task S3ReadFallsBackToTheLegacyVolumeWhileItIsConfigured()
    {
        await using var temporary = new TemporaryStorage();
        using var client = new InMemoryS3Client();
        var farmId = Guid.NewGuid();
        const string objectKey = "documents/legacy.pdf";
        var legacyPath = Path.Combine(temporary.RootPath, farmId.ToString("N"), "documents", "legacy.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        await File.WriteAllTextAsync(legacyPath, "volume copy");

        var storage = new S3PrivateObjectStorage(client, Options.Create(new PrivateStorageOptions
        {
            Provider = "S3",
            LegacyPrivateRootPath = temporary.RootPath,
            S3 = new S3PrivateStorageOptions { Bucket = "private-bucket" }
        }));

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            storage.OpenDestinationReadAsync(farmId, objectKey));
        await using var stored = await storage.OpenReadAsync(farmId, objectKey);
        using var reader = new StreamReader(stored, Encoding.UTF8);
        Assert.Equal("volume copy", await reader.ReadToEndAsync());
        Assert.True(File.Exists(legacyPath));
    }

    [Fact]
    public async Task S3MoveAsyncCompensatesDestinationCopyWhenOriginDeleteFails()
    {
        using var client = new InMemoryS3Client();
        var options = Options.Create(new PrivateStorageOptions
        {
            Provider = "S3",
            S3 = new S3PrivateStorageOptions { Bucket = "private-bucket" }
        });
        var storage = new S3PrivateObjectStorage(client, options);
        var sourceFarmId = Guid.NewGuid();
        var destinationFarmId = Guid.NewGuid();
        const string objectKey = "birds/photo-001";
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("private photo"));

        await storage.PutAsync(new PrivateObjectUpload(
            sourceFarmId,
            objectKey,
            "bird.jpg",
            "image/jpeg",
            content));

        // Configure client to fail deleting the source key
        client.FailDeleteKey = $"{sourceFarmId:N}/{objectKey}";

        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(() =>
            storage.MoveAsync(sourceFarmId, destinationFarmId, objectKey));
        Assert.Equal("Simulated delete failure", exception.Message);

        // Origin still has the file
        await using var sourceStored = await storage.OpenReadAsync(sourceFarmId, objectKey);
        using var reader = new StreamReader(sourceStored, Encoding.UTF8);
        Assert.Equal("private photo", await reader.ReadToEndAsync());

        // Destination was compensated (deleted) and does not have the orphan file
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            storage.OpenReadAsync(destinationFarmId, objectKey));
    }

    private static IPrivateObjectStorage CreateStorage(string rootPath) =>
        new FileSystemPrivateObjectStorage(Options.Create(new PrivateStorageOptions
        {
            PrivateRootPath = rootPath
        }));

    private static ServiceProvider BuildProvider(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var services = new ServiceCollection();
        services.AddSingleton(environment);
        services.AddPrivateStorage(configuration, environment);
        return services.BuildServiceProvider();
    }

    private sealed class TemporaryStorage : IAsyncDisposable
    {
        public TemporaryStorage() =>
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "CriatorioVirtualTests",
                Guid.NewGuid().ToString("N"));

        public string RootPath { get; }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "CriatorioVirtual.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class InMemoryS3Client() : AmazonS3Client(
        new AnonymousAWSCredentials(),
        new AmazonS3Config { ServiceURL = "https://s3.example.test", AuthenticationRegion = "auto" })
    {
        private readonly Dictionary<(string Bucket, string Key), byte[]> objects = [];

        public string? FailDeleteKey { get; set; }

        public PutObjectRequest? LastPutRequest { get; private set; }

        public override async Task<PutObjectResponse> PutObjectAsync(
            PutObjectRequest request,
            CancellationToken cancellationToken = default)
        {
            LastPutRequest = request;
            using var content = new MemoryStream();
            await request.InputStream.CopyToAsync(content, cancellationToken);
            objects[(request.BucketName, request.Key)] = content.ToArray();
            return new PutObjectResponse();
        }

        public override Task<CopyObjectResponse> CopyObjectAsync(
            CopyObjectRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!objects.TryGetValue((request.SourceBucket, request.SourceKey), out var content))
            {
                return Task.FromException<CopyObjectResponse>(new AmazonS3Exception("Source object not found")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchKey"
                });
            }

            objects[(request.DestinationBucket, request.DestinationKey)] = content;
            return Task.FromResult(new CopyObjectResponse());
        }

        public override Task<GetObjectResponse> GetObjectAsync(
            string bucketName,
            string key,
            CancellationToken cancellationToken = default)
        {
            if (!objects.TryGetValue((bucketName, key), out var content))
            {
                return Task.FromException<GetObjectResponse>(new AmazonS3Exception("Object not found")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchKey"
                });
            }

            return Task.FromResult(new GetObjectResponse
            {
                ResponseStream = new MemoryStream(content, writable: false)
            });
        }

        public override Task<DeleteObjectResponse> DeleteObjectAsync(
            DeleteObjectRequest request,
            CancellationToken cancellationToken = default)
        {
            if (FailDeleteKey is not null && request.Key == FailDeleteKey)
            {
                return Task.FromException<DeleteObjectResponse>(new AmazonS3Exception("Simulated delete failure")
                {
                    StatusCode = HttpStatusCode.InternalServerError,
                    ErrorCode = "InternalError"
                });
            }

            objects.Remove((request.BucketName, request.Key));
            return Task.FromResult(new DeleteObjectResponse());
        }
    }
}
