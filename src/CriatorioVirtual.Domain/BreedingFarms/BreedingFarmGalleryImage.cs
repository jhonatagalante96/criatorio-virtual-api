using CriatorioVirtual.Domain.Primitives;

namespace CriatorioVirtual.Domain.BreedingFarms;

public sealed class BreedingFarmGalleryImage : Entity
{
    public const int CaptionMaxLength = 500;

    private BreedingFarmGalleryImage()
        : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
    {
        ObjectKey = null!;
        FileName = null!;
        ContentType = null!;
    }

    public BreedingFarmGalleryImage(
        Guid id,
        DateTimeOffset createdAtUtc,
        Guid breedingFarmId,
        string objectKey,
        string fileName,
        string contentType,
        long length,
        int width,
        int height,
        string? caption)
        : base(id, createdAtUtc)
    {
        if (breedingFarmId == Guid.Empty)
        {
            throw new ArgumentException("The breeding farm identifier cannot be empty.", nameof(breedingFarmId));
        }

        BreedingFarmId = breedingFarmId;
        ObjectKey = RequireObjectKey(objectKey);
        FileName = RequireFileName(fileName);
        ContentType = RequireContentType(contentType);
        if (length <= 0 || width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Image length and dimensions must be positive.");
        }

        Length = length;
        Width = width;
        Height = height;
        Caption = NormalizeCaption(caption);
    }

    public Guid BreedingFarmId { get; private set; }

    public string ObjectKey { get; private set; }

    public string FileName { get; private set; }

    public string ContentType { get; private set; }

    public long Length { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public string? Caption { get; private set; }

    public DateTimeOffset? DeletedAtUtc { get; private set; }

    public bool StorageCleanupPending { get; private set; }

    public bool IsDeleted => DeletedAtUtc is not null;

    public void UpdateCaption(string? caption, DateTimeOffset updatedAtUtc)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("A deleted gallery image cannot be edited.");
        }

        Caption = NormalizeCaption(caption);
        EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        Touch(updatedAtUtc);
    }

    public void MarkDeleted(DateTimeOffset deletedAtUtc)
    {
        if (DeletedAtUtc is not null)
        {
            return;
        }

        EnsureUtc(deletedAtUtc, nameof(deletedAtUtc));
        DeletedAtUtc = deletedAtUtc;
        StorageCleanupPending = true;
        Touch(deletedAtUtc);
    }

    public void MarkStorageCleanupCompleted(DateTimeOffset updatedAtUtc)
    {
        if (!IsDeleted)
        {
            throw new InvalidOperationException("Only deleted gallery images can complete storage cleanup.");
        }

        EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        StorageCleanupPending = false;
        Touch(updatedAtUtc);
    }

    public static string? NormalizeCaption(string? caption)
    {
        var normalized = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        if (normalized?.Length > CaptionMaxLength)
        {
            throw new ArgumentException($"A caption cannot exceed {CaptionMaxLength} characters.", nameof(caption));
        }

        return normalized;
    }

    private static string RequireObjectKey(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 500 ||
            normalized.Contains('\0') || normalized.Contains('\\') ||
            normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Contains(':', StringComparison.Ordinal) ||
            normalized.Split('/', StringSplitOptions.None).Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new ArgumentException("A safe relative object key is required.", nameof(value));
        }

        return normalized;
    }

    private static string RequireFileName(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 255 || normalized is "." or ".." ||
            normalized.Contains('\0') || normalized.Contains('/') || normalized.Contains('\\') ||
            normalized.Contains('\r') || normalized.Contains('\n'))
        {
            throw new ArgumentException("A safe file name of at most 255 characters is required.", nameof(value));
        }

        return normalized;
    }

    private static string RequireContentType(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 100)
        {
            throw new ArgumentException("A content type of at most 100 characters is required.", nameof(value));
        }

        return normalized;
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamps must be expressed in UTC.", parameterName);
        }
    }
}
