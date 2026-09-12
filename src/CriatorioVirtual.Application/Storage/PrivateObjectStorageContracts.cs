namespace CriatorioVirtual.Application.Storage;

public static class PrivateObjectStorageFileValidation
{
    private static readonly IReadOnlyDictionary<string, string[]> SupportedExtensionsByContentType =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = [".pdf"],
            ["image/gif"] = [".gif"],
            ["image/heic"] = [".heic"],
            ["image/heif"] = [".heif"],
            ["image/jpeg"] = [".jpeg", ".jpg"],
            ["image/png"] = [".png"],
            ["image/webp"] = [".webp"]
        };

    public static bool TryValidateMetadata(
        string? fileName,
        string? contentType,
        out string error)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Length > 255 ||
            fileName.Contains('\0') ||
            fileName.Contains('/') ||
            fileName.Contains('\\') ||
            fileName is "." or ".." ||
            fileName.Contains('\r') ||
            fileName.Contains('\n'))
        {
            error = "The file name must not contain path traversal or control characters.";
            return false;
        }

        var normalizedContentType = contentType?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedContentType) ||
            normalizedContentType.Length > 100 ||
            !SupportedExtensionsByContentType.TryGetValue(normalizedContentType, out var extensions))
        {
            error = "The content type is not supported for private storage.";
            return false;
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension) ||
            !extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            error = "The file extension does not match the declared content type.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool IsSupportedImageContentType(string? contentType) =>
        contentType is not null &&
        SupportedExtensionsByContentType.ContainsKey(contentType.Trim().ToLowerInvariant()) &&
        contentType.Trim().StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}

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
