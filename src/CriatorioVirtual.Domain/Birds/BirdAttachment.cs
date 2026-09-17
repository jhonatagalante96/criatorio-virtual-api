using CriatorioVirtual.Domain.Primitives;

namespace CriatorioVirtual.Domain.Birds;

public sealed class BirdAttachment : Entity
{
    public const int CaptionMaxLength = 500;

    private BirdAttachment()
        : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
    {
        ObjectKey = null!;
        FileName = null!;
        ContentType = null!;
    }

    public BirdAttachment(
        Guid id,
        DateTimeOffset createdAtUtc,
        Guid breedingFarmId,
        Guid? birdId,
        string objectKey,
        string fileName,
        string contentType,
        long length,
        string? caption = null)
        : base(id, createdAtUtc)
    {
        if (breedingFarmId == Guid.Empty)
        {
            throw new ArgumentException("The breeding farm identifier cannot be empty.", nameof(breedingFarmId));
        }

        if (birdId == Guid.Empty)
        {
            throw new ArgumentException("The bird identifier cannot be empty.", nameof(birdId));
        }

        ObjectKey = RequireObjectKey(objectKey);
        FileName = RequireFileName(fileName);
        ContentType = RequireContentType(contentType);
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "An attachment must contain at least one byte.");
        }

        BreedingFarmId = breedingFarmId;
        BirdId = birdId;
        Length = length;
        Caption = NormalizeCaption(caption);
    }

    public Guid BreedingFarmId { get; private set; }

    public Guid? BirdId { get; private set; }

    public string ObjectKey { get; private set; }

    public string FileName { get; private set; }

    public string ContentType { get; private set; }

    public long Length { get; private set; }

    public string? Caption { get; private set; }

    public DateTimeOffset? DeletedAtUtc { get; private set; }

    public bool StorageCleanupPending { get; private set; }

    public bool IsDeleted => DeletedAtUtc is not null;

    public bool IsMedia =>
        ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
        ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);

    public void UpdateCaption(string? caption, DateTimeOffset updatedAtUtc)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("A deleted attachment cannot be edited.");
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
        if (DeletedAtUtc is null)
        {
            throw new InvalidOperationException("Only deleted attachments can complete storage cleanup.");
        }

        EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        StorageCleanupPending = false;
        Touch(updatedAtUtc);
    }

    public static string? NormalizeCaption(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > CaptionMaxLength)
        {
            throw new ArgumentException($"A caption cannot exceed {CaptionMaxLength} characters.", nameof(value));
        }

        return normalized;
    }

    private static string RequireObjectKey(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 500)
        {
            throw new ArgumentException(
                "An attachment object key is required and cannot exceed 500 characters.",
                nameof(value));
        }

        if (normalized.Contains('\0') ||
            normalized.Contains('\\') ||
            normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Contains(':', StringComparison.Ordinal) ||
            normalized.Split('/', StringSplitOptions.None)
                .Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new ArgumentException("An attachment object key must be a safe relative path.", nameof(value));
        }

        return normalized;
    }

    private static string RequireFileName(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 255)
        {
            throw new ArgumentException(
                "An attachment file name is required and cannot exceed 255 characters.",
                nameof(value));
        }

        if (normalized is "." or ".." ||
            normalized.Contains('\0') ||
            normalized.Contains('/') ||
            normalized.Contains('\\') ||
            normalized.Contains('\r') ||
            normalized.Contains('\n'))
        {
            throw new ArgumentException("An attachment file name must not contain path traversal or control characters.", nameof(value));
        }

        return normalized;
    }

    private static string RequireContentType(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 100)
        {
            throw new ArgumentException(
                "An attachment content type is required and cannot exceed 100 characters.",
                nameof(value));
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
