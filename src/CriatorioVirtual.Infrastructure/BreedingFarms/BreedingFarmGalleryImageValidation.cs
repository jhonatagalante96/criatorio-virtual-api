using System.Buffers.Binary;
using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Storage;

namespace CriatorioVirtual.Infrastructure.BreedingFarms;

internal static class BreedingFarmGalleryImageValidation
{
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static bool TryValidate(
        string fileName,
        string contentType,
        ReadOnlySpan<byte> content,
        out int width,
        out int height,
        out string error)
    {
        width = 0;
        height = 0;
        if (content.IsEmpty)
        {
            error = "The image file cannot be empty.";
            return false;
        }

        if (!PrivateObjectStorageFileValidation.TryValidateMetadata(fileName, contentType, out error))
        {
            return false;
        }

        var normalizedType = contentType.Trim().ToLowerInvariant();
        var valid = normalizedType switch
        {
            "image/png" when Path.GetExtension(fileName).Equals(".png", StringComparison.OrdinalIgnoreCase) =>
                TryReadPngDimensions(content, out width, out height),
            "image/jpeg" when IsJpegExtension(fileName) => TryReadJpegDimensions(content, out width, out height),
            "image/webp" when Path.GetExtension(fileName).Equals(".webp", StringComparison.OrdinalIgnoreCase) =>
                TryReadWebpDimensions(content, out width, out height),
            _ => false
        };

        if (!valid || width <= 0 || height <= 0 ||
            width > BreedingFarmGalleryUploadLimits.MaxWidth ||
            height > BreedingFarmGalleryUploadLimits.MaxHeight ||
            (long)width * height > BreedingFarmGalleryUploadLimits.MaxPixelCount)
        {
            error = "Only valid PNG, JPEG, or WebP images with supported dimensions are accepted.";
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

        var w = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(16, 4));
        var h = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(20, 4));
        if (w > int.MaxValue || h > int.MaxValue)
        {
            return false;
        }

        width = (int)w;
        height = (int)h;
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

    private static bool TryReadWebpDimensions(ReadOnlySpan<byte> content, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (content.Length < 30 || !content[..4].SequenceEqual("RIFF"u8) ||
            !content.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return false;
        }

        var chunk = content.Slice(12, 4);
        if (chunk.SequenceEqual("VP8X"u8) && content.Length >= 30)
        {
            width = 1 + ReadUInt24LittleEndian(content.Slice(24, 3));
            height = 1 + ReadUInt24LittleEndian(content.Slice(27, 3));
            return true;
        }

        if (chunk.SequenceEqual("VP8 "u8) && content.Length >= 30 &&
            content[23] == 0x9D && content[24] == 0x01 && content[25] == 0x2A)
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(content.Slice(26, 2)) & 0x3FFF;
            height = BinaryPrimitives.ReadUInt16LittleEndian(content.Slice(28, 2)) & 0x3FFF;
            return true;
        }

        if (chunk.SequenceEqual("VP8L"u8) && content.Length >= 25 && content[20] == 0x2F)
        {
            width = 1 + content[21] + ((content[22] & 0x3F) << 8);
            height = 1 + ((content[22] & 0xC0) >> 6) + (content[23] << 2) + ((content[24] & 0x0F) << 10);
            return true;
        }

        return false;
    }

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> bytes) =>
        bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);

    private static bool IsJpegExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStartOfFrame(byte marker) =>
        marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or
            0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;
}
