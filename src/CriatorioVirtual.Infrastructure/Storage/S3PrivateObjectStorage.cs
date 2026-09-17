using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using CriatorioVirtual.Application.Storage;
using Microsoft.Extensions.Options;

namespace CriatorioVirtual.Infrastructure.Storage;

public sealed class S3PrivateObjectStorage : IPrivateObjectStorage, IPrivateStorageMigrationTarget
{
    private const int BufferSize = 64 * 1024;

    private readonly IAmazonS3 client;
    private readonly string bucket;
    private readonly FileSystemPrivateObjectStorage? legacyStorage;

    public S3PrivateObjectStorage(IAmazonS3 client, IOptions<PrivateStorageOptions> options)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(value.S3.Bucket);
        bucket = value.S3.Bucket;
        if (!string.IsNullOrWhiteSpace(value.LegacyPrivateRootPath))
        {
            legacyStorage = new FileSystemPrivateObjectStorage(Options.Create(new PrivateStorageOptions
            {
                PrivateRootPath = value.LegacyPrivateRootPath
            }));
        }
    }

    public async Task<PrivateObjectDescriptor> PutAsync(
        PrivateObjectUpload upload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        PrivateObjectStorageKeyValidation.ValidateTenant(upload.BreedingFarmId);
        PrivateObjectStorageKeyValidation.ValidateObjectKey(upload.ObjectKey);
        if (!PrivateObjectStorageFileValidation.TryValidateMetadata(upload.FileName, upload.ContentType, out var error))
        {
            throw new ArgumentException(error, nameof(upload));
        }

        var length = await PutCoreAsync(
            upload.BreedingFarmId,
            upload.ObjectKey,
            upload.ContentType.Trim().ToLowerInvariant(),
            upload.Content,
            expectedLength: null,
            cancellationToken);
        return new PrivateObjectDescriptor(
            upload.BreedingFarmId,
            upload.ObjectKey,
            upload.ContentType.Trim().ToLowerInvariant(),
            length);
    }

    public async Task<Stream> OpenReadAsync(
        Guid breedingFarmId,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await OpenDestinationReadAsync(breedingFarmId, objectKey, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            if (legacyStorage is not null)
            {
                return await legacyStorage.OpenReadAsync(breedingFarmId, objectKey, cancellationToken);
            }

            throw;
        }
    }

    public async Task<Stream> OpenDestinationReadAsync(
        Guid breedingFarmId,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        var key = PrivateObjectStorageKeyValidation.ToTenantKey(breedingFarmId, objectKey);
        try
        {
            var response = await client.GetObjectAsync(bucket, key, cancellationToken);
            return new ResponseOwnedStream(response);
        }
        catch (AmazonS3Exception exception) when (IsMissingObject(exception))
        {
            throw new FileNotFoundException("The private object was not found.", exception);
        }
    }

    public async Task DeleteAsync(
        Guid breedingFarmId,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        var key = PrivateObjectStorageKeyValidation.ToTenantKey(breedingFarmId, objectKey);
        await client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = bucket,
            Key = key
        }, cancellationToken);
    }

    public Task PutMigratedObjectAsync(
        Guid breedingFarmId,
        string objectKey,
        string contentType,
        long expectedLength,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        if (expectedLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedLength));
        }

        if (string.IsNullOrWhiteSpace(contentType) || contentType.Length > 100 ||
            contentType.Contains('\r') || contentType.Contains('\n'))
        {
            throw new ArgumentException("A valid content type is required for a migrated object.", nameof(contentType));
        }

        return PutCoreAsync(
            breedingFarmId,
            objectKey,
            contentType,
            content,
            expectedLength,
            cancellationToken);
    }

    private async Task<long> PutCoreAsync(
        Guid breedingFarmId,
        string objectKey,
        string contentType,
        Stream content,
        long? expectedLength,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead)
        {
            throw new ArgumentException("The upload stream must be readable.", nameof(content));
        }

        var key = PrivateObjectStorageKeyValidation.ToTenantKey(breedingFarmId, objectKey);
        await using var prepared = await PrepareUploadStreamAsync(content, cancellationToken);
        if (expectedLength is not null && prepared.Length != expectedLength.Value)
        {
            throw new InvalidDataException("The source length changed while preparing a migrated object.");
        }

        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = prepared.Stream,
            ContentType = contentType,
            AutoCloseStream = false,
            AutoResetStreamPosition = false
        }, cancellationToken);

        return prepared.Length;
    }

    private static async Task<PreparedUploadStream> PrepareUploadStreamAsync(
        Stream content,
        CancellationToken cancellationToken)
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "CriatorioVirtual", "s3-upload-staging");
        Directory.CreateDirectory(temporaryDirectory);
        var temporaryPath = Path.Combine(temporaryDirectory, $"{Guid.NewGuid():N}.uploading");
        var temporary = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
        try
        {
            await content.CopyToAsync(temporary, BufferSize, cancellationToken);
            await temporary.FlushAsync(cancellationToken);
            temporary.Position = 0;
            return new PreparedUploadStream(temporary, temporary.Length, temporaryPath);
        }
        catch
        {
            await temporary.DisposeAsync();
            throw;
        }
    }

    private static bool IsMissingObject(AmazonS3Exception exception) =>
        exception.StatusCode == HttpStatusCode.NotFound &&
        string.Equals(exception.ErrorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase);

    private sealed class PreparedUploadStream(Stream stream, long length, string temporaryPath) : IAsyncDisposable
    {
        public Stream Stream { get; } = stream;

        public long Length { get; } = length;

        public async ValueTask DisposeAsync()
        {
            await Stream.DisposeAsync();
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed class ResponseOwnedStream(GetObjectResponse response) : Stream
    {
        private readonly Stream stream = response.ResponseStream;
        private bool disposed;

        public override bool CanRead => !disposed && stream.CanRead;

        public override bool CanSeek => !disposed && stream.CanSeek;

        public override bool CanWrite => false;

        public override long Length => stream.Length;

        public override long Position
        {
            get => stream.Position;
            set => stream.Position = value;
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => stream.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            stream.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            stream.ReadAsync(buffer, offset, count, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => stream.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (!disposed && disposing)
            {
                response.Dispose();
                disposed = true;
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!disposed)
            {
                response.Dispose();
                disposed = true;
            }

            await base.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}
