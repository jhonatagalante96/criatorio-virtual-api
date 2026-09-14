using System.Globalization;
using System.Text;
using CriatorioVirtual.Application.Reports;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Infrastructure.Documents;
using static CriatorioVirtual.Infrastructure.Documents.PdfDocumentPrimitives;

namespace CriatorioVirtual.Infrastructure.Reports;

/// <summary>
/// Builds a print-friendly plantel report with a compact dashboard header and
/// readable two-column group cards.
/// </summary>
public sealed class BirdsReportPdfRenderer : IBirdsReportRenderer
{
    private const double PageWidthMillimeters = 210d;
    private const double PageHeightMillimeters = 297d;
    private const int MaximumRowsPerSection = 18;
    private const double PageMargin = 28d;
    private const double ColumnGap = 12d;
    private const double SectionRowHeight = 25d;

    private static readonly PdfColor Ink = new(0.08, 0.18, 0.16);
    private static readonly PdfColor DeepForest = new(0.04, 0.22, 0.18);
    private static readonly PdfColor Forest = new(0.08, 0.36, 0.29);
    private static readonly PdfColor Sage = new(0.43, 0.65, 0.56);
    private static readonly PdfColor Mint = new(0.82, 0.92, 0.88);
    private static readonly PdfColor Cloud = new(0.96, 0.98, 0.97);
    private static readonly PdfColor Paper = new(0.99, 0.995, 0.98);
    private static readonly PdfColor White = new(1, 1, 1);
    private static readonly PdfColor Muted = new(0.36, 0.44, 0.42);
    private static readonly PdfColor Gold = new(0.78, 0.57, 0.18);
    private static readonly PdfColor GoldLight = new(0.96, 0.88, 0.61);
    private static readonly PdfColor Coral = new(0.79, 0.35, 0.27);
    private static readonly PdfColor Line = new(0.78, 0.86, 0.83);

    public Task<RenderedBirdsReport> RenderAsync(
        BirdsReportResult report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        cancellationToken.ThrowIfCancellationRequested();

        var sections = BuildSections(report);
        var layouts = PackSections(sections);
        var pages = layouts
            .Select((layout, index) => CreatePage(report, layout, index + 1, layouts.Count))
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

    private static IReadOnlyList<ReportSection> BuildSections(BirdsReportResult report)
    {
        var sections = new List<ReportSection>();
        foreach (var group in report.Groups)
        {
            if (group.Items.Count == 0)
            {
                sections.Add(new ReportSection(group.Classification, group.Sex, [], group.Items.Count, false));
                continue;
            }

            var chunks = group.Items.Chunk(MaximumRowsPerSection).ToArray();
            for (var index = 0; index < chunks.Length; index++)
            {
                sections.Add(new ReportSection(
                    group.Classification,
                    group.Sex,
                    chunks[index],
                    group.Items.Count,
                    index > 0));
            }
        }

        return sections;
    }

    private static IReadOnlyList<ReportPageLayout> PackSections(IReadOnlyList<ReportSection> sections)
    {
        var pages = new List<ReportPageLayout>();
        var current = new ReportPageLayout();
        var availableHeight = PageHeightMillimeters * PointsPerMillimeter - 166;
        foreach (var section in sections)
        {
            var sectionHeight = GetSectionHeight(section);
            var firstColumn = current.LeftHeight <= current.RightHeight ? current.Left : current.Right;
            var firstHeight = ReferenceEquals(firstColumn, current.Left) ? current.LeftHeight : current.RightHeight;
            var secondColumn = ReferenceEquals(firstColumn, current.Left) ? current.Right : current.Left;
            var secondHeight = ReferenceEquals(firstColumn, current.Left) ? current.RightHeight : current.LeftHeight;
            if (firstHeight + sectionHeight <= availableHeight)
            {
                firstColumn.Add(section);
                if (ReferenceEquals(firstColumn, current.Left))
                {
                    current.LeftHeight += sectionHeight;
                }
                else
                {
                    current.RightHeight += sectionHeight;
                }
            }
            else if (secondHeight + sectionHeight <= availableHeight)
            {
                secondColumn.Add(section);
                if (ReferenceEquals(secondColumn, current.Left))
                {
                    current.LeftHeight += sectionHeight;
                }
                else
                {
                    current.RightHeight += sectionHeight;
                }
            }
            else
            {
                if (current.Left.Count > 0 || current.Right.Count > 0)
                {
                    pages.Add(current);
                }

                current = new ReportPageLayout();
                current.Left.Add(section);
                current.LeftHeight = sectionHeight;
            }
        }

        if (current.Left.Count > 0 || current.Right.Count > 0 || pages.Count == 0)
        {
            pages.Add(current);
        }

        return pages;
    }

    private static double GetSectionHeight(ReportSection section) =>
        42 + (Math.Max(1, section.Items.Count) * SectionRowHeight);

    private static string CreatePage(
        BirdsReportResult report,
        ReportPageLayout layout,
        int pageNumber,
        int pageCount)
    {
        var width = PageWidthMillimeters * PointsPerMillimeter;
        var height = PageHeightMillimeters * PointsPerMillimeter;
        var content = new StringBuilder();
        DrawFilledRectangle(content, 0, 0, width, height, Paper);
        DrawFilledRectangle(content, 0, height - 5, width, 5, Forest);
        DrawFilledRectangle(content, 18, height - 18 - 83, width - 36, 83, DeepForest);
        DrawBrandLockup(content, PageMargin, height - 73, 24, White);
        DrawTextColoredBold(content, 208, height - 47, 17, "Relatorio do plantel", White, width - 238);
        DrawTextColored(content, 208, height - 66, 7.5, $"Relatorio das aves cadastradas | Total de aves: {report.TotalCount}", Mint, width - 238);
        DrawTextColored(content, 208, height - 78, 6.5, $"Criatorio: {report.BreedingFarmName}", new PdfColor(0.75, 0.84, 0.8), width - 238);

        DrawReportStat(content, PageMargin, height - 133, 126, 38, "Total de aves", report.TotalCount.ToString(CultureInfo.InvariantCulture), Forest);
        DrawReportStat(content, PageMargin + 136, height - 133, 126, 38, "Matrizes", GetTotal(report, BirdReportClassification.Matrix).ToString(CultureInfo.InvariantCulture), Sage);
        DrawReportStat(content, PageMargin + 272, height - 133, 126, 38, "Filhotes", GetTotal(report, BirdReportClassification.Offspring).ToString(CultureInfo.InvariantCulture), Gold);
        DrawReportStat(content, PageMargin + 408, height - 133, width - PageMargin - 408, 38, "Gerado em", report.GeneratedAtUtc.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), Coral);

        DrawTextColoredBold(content, PageMargin, height - 158, 7.5, "Plantel por classificacao e sexo", Ink, width - (2 * PageMargin));
        var columnWidth = (width - (2 * PageMargin) - ColumnGap) / 2;
        var columnTop = height - 173;
        DrawSectionColumn(content, layout.Left, PageMargin, columnTop, columnWidth);
        DrawSectionColumn(content, layout.Right, PageMargin + columnWidth + ColumnGap, columnTop, columnWidth);

        DrawLine(content, PageMargin, 42, width - PageMargin, 42, Mint, 0.8);
        DrawBrandLockup(content, PageMargin, 20, 12, Forest, compact: true);
        DrawTextColored(content, 174, 23, 6.2, "Identidade, organizacao e paixao pelo mundo das aves.", Muted, width - 270);
        DrawTextRight(content, width - PageMargin, 23, 6.3, $"Pagina {pageNumber} de {pageCount}", true, 100);
        return content.ToString();
    }

    private static void DrawReportStat(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        string label,
        string value,
        PdfColor accent)
    {
        DrawRoundedRectangle(content, x, y, width, height, 6, White, Line, 0.7);
        DrawFilledRectangle(content, x, y, 4, height, accent);
        DrawTextColored(content, x + 12, y + height - 14, 5.5, label.ToUpperInvariant(), Muted, width - 18);
        DrawTextColoredBold(content, x + 12, y + 9, 12, value, Ink, width - 18);
    }

    private static void DrawSectionColumn(
        StringBuilder content,
        IReadOnlyCollection<ReportSection> sections,
        double x,
        double top,
        double width)
    {
        var cursor = top;
        foreach (var section in sections)
        {
            var height = GetSectionHeight(section);
            cursor -= height;
            DrawReportSection(content, section, x, cursor, width, height);
            cursor -= 10;
        }
    }

    private static void DrawReportSection(
        StringBuilder content,
        ReportSection section,
        double x,
        double y,
        double width,
        double height)
    {
        var accent = section.Classification == BirdReportClassification.Matrix ? Forest : Gold;
        var sexAccent = section.Sex switch
        {
            BirdSex.Male => Sage,
            BirdSex.Female => Coral,
            _ => Muted
        };
        DrawRoundedRectangle(content, x, y, width, height, 8, White, Line, 0.7);
        DrawFilledRectangle(content, x, y + height - 31, width, 31, accent);
        DrawCircle(content, x + 14, y + height - 15.5, 5.5, sexAccent);
        var sectionTitle = $"{GetClassificationLabel(section.Classification)} - {GetSexLabel(section.Sex)}";
        if (section.IsContinuation)
        {
            sectionTitle += " (continuacao)";
        }

        DrawTextColoredBold(content, x + 25, y + height - 19, 7.3, sectionTitle, White, width * 0.69);
        DrawTextColoredRight(content, x + width - 9, y + height - 18, 5.4, $"Total do grupo: {section.TotalCount}", White, maxWidth: width * 0.29);

        var rowY = y + height - 36 - SectionRowHeight;
        if (section.Items.Count == 0)
        {
            DrawFilledRectangle(content, x + 6, rowY, width - 12, SectionRowHeight, Cloud);
            DrawTextColored(content, x + 14, rowY + 9, 6.2, "Nenhuma ave neste grupo.", Muted, width - 28);
            return;
        }

        for (var index = 0; index < section.Items.Count; index++)
        {
            var item = section.Items[index];
            var rowX = x + 6;
            var rowWidth = width - 12;
            DrawFilledRectangle(content, rowX, rowY, rowWidth, SectionRowHeight, index % 2 == 0 ? Cloud : Paper);
            DrawTextColoredBold(content, rowX + 7, rowY + 14, 6.3, $"{index + 1}", accent, 14);
            DrawTextColoredBold(content, rowX + 25, rowY + 14, 6.2, item.Name, Ink, rowWidth - 90);
            DrawTextColoredRight(content, rowX + rowWidth - 7, rowY + 14, 5.2, item.RingNumber ?? "Nao informado", Ink, maxWidth: 64);
            var detailLines = FormatBirdDetails(item);
            DrawTextColored(content, rowX + 25, rowY + 8, 4.2, detailLines.First, Muted, rowWidth - 32);
            DrawTextColored(content, rowX + 25, rowY + 3, 4.2, detailLines.Second, Muted, rowWidth - 32);
            rowY -= SectionRowHeight;
        }
    }

    private static int GetTotal(BirdsReportResult report, BirdReportClassification classification) =>
        report.Groups
            .Where(group => group.Classification == classification)
            .Sum(group => group.Items.Count);

    private static (string First, string Second) FormatBirdDetails(BirdReportItemResult item) =>
        ($"Especie: {FormatSpecies(item)} | Sexo: {GetSexLabel(item.Sex)}",
            $"Nascimento: {item.BirthDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Nao informado"} | Status: {GetStatusLabel(item.Status)}");

    private static string FormatSpecies(BirdReportItemResult item) =>
        string.Equals(item.SpeciesPopularName, item.SpeciesScientificName, StringComparison.OrdinalIgnoreCase)
            ? item.SpeciesPopularName
            : $"{item.SpeciesPopularName} ({item.SpeciesScientificName})";

    private static string GetClassificationLabel(BirdReportClassification classification) => classification switch
    {
        BirdReportClassification.Matrix => "Matrizes",
        BirdReportClassification.Offspring => "Filhotes",
        _ => throw new ArgumentOutOfRangeException(nameof(classification), "The report classification is invalid.")
    };

    private static string GetSexLabel(BirdSex sex) => sex switch
    {
        BirdSex.Male => "Macho",
        BirdSex.Female => "Femea",
        BirdSex.Unknown => "Nao informado",
        _ => throw new ArgumentOutOfRangeException(nameof(sex), "The bird sex is invalid.")
    };

    private static string GetStatusLabel(BirdStatus status) => status switch
    {
        BirdStatus.Active => "Ativa",
        BirdStatus.Archived => "Arquivada",
        BirdStatus.Transferred => "Transferida",
        BirdStatus.Deceased => "Falecida",
        BirdStatus.Escaped => "Escapou",
        _ => throw new ArgumentOutOfRangeException(nameof(status), "The bird status is invalid.")
    };

    private static void DrawTextColoredRight(
        StringBuilder content,
        double rightX,
        double y,
        double fontSize,
        string value,
        PdfColor color,
        double? maxWidth = null)
    {
        var printableWidth = value.Normalize(NormalizationForm.FormD)
            .Count(character => char.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark) * fontSize * 0.52;
        var width = Math.Min(maxWidth ?? double.MaxValue, printableWidth);
        DrawTextColored(content, rightX - width, y, fontSize, value, color, maxWidth);
    }

    private sealed class ReportPageLayout
    {
        public List<ReportSection> Left { get; } = [];

        public List<ReportSection> Right { get; } = [];

        public double LeftHeight { get; set; }

        public double RightHeight { get; set; }
    }

    private sealed record ReportSection(
        BirdReportClassification Classification,
        BirdSex Sex,
        IReadOnlyList<BirdReportItemResult> Items,
        int TotalCount,
        bool IsContinuation);
}
