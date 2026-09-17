using CriatorioVirtual.Application.Storage;

namespace CriatorioVirtual.Infrastructure.Birds;

internal static class BreedingFarmMediaFileValidation
{
    public static async Task<bool> TryValidateAsync(
        string fileName,
        string contentType,
        long length,
        Stream content,
        CancellationToken cancellationToken)
    {
        if (length <= 0 ||
            !content.CanRead ||
            !content.CanSeek ||
            !PrivateObjectStorageFileValidation.TryValidateMetadata(fileName, contentType, out _))
        {
            return false;
        }

        if (!PrivateObjectStorageFileValidation.IsSupportedMediaContentType(contentType))
        {
            return true;
        }

        content.Position = 0;
        var header = new byte[12];
        var read = await content.ReadAsync(header.AsMemory(), cancellationToken);
        content.Position = 0;
        if (read < 4)
        {
            return false;
        }

        var normalizedType = contentType.Trim().ToLowerInvariant();
        return normalizedType switch
        {
            "image/png" => read >= 8 &&
                header[0] == 137 && header[1] == 80 && header[2] == 78 && header[3] == 71 &&
                header[4] == 13 && header[5] == 10 && header[6] == 26 && header[7] == 10,
            "image/jpeg" => header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            "image/webp" => read >= 12 &&
                header.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
                header.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            "image/gif" => read >= 6 &&
                (header.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || header.AsSpan(0, 6).SequenceEqual("GIF89a"u8)),
            "image/heic" => HasIsoBaseMediaBrand(header, read, "heic", "heix", "hevc", "hevx"),
            "image/heif" => HasIsoBaseMediaBrand(header, read, "mif1", "msf1"),
            "video/mp4" => HasIsoBaseMediaBrand(header, read, "isom", "iso2", "mp41", "mp42", "avc1", "M4V "),
            "video/webm" => read >= 4 &&
                header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3,
            _ => false
        };
    }

    private static bool HasIsoBaseMediaBrand(byte[] header, int read, params string[] brands)
    {
        if (read < 12 || !header.AsSpan(4, 4).SequenceEqual("ftyp"u8))
        {
            return false;
        }

        var brand = System.Text.Encoding.ASCII.GetString(header, 8, 4);
        return brands.Contains(brand, StringComparer.Ordinal);
    }
}
