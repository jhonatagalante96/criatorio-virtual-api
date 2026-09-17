using CriatorioVirtual.Application.Identity;
using CriatorioVirtual.Application.Storage;
using StbImageSharp;

namespace CriatorioVirtual.Infrastructure.Identity;

internal static class UserAvatarImageValidation
{
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static bool TryValidate(
        string fileName,
        string contentType,
        ReadOnlySpan<byte> content,
        out string error)
    {
        if (content.IsEmpty)
        {
            error = "The image file cannot be empty.";
            return false;
        }

        if (!PrivateObjectStorageFileValidation.TryValidateMetadata(fileName, contentType, out error))
        {
            return false;
        }

        var normalizedContentType = contentType.Trim().ToLowerInvariant();
        var hasMatchingImageSignature = normalizedContentType switch
        {
            "image/png" when Path.GetExtension(fileName).Equals(".png", StringComparison.OrdinalIgnoreCase) =>
                content.StartsWith(PngSignature),
            "image/jpeg" when IsJpegExtension(fileName) =>
                content.Length >= 4 && content[0] == 0xFF && content[1] == 0xD8 &&
                content[^2] == 0xFF && content[^1] == 0xD9,
            _ => false
        };

        if (!hasMatchingImageSignature || !IsDecodableImage(content))
        {
            error = "Only valid PNG or JPEG images with matching file extensions are accepted.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsDecodableImage(ReadOnlySpan<byte> content)
    {
        var encodedImage = content.ToArray();
        try
        {
            using var headerStream = new MemoryStream(encodedImage, writable: false);
            var imageInfo = ImageInfo.FromStream(headerStream);
            if (imageInfo is not { } dimensions ||
                !IsAllowedDimensions(dimensions.Width, dimensions.Height))
            {
                return false;
            }

            var decodedImage = ImageResult.FromMemory(encodedImage, ColorComponents.Grey);
            return decodedImage.Width == dimensions.Width &&
                decodedImage.Height == dimensions.Height &&
                IsAllowedDimensions(decodedImage.Width, decodedImage.Height);
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
        catch (Exception)
        {
            // Image decoders may report malformed, truncated, or unsupported pixel data with different exceptions.
            return false;
        }
    }

    private static bool IsAllowedDimensions(int width, int height) =>
        width > 0 &&
        height > 0 &&
        width <= UserAvatarUploadLimits.MaxWidth &&
        height <= UserAvatarUploadLimits.MaxHeight &&
        (long)width * height <= UserAvatarUploadLimits.MaxPixelCount;

    private static bool IsJpegExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }
}
