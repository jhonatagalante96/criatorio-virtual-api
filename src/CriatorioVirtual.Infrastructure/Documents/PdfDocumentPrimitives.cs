using System.Globalization;
using System.Text;

namespace CriatorioVirtual.Infrastructure.Documents;

internal static class PdfDocumentPrimitives
{
    internal const double PointsPerMillimeter = 72d / 25.4d;

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

        var pageObjectNumbers = new List<int>(pageContents.Count);
        for (var index = 0; index < pageContents.Count; index++)
        {
            var pageObjectNumber = objects.Count + 1;
            var contentObjectNumber = pageObjectNumber + 1;
            pageObjectNumbers.Add(pageObjectNumber);
            objects.Add(string.Format(
                CultureInfo.InvariantCulture,
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0:0.###} {1:0.###}] /Resources << /Font << /F1 {2} 0 R >> >> /Contents {3} 0 R >>",
                width,
                height,
                3 + (pageContents.Count * 2),
                contentObjectNumber));
            var contentBytes = Encoding.ASCII.GetBytes(pageContents[index]);
            objects.Add($"<< /Length {contentBytes.Length} >>\nstream\n{pageContents[index]}endstream");
        }

        objects[1] = $"<< /Type /Pages /Kids [{string.Join(' ', pageObjectNumbers.Select(number => number + " 0 R"))}] /Count {pageContents.Count} >>";
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

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
        content.AppendFormat(
            CultureInfo.InvariantCulture,
            "q 0 0 0 RG 1 w {0:0.###} {1:0.###} {2:0.###} {3:0.###} re S Q\n",
            x,
            y,
            width,
            height);

    internal static void DrawText(
        StringBuilder content,
        double x,
        double y,
        double fontSize,
        string value,
        double? maxWidth = null)
    {
        var text = ToPdfAscii(value);
        if (maxWidth is not null)
        {
            text = TrimToWidth(text, maxWidth.Value, fontSize);
        }

        var bytes = Encoding.ASCII.GetBytes(text);
        content.AppendFormat(
            CultureInfo.InvariantCulture,
            "BT /F1 {0:0.###} Tf 1 0 0 1 {1:0.###} {2:0.###} Tm <{3}> Tj ET\n",
            fontSize,
            x,
            y,
            Convert.ToHexString(bytes));
    }

    private static string TrimToWidth(string value, double maxWidth, double fontSize)
    {
        var maxCharacters = Math.Max(1, (int)(maxWidth / Math.Max(4, fontSize * 0.52)));
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
