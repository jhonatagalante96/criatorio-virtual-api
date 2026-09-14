using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace CriatorioVirtual.Infrastructure.Documents;

internal readonly record struct PdfColor(double Red, double Green, double Blue)
{
    internal string ToPdf() => string.Format(
        CultureInfo.InvariantCulture,
        "{0:0.###} {1:0.###} {2:0.###}",
        Red,
        Green,
        Blue);
}

internal readonly record struct PdfImage(int Width, int Height, string Filter, byte[] Data);

internal static class PdfDocumentPrimitives
{
    internal const double PointsPerMillimeter = 72d / 25.4d;
    private const double BezierCircleConstant = 0.5522848d;

    internal static byte[] CreateFile(
        IReadOnlyList<string> pageContents,
        double widthMillimeters,
        double heightMillimeters)
    {
        var width = widthMillimeters * PointsPerMillimeter;
        var height = heightMillimeters * PointsPerMillimeter;
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            string.Empty
        };

        var firstFontObjectNumber = 3 + pageContents.Count * 2;
        var pageObjectNumbers = new List<int>(pageContents.Count);
        for (var index = 0; index < pageContents.Count; index++)
        {
            var pageObjectNumber = objects.Count + 1;
            var contentObjectNumber = pageObjectNumber + 1;
            pageObjectNumbers.Add(pageObjectNumber);
            objects.Add(string.Format(
                CultureInfo.InvariantCulture,
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0:0.###} {1:0.###}] /Resources << /Font << /F1 {2} 0 R /F2 {3} 0 R /F3 {4} 0 R >> >> /Contents {5} 0 R >>",
                width,
                height,
                firstFontObjectNumber,
                firstFontObjectNumber + 1,
                firstFontObjectNumber + 2,
                contentObjectNumber));
            var contentBytes = Encoding.ASCII.GetBytes(pageContents[index]);
            objects.Add($"<< /Length {contentBytes.Length} >>\nstream\n{pageContents[index]}endstream");
        }

        objects[1] = $"<< /Type /Pages /Kids [{string.Join(' ', pageObjectNumbers.Select(number => number + " 0 R"))}] /Count {pageContents.Count} >>";
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Oblique /Encoding /WinAnsiEncoding >>");

        using var output = new MemoryStream();
        using var writer = new StreamWriter(output, Encoding.ASCII, leaveOpen: true);
        writer.Write("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
        writer.Flush();
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(output.Position);
            writer.Write($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
            writer.Flush();
        }

        var xrefOffset = output.Position;
        writer.Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            writer.Write($"{offset:0000000000} 00000 n \n");
        }

        writer.Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        writer.Flush();
        return output.ToArray();
    }

    internal static void DrawRectangle(StringBuilder content, double x, double y, double width, double height) =>
        DrawStrokedRectangle(content, x, y, width, height, new PdfColor(0, 0, 0), 1);

    internal static void DrawFilledRectangle(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        PdfColor fill,
        PdfColor? stroke = null,
        double strokeWidth = 0)
    {
        content.Append("q ");
        AppendFillColor(content, fill);
        if (stroke is { } strokeColor)
        {
            AppendStrokeColor(content, strokeColor);
            content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} w", strokeWidth);
        }

        content.AppendFormat(
            CultureInfo.InvariantCulture,
            " {0:0.###} {1:0.###} {2:0.###} {3:0.###} re {4} Q\n",
            x,
            y,
            width,
            height,
            stroke is null ? "f" : "B");
    }

    internal static void DrawStrokedRectangle(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        PdfColor stroke,
        double strokeWidth = 1)
    {
        content.Append("q ");
        AppendStrokeColor(content, stroke);
        content.AppendFormat(
            CultureInfo.InvariantCulture,
            " {0:0.###} w {1:0.###} {2:0.###} {3:0.###} {4:0.###} re S Q\n",
            strokeWidth,
            x,
            y,
            width,
            height);
    }

    internal static void DrawRoundedRectangle(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        double radius,
        PdfColor fill,
        PdfColor? stroke = null,
        double strokeWidth = 0)
    {
        var r = Math.Min(Math.Max(0, radius), Math.Min(width, height) / 2);
        var k = r * BezierCircleConstant;
        content.Append("q ");
        AppendFillColor(content, fill);
        if (stroke is { } strokeColor)
        {
            AppendStrokeColor(content, strokeColor);
            content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} w", strokeWidth);
        }

        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} m", x + r, y);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} l", x + width - r, y);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} c", x + width - r + k, y, x + width, y + r - k, x + width, y + r);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} l", x + width, y + height - r);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} c", x + width, y + height - r + k, x + width - r + k, y + height, x + width - r, y + height);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} l", x + r, y + height);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} c", x + r - k, y + height, x, y + height - r + k, x, y + height - r);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} l", x, y + r);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} c h {6}\n",
            x,
            y + r - k,
            x + r - k,
            y,
            x + r,
            y,
            stroke is null ? "f Q" : "B Q");
    }

    internal static void DrawRoundedRectangleOutline(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        double radius,
        PdfColor stroke,
        double strokeWidth = 1)
    {
        var r = Math.Min(Math.Max(0, radius), Math.Min(width, height) / 2);
        var k = r * BezierCircleConstant;
        content.Append("q ");
        AppendStrokeColor(content, stroke);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} w", strokeWidth);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} m", x + r, y);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} l", x + width - r, y);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} c", x + width - r + k, y, x + width, y + r - k, x + width, y + r);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} l", x + width, y + height - r);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} c", x + width, y + height - r + k, x + width - r + k, y + height, x + width - r, y + height);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} l", x + r, y + height);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} c", x + r - k, y + height, x, y + height - r + k, x, y + height - r);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} l", x, y + r);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} c S Q\n",
            x,
            y + r - k,
            x + r - k,
            y,
            x + r,
            y);
    }

    internal static void DrawLine(
        StringBuilder content,
        double x1,
        double y1,
        double x2,
        double y2,
        PdfColor color,
        double width = 1,
        bool dashed = false)
    {
        content.Append("q ");
        AppendStrokeColor(content, color);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} w", width);
        if (dashed)
        {
            content.Append(" [3 2] 0 d");
        }

        content.AppendFormat(
            CultureInfo.InvariantCulture,
            " {0:0.###} {1:0.###} m {2:0.###} {3:0.###} l S Q\n",
            x1,
            y1,
            x2,
            y2);
    }

    internal static void DrawCircle(
        StringBuilder content,
        double centerX,
        double centerY,
        double radius,
        PdfColor fill,
        PdfColor? stroke = null,
        double strokeWidth = 0) =>
        DrawEllipse(content, centerX, centerY, radius, radius, fill, stroke, strokeWidth);

    internal static void DrawEllipse(
        StringBuilder content,
        double centerX,
        double centerY,
        double radiusX,
        double radiusY,
        PdfColor fill,
        PdfColor? stroke = null,
        double strokeWidth = 0)
    {
        var kx = radiusX * BezierCircleConstant;
        var ky = radiusY * BezierCircleConstant;
        content.Append("q ");
        AppendFillColor(content, fill);
        if (stroke is { } strokeColor)
        {
            AppendStrokeColor(content, strokeColor);
            content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} w", strokeWidth);
        }

        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} m", centerX + radiusX, centerY);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} c", centerX + radiusX, centerY + ky, centerX + kx, centerY + radiusY, centerX, centerY + radiusY);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} c", centerX - kx, centerY + radiusY, centerX - radiusX, centerY + ky, centerX - radiusX, centerY);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} c", centerX - radiusX, centerY - ky, centerX - kx, centerY - radiusY, centerX, centerY - radiusY);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} c h {6}\n",
            centerX + kx,
            centerY - radiusY,
            centerX + radiusX,
            centerY - ky,
            centerX + radiusX,
            centerY,
            stroke is null ? "f Q" : "B Q");
    }

    internal static void DrawEllipseOutline(
        StringBuilder content,
        double centerX,
        double centerY,
        double radiusX,
        double radiusY,
        PdfColor stroke,
        double strokeWidth = 1)
    {
        content.Append("q ");
        AppendStrokeColor(content, stroke);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} w ", strokeWidth);
        AppendEllipsePath(content, centerX, centerY, radiusX, radiusY);
        content.Append(" S Q\n");
    }

    internal static void DrawStar(
        StringBuilder content,
        double centerX,
        double centerY,
        double outerRadius,
        double innerRadius,
        PdfColor fill,
        PdfColor? stroke = null,
        double strokeWidth = 0)
    {
        var points = new List<(double X, double Y)>(10);
        for (var index = 0; index < 10; index++)
        {
            var angle = (-Math.PI / 2) + (index * Math.PI / 5);
            var radius = index % 2 == 0 ? outerRadius : innerRadius;
            points.Add((
                centerX + (Math.Cos(angle) * radius),
                centerY + (Math.Sin(angle) * radius)));
        }

        DrawPolygon(content, points, fill, stroke, strokeWidth);
    }

    internal static void DrawPolygon(
        StringBuilder content,
        IReadOnlyList<(double X, double Y)> points,
        PdfColor fill,
        PdfColor? stroke = null,
        double strokeWidth = 0)
    {
        if (points is null || points.Count < 3)
        {
            throw new ArgumentException("A polygon requires at least three points.", nameof(points));
        }

        content.Append("q ");
        AppendFillColor(content, fill);
        if (stroke is { } strokeColor)
        {
            AppendStrokeColor(content, strokeColor);
            content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} w", strokeWidth);
        }

        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} m", points[0].X, points[0].Y);
        for (var index = 1; index < points.Count; index++)
        {
            content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} l", points[index].X, points[index].Y);
        }

        content.Append(stroke is null ? " h f Q\n" : " h B Q\n");
    }

    internal static void DrawLeaf(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        PdfColor color,
        bool mirrored = false)
    {
        var direction = mirrored ? -1 : 1;
        var tipX = x + width * direction;
        var baseX = x;
        var controlX = x + width * 0.52 * direction;
        content.Append("q ");
        AppendFillColor(content, color);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} m", baseX, y);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} c", controlX, y + height * 0.08, tipX, y + height * 0.62, tipX, y + height);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} c h f Q\n", tipX - width * 0.22 * direction, y + height * 0.96, baseX + width * 0.16 * direction, y + height * 0.75, baseX, y);
    }

    internal static void DrawBrandLockup(
        StringBuilder content,
        double x,
        double y,
        double scale,
        PdfColor color,
        bool compact = false)
    {
        DrawLeaf(content, x, y + (scale * 0.22), scale * 0.42, scale * 0.72, color);
        DrawLeaf(content, x + (scale * 0.2), y, scale * 0.28, scale * 0.54, color, mirrored: true);
        DrawLine(content, x + (scale * 0.06), y + (scale * 0.14), x + (scale * 0.46), y + (scale * 0.86), color, Math.Max(0.5, scale * 0.035));
        DrawTextColoredBold(content, x + (scale * 0.58), y + (scale * 0.46), scale * (compact ? 0.43 : 0.48), "Criatorio Virtual", color, maxWidth: scale * 4.5);
        if (!compact)
        {
            DrawTextColored(content, x + (scale * 0.58), y + (scale * 0.12), scale * 0.22, "Mais que um registro, uma historia que vive.", color, scale * 4.8);
        }
    }

    internal static void DrawText(
        StringBuilder content,
        double x,
        double y,
        double fontSize,
        string value,
        double? maxWidth = null,
        string fontResource = "F1")
    {
        var text = ToPdfAscii(value);
        if (maxWidth is not null)
        {
            text = TrimToWidth(text, maxWidth.Value, fontSize);
        }

        var bytes = Encoding.ASCII.GetBytes(text);
        content.AppendFormat(
            CultureInfo.InvariantCulture,
            "BT /{0} {1:0.###} Tf 1 0 0 1 {2:0.###} {3:0.###} Tm <{4}> Tj ET\n",
            fontResource,
            fontSize,
            x,
            y,
            Convert.ToHexString(bytes));
    }

    internal static void DrawTextBold(
        StringBuilder content,
        double x,
        double y,
        double fontSize,
        string value,
        double? maxWidth = null) =>
        DrawText(content, x, y, fontSize, value, maxWidth, "F2");

    internal static void DrawTextItalic(
        StringBuilder content,
        double x,
        double y,
        double fontSize,
        string value,
        double? maxWidth = null) =>
        DrawText(content, x, y, fontSize, value, maxWidth, "F3");

    internal static void DrawTextColored(
        StringBuilder content,
        double x,
        double y,
        double fontSize,
        string value,
        PdfColor color,
        double? maxWidth = null)
    {
        content.Append("q ");
        AppendFillColor(content, color);
        DrawText(content, x, y, fontSize, value, maxWidth);
        content.Append("Q\n");
    }

    internal static void DrawTextColoredBold(
        StringBuilder content,
        double x,
        double y,
        double fontSize,
        string value,
        PdfColor color,
        double? maxWidth = null)
    {
        content.Append("q ");
        AppendFillColor(content, color);
        DrawTextBold(content, x, y, fontSize, value, maxWidth);
        content.Append("Q\n");
    }

    internal static void DrawTextCentered(
        StringBuilder content,
        double centerX,
        double y,
        double fontSize,
        string value,
        bool bold = false,
        double? maxWidth = null)
    {
        var printable = ToPdfAscii(value);
        var width = Math.Min(maxWidth ?? double.MaxValue, printable.Length * fontSize * 0.52);
        if (bold)
        {
            DrawTextBold(content, centerX - (width / 2), y, fontSize, value, maxWidth);
        }
        else
        {
            DrawText(content, centerX - (width / 2), y, fontSize, value, maxWidth);
        }
    }

    internal static void DrawTextCenteredColored(
        StringBuilder content,
        double centerX,
        double y,
        double fontSize,
        string value,
        PdfColor color,
        bool bold = false,
        double? maxWidth = null)
    {
        var printable = ToPdfAscii(value);
        var width = Math.Min(maxWidth ?? double.MaxValue, printable.Length * fontSize * 0.52);
        if (bold)
        {
            DrawTextColoredBold(content, centerX - (width / 2), y, fontSize, value, color, maxWidth);
        }
        else
        {
            DrawTextColored(content, centerX - (width / 2), y, fontSize, value, color, maxWidth);
        }
    }

    internal static void DrawTextRight(
        StringBuilder content,
        double rightX,
        double y,
        double fontSize,
        string value,
        bool bold = false,
        double? maxWidth = null)
    {
        var printable = ToPdfAscii(value);
        var width = Math.Min(maxWidth ?? double.MaxValue, printable.Length * fontSize * 0.52);
        if (bold)
        {
            DrawTextBold(content, rightX - width, y, fontSize, value, maxWidth);
        }
        else
        {
            DrawText(content, rightX - width, y, fontSize, value, maxWidth);
        }
    }

    internal static bool TryDrawImage(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        string contentType,
        byte[] bytes,
        bool cover = false,
        bool circleClip = false,
        PdfColor? transparentBackground = null)
    {
        if (string.IsNullOrWhiteSpace(contentType) || bytes is null || bytes.Length == 0)
        {
            return false;
        }

        PdfImage image;
        try
        {
            image = contentType.Trim().ToLowerInvariant() switch
            {
                "image/jpeg" or "image/jpg" => CreateJpegImage(bytes),
                "image/png" => CreatePngImage(bytes, transparentBackground ?? new PdfColor(1, 1, 1)),
                _ => default
            };
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException)
        {
            return false;
        }

        if (image.Width <= 0 || image.Height <= 0 || image.Data.Length == 0)
        {
            return false;
        }

        var fitted = cover || circleClip
            ? FitCover(image.Width, image.Height, width, height)
            : FitInside(image.Width, image.Height, width, height);
        content.Append("q ");
        if (circleClip)
        {
            AppendEllipsePath(
                content,
                x + (width / 2),
                y + (height / 2),
                width / 2,
                height / 2);
            content.Append(" W n ");
        }
        else if (cover)
        {
            content.AppendFormat(
                CultureInfo.InvariantCulture,
                "{0:0.###} {1:0.###} {2:0.###} {3:0.###} re W n ",
                x,
                y,
                width,
                height);
        }

        content.AppendFormat(
            CultureInfo.InvariantCulture,
            "1 0 0 1 {0:0.###} {1:0.###} cm {2:0.###} 0 0 {3:0.###} 0 0 cm BI /W {4} /H {5} /CS /RGB /BPC 8 /Filter [/ASCIIHexDecode {6}] ID\n",
            x + ((width - fitted.Width) / 2),
            y + ((height - fitted.Height) / 2),
            fitted.Width,
            fitted.Height,
            image.Width,
            image.Height,
            image.Filter);
        content.Append(Convert.ToHexString(image.Data));
        content.Append(">\nEI Q\n");
        return true;
    }

    private static (double Width, double Height) FitInside(
        int imageWidth,
        int imageHeight,
        double boxWidth,
        double boxHeight)
    {
        var scale = Math.Min(boxWidth / imageWidth, boxHeight / imageHeight);
        return (imageWidth * scale, imageHeight * scale);
    }

    private static (double Width, double Height) FitCover(
        int imageWidth,
        int imageHeight,
        double boxWidth,
        double boxHeight)
    {
        var scale = Math.Max(boxWidth / imageWidth, boxHeight / imageHeight);
        return (imageWidth * scale, imageHeight * scale);
    }

    private static void AppendEllipsePath(
        StringBuilder content,
        double centerX,
        double centerY,
        double radiusX,
        double radiusY)
    {
        var kx = radiusX * BezierCircleConstant;
        var ky = radiusY * BezierCircleConstant;
        content.AppendFormat(CultureInfo.InvariantCulture, "{0:0.###} {1:0.###} m", centerX + radiusX, centerY);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} c", centerX + radiusX, centerY + ky, centerX + kx, centerY + radiusY, centerX, centerY + radiusY);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} c", centerX - kx, centerY + radiusY, centerX - radiusX, centerY + ky, centerX - radiusX, centerY);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} c", centerX - radiusX, centerY - ky, centerX - kx, centerY - radiusY, centerX, centerY - radiusY);
        content.AppendFormat(CultureInfo.InvariantCulture, " {0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} c h", centerX + kx, centerY - radiusY, centerX + radiusX, centerY - ky, centerX + radiusX, centerY);
    }

    private static PdfImage CreateJpegImage(byte[] bytes)
    {
        if (!TryGetJpegDimensions(bytes, out var width, out var height))
        {
            throw new InvalidDataException("The JPEG dimensions are invalid.");
        }

        return new PdfImage(width, height, "/DCTDecode", bytes);
    }

    private static PdfImage CreatePngImage(byte[] bytes, PdfColor transparentBackground)
    {
        const int signatureLength = 8;
        if (bytes.Length < signatureLength ||
            !bytes.AsSpan(0, signatureLength).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            throw new InvalidDataException("The PNG signature is invalid.");
        }

        var offset = signatureLength;
        var width = 0;
        var height = 0;
        var colorType = 0;
        using var compressed = new MemoryStream();
        while (offset + 12 <= bytes.Length)
        {
            var length = ReadBigEndianInt32(bytes, offset);
            if (length < 0 || offset + 12L + length > bytes.Length)
            {
                throw new InvalidDataException("The PNG chunk length is invalid.");
            }

            var chunkType = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            var dataStart = offset + 8;
            switch (chunkType)
            {
                case "IHDR" when length >= 13:
                    width = ReadBigEndianInt32(bytes, dataStart);
                    height = ReadBigEndianInt32(bytes, dataStart + 4);
                    var bitDepth = bytes[dataStart + 8];
                    colorType = bytes[dataStart + 9];
                    if (width <= 0 || height <= 0 || width > 4000 || height > 4000 || bitDepth != 8 || bytes[dataStart + 10] != 0 || bytes[dataStart + 11] != 0 || bytes[dataStart + 12] != 0)
                    {
                        throw new InvalidDataException("The PNG format is not supported.");
                    }

                    break;
                case "IDAT":
                    compressed.Write(bytes, dataStart, length);
                    break;
                case "IEND":
                    offset = bytes.Length;
                    continue;
            }

            offset += length + 12;
        }

        if (width <= 0 || height <= 0 || compressed.Length == 0 || colorType is not (0 or 2 or 4 or 6))
        {
            throw new InvalidDataException("The PNG image is incomplete or unsupported.");
        }

        var channels = colorType switch
        {
            0 => 1,
            2 => 3,
            4 => 2,
            6 => 4,
            _ => 0
        };
        var scanlineLength = checked(width * channels);
        var rawLength = checked((scanlineLength + 1) * height);
        var raw = new byte[rawLength];
        compressed.Position = 0;
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true))
        {
            ReadExactly(zlib, raw);
        }

        var rgb = new byte[checked(width * height * 3)];
        var previous = new byte[scanlineLength];
        var current = new byte[scanlineLength];
        for (var row = 0; row < height; row++)
        {
            var rawOffset = row * (scanlineLength + 1);
            var filter = raw[rawOffset];
            raw.AsSpan(rawOffset + 1, scanlineLength).CopyTo(current);
            ApplyPngFilter(current, previous, filter, channels);
            for (var column = 0; column < width; column++)
            {
                var source = column * channels;
                var target = (row * width + column) * 3;
                var alpha = colorType is 4 or 6 ? current[source + channels - 1] : (byte)255;
                var red = current[source];
                var green = colorType is 2 or 6 ? current[source + 1] : red;
                var blue = colorType is 2 or 6 ? current[source + 2] : red;
                rgb[target] = Blend(red, alpha, ToByte(transparentBackground.Red));
                rgb[target + 1] = Blend(green, alpha, ToByte(transparentBackground.Green));
                rgb[target + 2] = Blend(blue, alpha, ToByte(transparentBackground.Blue));
            }

            (current, previous) = (previous, current);
        }

        using var encoded = new MemoryStream();
        using (var zlib = new ZLibStream(encoded, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(rgb);
        }

        return new PdfImage(width, height, "/FlateDecode", encoded.ToArray());
    }

    private static void ApplyPngFilter(byte[] row, byte[] previous, byte filter, int bytesPerPixel)
    {
        if (filter > 4)
        {
            throw new InvalidDataException("The PNG filter is invalid.");
        }

        for (var index = 0; index < row.Length; index++)
        {
            var left = index >= bytesPerPixel ? row[index - bytesPerPixel] : (byte)0;
            var above = previous[index];
            var upperLeft = index >= bytesPerPixel ? previous[index - bytesPerPixel] : (byte)0;
            row[index] = filter switch
            {
                0 => row[index],
                1 => unchecked((byte)(row[index] + left)),
                2 => unchecked((byte)(row[index] + above)),
                3 => unchecked((byte)(row[index] + ((left + above) / 2))),
                4 => unchecked((byte)(row[index] + Paeth(left, above, upperLeft))),
                _ => row[index]
            };
        }
    }

    private static byte Paeth(byte left, byte above, byte upperLeft)
    {
        var estimate = left + above - upperLeft;
        var leftDistance = Math.Abs(estimate - left);
        var aboveDistance = Math.Abs(estimate - above);
        var upperLeftDistance = Math.Abs(estimate - upperLeft);
        return leftDistance <= aboveDistance && leftDistance <= upperLeftDistance
            ? left
            : aboveDistance <= upperLeftDistance ? above : upperLeft;
    }

    private static byte Blend(byte value, byte alpha, byte background) =>
        (byte)((value * alpha + background * (255 - alpha)) / 255);

    private static byte ToByte(double value) =>
        (byte)Math.Clamp(Math.Round(value * 255), 0, 255);

    private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
        checked((bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3]);

    private static void ReadExactly(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0)
            {
                throw new InvalidDataException("The image stream ended before all pixels were decoded.");
            }

            offset += read;
        }
    }

    private static bool TryGetJpegDimensions(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
        {
            return false;
        }

        var offset = 2;
        while (offset + 3 < bytes.Length)
        {
            while (offset < bytes.Length && bytes[offset] != 0xFF)
            {
                offset++;
            }

            while (offset < bytes.Length && bytes[offset] == 0xFF)
            {
                offset++;
            }

            if (offset >= bytes.Length)
            {
                break;
            }

            var marker = bytes[offset++];
            if (marker is 0xD8 or 0xD9 or >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            if (offset + 1 >= bytes.Length)
            {
                break;
            }

            var segmentLength = (bytes[offset] << 8) | bytes[offset + 1];
            if (segmentLength < 2 || offset + segmentLength > bytes.Length)
            {
                break;
            }

            var isStartOfFrame = marker is >= 0xC0 and <= 0xC3 or >= 0xC5 and <= 0xC7 or >= 0xC9 and <= 0xCB or >= 0xCD and <= 0xCF;
            if (isStartOfFrame && segmentLength >= 7)
            {
                height = (bytes[offset + 3] << 8) | bytes[offset + 4];
                width = (bytes[offset + 5] << 8) | bytes[offset + 6];
                return width > 0 && height > 0;
            }

            offset += segmentLength;
        }

        return false;
    }

    private static void AppendFillColor(StringBuilder content, PdfColor color) =>
        content.Append(color.ToPdf()).Append(" rg ");

    private static void AppendStrokeColor(StringBuilder content, PdfColor color) =>
        content.Append(color.ToPdf()).Append(" RG ");

    private static string TrimToWidth(string value, double maxWidth, double fontSize)
    {
        var maxCharacters = Math.Max(1, (int)(maxWidth / Math.Max(1.5, fontSize * 0.52)));
        return value.Length <= maxCharacters ? value : value[..Math.Max(1, maxCharacters - 1)] + "...";
    }

    private static string ToPdfAscii(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (char.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character <= 127 ? character : '?');
            }
        }

        return builder.ToString();
    }
}
