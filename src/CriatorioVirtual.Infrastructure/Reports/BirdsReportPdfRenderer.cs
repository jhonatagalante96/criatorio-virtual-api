using System.Globalization;
using System.Text;
using CriatorioVirtual.Application.Reports;
using CriatorioVirtual.Domain.Birds;
using static CriatorioVirtual.Infrastructure.Documents.PdfDocumentPrimitives;

namespace CriatorioVirtual.Infrastructure.Reports;

public sealed class BirdsReportPdfRenderer : IBirdsReportRenderer
{
    private const double PageWidthMillimeters = 210d;
    private const double PageHeightMillimeters = 297d;
    private const int LinesPerPage = 34;

    public Task<RenderedBirdsReport> RenderAsync(
        BirdsReportResult report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        cancellationToken.ThrowIfCancellationRequested();

        var lines = BuildLines(report);
        var pageLines = lines
            .Chunk(LinesPerPage)
            .Select(chunk => chunk.ToArray())
            .ToArray();
        if (pageLines.Length == 0)
        {
            pageLines = [[]];
        }

        var pages = pageLines
            .Select((page, index) => CreatePage(report, page, index > 0))
            .ToArray();
        var content = CreateFile(pages, PageWidthMillimeters, PageHeightMillimeters);

        return Task.FromResult(new RenderedBirdsReport(
            content,
            "birds-report.pdf",
            "application/pdf",
            pages.Length,
            PageWidthMillimeters,
            PageHeightMillimeters));
    }

    private static IReadOnlyCollection<ReportLine> BuildLines(BirdsReportResult report)
    {
        var lines = new List<ReportLine>();
        foreach (var group in report.Groups)
        {
            lines.Add(new ReportLine(
                $"{GetClassificationLabel(group.Classification)} - {GetSexLabel(group.Sex)}",
                11,
                IsHeading: true));
            lines.Add(new ReportLine($"Group total: {group.Items.Count}", 9, IsHeading: false));
            if (group.Items.Count == 0)
            {
                lines.Add(new ReportLine("No birds in this group.", 9, IsHeading: false));
                continue;
            }

            foreach (var item in group.Items)
            {
                lines.Add(new ReportLine(FormatBirdIdentity(item), 8, IsHeading: false));
                lines.Add(new ReportLine(FormatBirdDetails(item), 8, IsHeading: false));
            }
        }

        return lines;
    }

    private static string CreatePage(
        BirdsReportResult report,
        IReadOnlyCollection<ReportLine> lines,
        bool continuation)
    {
        var width = PageWidthMillimeters * PointsPerMillimeter;
        var height = PageHeightMillimeters * PointsPerMillimeter;
        var margin = 42d;
        var content = new StringBuilder();
        DrawRectangle(content, margin / 2, margin / 2, width - margin, height - margin);
        DrawText(content, margin, height - margin - 18, 18, "Criatorio Virtual");
        DrawText(
            content,
            margin,
            height - margin - 43,
            14,
            continuation ? "Registered birds report - continued" : "Registered birds report");
        DrawText(content, margin, height - margin - 64, 9, $"Breeding farm: {report.BreedingFarmName}", width - (2 * margin));
        DrawText(content, margin, height - margin - 80, 8, $"Generated: {report.GeneratedAtUtc:dd/MM/yyyy HH:mm 'UTC'}", width - (2 * margin));
        DrawText(content, margin, height - margin - 96, 9, $"Total birds: {report.TotalCount}", width - (2 * margin));

        var y = height - margin - 122;
        foreach (var line in lines)
        {
            if (y < margin + 18)
            {
                break;
            }

            DrawText(content, margin + (line.IsHeading ? 0 : 8), y, line.FontSize, line.Text, width - (2 * margin) - (line.IsHeading ? 0 : 8));
            y -= line.IsHeading ? 19 : 15;
        }

        return content.ToString();
    }

    private static string FormatBirdIdentity(BirdReportItemResult item)
    {
        var species = string.Equals(item.SpeciesPopularName, item.SpeciesScientificName, StringComparison.OrdinalIgnoreCase)
            ? item.SpeciesPopularName
            : $"{item.SpeciesPopularName} ({item.SpeciesScientificName})";
        return string.Join(
            " | ",
            $"Name: {item.Name}",
            $"Ring: {item.RingNumber ?? "Not informed"}",
            $"Species: {species}");
    }

    private static string FormatBirdDetails(BirdReportItemResult item) => string.Join(
            " | ",
            $"Sex: {GetSexLabel(item.Sex)}",
            $"Birth: {item.BirthDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Not informed"}",
            $"Status: {item.Status}");

    private static string GetClassificationLabel(BirdReportClassification classification) => classification switch
    {
        BirdReportClassification.Matrix => "Matrices",
        BirdReportClassification.Offspring => "Offspring",
        _ => throw new ArgumentOutOfRangeException(nameof(classification), "The report classification is invalid.")
    };

    private static string GetSexLabel(BirdSex sex) => sex switch
    {
        BirdSex.Male => "Male",
        BirdSex.Female => "Female",
        BirdSex.Unknown => "Unidentified",
        _ => throw new ArgumentOutOfRangeException(nameof(sex), "The bird sex is invalid.")
    };

    private sealed record ReportLine(string Text, double FontSize, bool IsHeading);
}
