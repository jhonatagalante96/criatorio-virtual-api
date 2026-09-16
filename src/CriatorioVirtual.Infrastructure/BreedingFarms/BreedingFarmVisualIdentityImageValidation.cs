using System.Buffers.Binary;
using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Storage;

namespace CriatorioVirtual.Infrastructure.BreedingFarms;

internal static class BreedingFarmVisualIdentityImageValidation
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
        var hasDimensions = normalizedContentType switch
        {
            "image/png" when Path.GetExtension(fileName).Equals(".png", StringComparison.OrdinalIgnoreCase) =>
                TryReadPngDimensions(content, out var width, out var height) && IsAllowedDimensions(width, height),
            "image/jpeg" when IsJpegExtension(fileName) =>
                TryReadJpegDimensions(content, out var width, out var height) && IsAllowedDimensions(width, height),
            _ => false
        };

        if (!hasDimensions)
        {
            error = "Only valid PNG or JPEG images with supported dimensions are accepted.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryReadPngDimensions(ReadOnlySpan<byte> content, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (content.Length < 24 ||
            !content[..8].SequenceEqual(PngSignature) ||
            !content.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }

        var unsignedWidth = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(16, 4));
        var unsignedHeight = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(20, 4));
        if (unsignedWidth > int.MaxValue || unsignedHeight > int.MaxValue)
        {
            return false;
        }

        width = (int)unsignedWidth;
        height = (int)unsignedHeight;
        return true;
    }

    private static bool TryReadJpegDimensions(ReadOnlySpan<byte> content, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (content.Length < 4 || content[0] != 0xFF || content[1] != 0xD8)
        {
            return false;
        }

        var offset = 2;
        while (offset < content.Length)
        {
            if (content[offset++] != 0xFF)
            {
                return false;
            }

            while (offset < content.Length && content[offset] == 0xFF)
            {
                offset++;
            }

            if (offset >= content.Length)
            {
                return false;
            }

            var marker = content[offset++];
            if (marker is 0xD8 or 0xD9 or 0x01 or >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            if (marker == 0xDA || content.Length - offset < 2)
            {
                return false;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(offset, 2));
            if (segmentLength < 2 || segmentLength > content.Length - offset)
            {
                return false;
            }

            if (IsStartOfFrame(marker))
            {
                if (segmentLength < 7)
                {
                    return false;
                }

                var frame = content.Slice(offset + 2, segmentLength - 2);
                height = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(1, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(3, 2));
                return true;
            }

            offset += segmentLength;
        }

        return false;
    }

    private static bool IsStartOfFrame(byte marker) =>
        marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or
            0xC5 or 0xC6 or 0xC7 or
            0xC9 or 0xCA or 0xCB or
            0xCD or 0xCE or 0xCF;

    private static bool IsJpegExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedDimensions(int width, int height) =>
        width > 0 &&
        height > 0 &&
        width <= BreedingFarmVisualIdentityUploadLimits.MaxWidth &&
        height <= BreedingFarmVisualIdentityUploadLimits.MaxHeight &&
        (long)width * height <= BreedingFarmVisualIdentityUploadLimits.MaxPixelCount;
}
