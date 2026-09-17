using System.Buffers.Binary;
using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Storage;

namespace CriatorioVirtual.Infrastructure.BreedingFarms.CoverTemplates;

internal static class BreedingFarmCoverImageValidation
{
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static bool TryValidate(
        string fileName,
        string contentType,
        ReadOnlySpan<byte> content,
        out string normalizedContentType,
        out string error)
    {
        normalizedContentType = contentType.Trim().ToLowerInvariant();
        if (content.IsEmpty || content.Length > BreedingFarmCoverUploadLimits.MaxFileLength)
        {
            error = "The cover image is empty or too large.";
            return false;
        }

        if (!PrivateObjectStorageFileValidation.TryValidateMetadata(fileName, normalizedContentType, out error))
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);
        var dimensionsValid = normalizedContentType switch
        {
            "image/png" when extension.Equals(".png", StringComparison.OrdinalIgnoreCase) =>
                TryReadPngDimensions(content, out var width, out var height) && IsAllowedDimensions(width, height),
            "image/jpeg" when extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) =>
                TryReadJpegDimensions(content, out var width, out var height) && IsAllowedDimensions(width, height),
            "image/webp" when extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) =>
                TryReadWebpDimensions(content, out var width, out var height) && IsAllowedDimensions(width, height),
            _ => false
        };

        if (!dimensionsValid)
        {
            error = "The cover must be a valid PNG, JPEG, or WebP image at least 1200×400 pixels.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryReadPngDimensions(ReadOnlySpan<byte> content, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (content.Length < 24 || !content[..8].SequenceEqual(PngSignature) ||
            !content.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }

        var imageWidth = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(16, 4));
        var imageHeight = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(20, 4));
        if (imageWidth > int.MaxValue || imageHeight > int.MaxValue)
        {
            return false;
        }

        width = (int)imageWidth;
        height = (int)imageHeight;
        return true;
    }

    private static bool TryReadJpegDimensions(ReadOnlySpan<byte> content, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (content.Length < 4 || content[0] != 0xff || content[1] != 0xd8)
        {
            return false;
        }

        var offset = 2;
        while (offset < content.Length)
        {
            if (content[offset++] != 0xff)
            {
                return false;
            }

            while (offset < content.Length && content[offset] == 0xff)
            {
                offset++;
            }

            if (offset >= content.Length)
            {
                return false;
            }

            var marker = content[offset++];
            if (marker is 0xd8 or 0xd9 or 0x01 or >= 0xd0 and <= 0xd7)
            {
                continue;
            }

            if (marker == 0xda || content.Length - offset < 2)
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

    private static bool TryReadWebpDimensions(ReadOnlySpan<byte> content, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (content.Length < 30 || !content[..4].SequenceEqual("RIFF"u8) ||
            !content.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return false;
        }

        if (content.Slice(12, 4).SequenceEqual("VP8X"u8))
        {
            width = 1 + ReadUInt24LittleEndian(content.Slice(24, 3));
            height = 1 + ReadUInt24LittleEndian(content.Slice(27, 3));
            return true;
        }

        if (content.Slice(12, 4).SequenceEqual("VP8 "u8) && content.Length >= 30 &&
            content[23] == 0x9d && content[24] == 0x01 && content[25] == 0x2a)
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(content.Slice(26, 2)) & 0x3fff;
            height = BinaryPrimitives.ReadUInt16LittleEndian(content.Slice(28, 2)) & 0x3fff;
            return true;
        }

        if (content.Slice(12, 4).SequenceEqual("VP8L"u8) && content.Length >= 25 && content[20] == 0x2f)
        {
            width = 1 + content[21] + ((content[22] & 0x3f) << 8);
            height = 1 + (content[22] >> 6) + (content[23] << 2) + ((content[24] & 0x0f) << 10);
            return true;
        }

        return false;
    }

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> content) =>
        content[0] | (content[1] << 8) | (content[2] << 16);

    private static bool IsStartOfFrame(byte marker) =>
        marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or
            0xc5 or 0xc6 or 0xc7 or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf;

    private static bool IsAllowedDimensions(int width, int height) =>
        width >= BreedingFarmCoverUploadLimits.MinimumWidth &&
        height >= BreedingFarmCoverUploadLimits.MinimumHeight &&
        width <= BreedingFarmCoverUploadLimits.MaxDimension &&
        height <= BreedingFarmCoverUploadLimits.MaxDimension &&
        (long)width * height <= BreedingFarmCoverUploadLimits.MaxPixelCount;
}
