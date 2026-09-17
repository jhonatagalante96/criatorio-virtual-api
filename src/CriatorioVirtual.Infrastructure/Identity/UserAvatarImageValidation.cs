using System.Buffers.Binary;
using System.IO.Compression;
using CriatorioVirtual.Application.Identity;
using CriatorioVirtual.Application.Storage;

namespace CriatorioVirtual.Infrastructure.Identity;

internal static class UserAvatarImageValidation
{
    private static readonly (int StartX, int StartY, int StepX, int StepY)[] Adam7Passes =
    [
        (0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4),
        (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)
    ];

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

        var valid = contentType.Trim().ToLowerInvariant() switch
        {
            "image/png" when Path.GetExtension(fileName).Equals(".png", StringComparison.OrdinalIgnoreCase) =>
                IsValidPng(content),
            "image/jpeg" when IsJpegExtension(fileName) => IsValidJpeg(content),
            _ => false
        };

        if (!valid)
        {
            error = "Only valid PNG or JPEG images with matching file extensions are accepted.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsValidPng(ReadOnlySpan<byte> content)
    {
        if (content.Length < 45 || !content[..8].SequenceEqual(PngSignature))
        {
            return false;
        }

        var offset = 8;
        var hasHeader = false;
        var hasImageData = false;
        var hasEnd = false;
        var width = 0;
        var height = 0;
        var bitDepth = 0;
        var colorType = 0;
        var interlaceMethod = 0;
        using var imageData = new MemoryStream();

        while (offset < content.Length)
        {
            if (content.Length - offset < 12)
            {
                return false;
            }

            var chunkLength = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(offset, 4));
            if (chunkLength > int.MaxValue || chunkLength > content.Length - offset - 12)
            {
                return false;
            }

            var length = (int)chunkLength;
            var chunkType = content.Slice(offset + 4, 4);
            var chunkData = content.Slice(offset + 8, length);
            var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(offset + 8 + length, 4));
            if (ComputeCrc32(content.Slice(offset + 4, length + 4)) != expectedCrc)
            {
                return false;
            }

            if (!hasHeader)
            {
                if (!chunkType.SequenceEqual("IHDR"u8) || length != 13)
                {
                    return false;
                }

                var unsignedWidth = BinaryPrimitives.ReadUInt32BigEndian(chunkData[..4]);
                var unsignedHeight = BinaryPrimitives.ReadUInt32BigEndian(chunkData.Slice(4, 4));
                if (unsignedWidth is 0 or > int.MaxValue || unsignedHeight is 0 or > int.MaxValue)
                {
                    return false;
                }

                width = (int)unsignedWidth;
                height = (int)unsignedHeight;
                bitDepth = chunkData[8];
                colorType = chunkData[9];
                interlaceMethod = chunkData[12];
                if (!IsAllowedDimensions(width, height) ||
                    chunkData[10] != 0 ||
                    chunkData[11] != 0 ||
                    interlaceMethod is not (0 or 1) ||
                    !IsValidBitDepth(colorType, bitDepth))
                {
                    return false;
                }

                hasHeader = true;
            }
            else if (chunkType.SequenceEqual("IHDR"u8))
            {
                return false;
            }
            else if (chunkType.SequenceEqual("IDAT"u8))
            {
                if (hasEnd)
                {
                    return false;
                }

                imageData.Write(chunkData);
                hasImageData = true;
            }
            else if (chunkType.SequenceEqual("IEND"u8))
            {
                if (length != 0 || !hasImageData || offset + 12 != content.Length)
                {
                    return false;
                }

                hasEnd = true;
                break;
            }

            offset += length + 12;
        }

        if (!hasHeader || !hasImageData || !hasEnd || imageData.Length == 0)
        {
            return false;
        }

        imageData.Position = 0;
        return HasValidPngImageData(imageData, width, height, bitDepth, colorType, interlaceMethod);
    }

    private static bool HasValidPngImageData(
        Stream compressedImageData,
        int width,
        int height,
        int bitDepth,
        int colorType,
        int interlaceMethod)
    {
        var channels = colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => 0
        };
        if (channels == 0)
        {
            return false;
        }

        try
        {
            using var decompressed = new ZLibStream(compressedImageData, CompressionMode.Decompress, leaveOpen: true);
            var rowBuffer = new byte[64 * 1024];
            var totalRows = 0L;
            foreach (var pass in GetPasses(width, height, interlaceMethod))
            {
                if (pass.Width == 0 || pass.Height == 0)
                {
                    continue;
                }

                var rowBytes = ((long)pass.Width * channels * bitDepth + 7) / 8;
                totalRows += (rowBytes + 1) * pass.Height;
                for (var row = 0; row < pass.Height; row++)
                {
                    var filter = decompressed.ReadByte();
                    if (filter is < 0 or > 4 || !SkipExactly(decompressed, rowBytes, rowBuffer))
                    {
                        return false;
                    }
                }
            }

            return totalRows <= 400_000_000 && decompressed.ReadByte() == -1;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static IEnumerable<(int Width, int Height)> GetPasses(int width, int height, int interlaceMethod)
    {
        if (interlaceMethod == 0)
        {
            yield return (width, height);
            yield break;
        }

        foreach (var (startX, startY, stepX, stepY) in Adam7Passes)
        {
            yield return (
                width <= startX ? 0 : (width - startX + stepX - 1) / stepX,
                height <= startY ? 0 : (height - startY + stepY - 1) / stepY);
        }
    }

    private static bool SkipExactly(Stream stream, long count, byte[] buffer)
    {
        while (count > 0)
        {
            var read = stream.Read(buffer, 0, (int)Math.Min(count, buffer.Length));
            if (read == 0)
            {
                return false;
            }

            count -= read;
        }

        return true;
    }

    private static bool IsValidJpeg(ReadOnlySpan<byte> content)
    {
        if (content.Length < 16 || content[0] != 0xFF || content[1] != 0xD8 ||
            content[^2] != 0xFF || content[^1] != 0xD9)
        {
            return false;
        }

        var offset = 2;
        var hasDimensions = false;
        var width = 0;
        var height = 0;
        while (offset < content.Length - 2)
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
            if (marker is 0xD8 or 0x01 or >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            if (content.Length - offset < 2)
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
                hasDimensions = IsAllowedDimensions(width, height);
            }

            offset += segmentLength;
            if (marker == 0xDA)
            {
                return hasDimensions && segmentLength >= 6 && offset < content.Length - 2;
            }
        }

        return false;
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    private static bool IsAllowedDimensions(int width, int height) =>
        width > 0 &&
        height > 0 &&
        width <= UserAvatarUploadLimits.MaxWidth &&
        height <= UserAvatarUploadLimits.MaxHeight &&
        (long)width * height <= UserAvatarUploadLimits.MaxPixelCount;

    private static bool IsValidBitDepth(int colorType, int bitDepth) => colorType switch
    {
        0 => bitDepth is 1 or 2 or 4 or 8 or 16,
        2 => bitDepth is 8 or 16,
        3 => bitDepth is 1 or 2 or 4 or 8,
        4 => bitDepth is 8 or 16,
        6 => bitDepth is 8 or 16,
        _ => false
    };

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
}
