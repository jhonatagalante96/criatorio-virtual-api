using System.Globalization;
using System.Reflection;
using System.Text;
using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.Documents;
using static CriatorioVirtual.Infrastructure.Documents.PdfDocumentPrimitives;

namespace CriatorioVirtual.Infrastructure.Documents;

/// <summary>
/// Renders the authorized document snapshots with the Criatorio Virtual visual system.
/// The renderer is intentionally dependency-free so generated files remain deterministic
/// and easy to assemble into a private batch PDF.
/// </summary>
public sealed class PdfDocumentRenderer : IDocumentRenderer
{
    private static readonly IReadOnlyDictionary<BadgePrintSize, (double Width, double Height)> BadgeSizes =
        new Dictionary<BadgePrintSize, (double Width, double Height)>
        {
            [BadgePrintSize.Small] = (85.60, 53.98),
            [BadgePrintSize.Medium] = (105, 74),
            [BadgePrintSize.Large] = (125, 88)
        };

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
    private static readonly byte[] BrandLogoBytes = LoadEmbeddedAsset(
      "CriatorioVirtual.Infrastructure.Documents.Assets.criatorio-virtual-horizontal.png");
    private static readonly byte[] CriatorioVirtualSymbolPng = LoadEmbeddedLogoAsset();

    public Task<RenderedDocument> RenderAsync(
        DocumentRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Type == BirdDocumentType.GenealogyCertificate)
        {
            const double width = 297d;
            const double height = 210d;
            var certificatePages = CreateGenealogyCertificatePages(
                request.Snapshot,
                width,
                height,
                request.Certificate?.ModelId ?? GenealogyCertificateRenderConfiguration.DefaultModelId);
            return Task.FromResult(CreateRenderedDocument(request.Snapshot, certificatePages, width, height));
        }

        if (request.Type == BirdDocumentType.ProvenanceDocument)
        {
            const double width = 210d;
            const double height = 297d;
            var provenancePages = CreateProvenancePages(request.Snapshot, width, height);
            return Task.FromResult(CreateRenderedDocument(request.Snapshot, provenancePages, width, height));
        }

        var configuration = request.Badge ?? throw new ArgumentException("Badge configuration is required.", nameof(request));
        var dimensions = BadgeSizes[configuration.PrintSize];
        var badgePages = new List<string>
        {
            CreateBadgePage(
                request.Snapshot,
                dimensions.Width,
                dimensions.Height,
                configuration.SelectedFields,
                configuration.ModelId)
        };
        if (configuration.SelectedFields.Contains(DocumentField.GenealogyTree))
        {
            badgePages.Add(CreateBadgeGenealogyPage(request.Snapshot, dimensions.Width, dimensions.Height, configuration.ModelId));
        }

        var content = CreateFile(badgePages, dimensions.Width, dimensions.Height);
        return Task.FromResult(new RenderedDocument(
            content,
            $"bird-{request.Snapshot.BirdId:N}.pdf",
            "application/pdf",
            badgePages.Count,
            dimensions.Width,
            dimensions.Height));
    }

    private static RenderedDocument CreateRenderedDocument(
        BirdDocumentSnapshot snapshot,
        IReadOnlyList<string> pages,
        double width,
        double height)
    {
        return new RenderedDocument(
            CreateFile(pages, width, height),
            $"bird-{snapshot.BirdId:N}.pdf",
            "application/pdf",
            pages.Count,
            width,
            height);
    }

    private static string CreateBadgePage(
        BirdDocumentSnapshot snapshot,
        double widthMillimeters,
        double heightMillimeters,
        IReadOnlyCollection<DocumentField> selectedFields,
        BadgeModelId modelId)
    {
        var width = widthMillimeters * PointsPerMillimeter;
        var height = heightMillimeters * PointsPerMillimeter;
        var content = new StringBuilder();
        DrawFilledRectangle(content, 0, 0, width, height, Cloud);
        DrawRoundedRectangle(content, 1, 1, width - 2, height - 2, 7, Cloud, Line, 0.7);

        switch (modelId)
        {
            case BadgeModelId.Classic:
                DrawClassicBadge(content, snapshot, selectedFields, width, height);
                break;
            case BadgeModelId.Minimalist:
                DrawMinimalistBadge(content, snapshot, selectedFields, width, height);
                break;
            case BadgeModelId.Competition:
                DrawCompetitionBadge(content, snapshot, selectedFields, width, height);
                break;
            case BadgeModelId.Photographic:
                DrawPhotographicBadge(content, snapshot, selectedFields, width, height);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(modelId), "The badge model is invalid.");
        }

        return content.ToString();
    }

    private static void DrawClassicBadge(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        IReadOnlyCollection<DocumentField> fields,
        double width,
        double height)
    {
        var margin = Math.Clamp(width * 0.035, 6, 10);
        var headerHeight = Math.Clamp(height * 0.22, 25, 43);
        var footerHeight = Math.Clamp(height * 0.18, 22, 35);
        DrawRoundedRectangle(content, 1, 1, width - 2, height - 2, 7, White, Line, 0.8);
        DrawRoundedRectangle(content, 1.5, height - headerHeight - 0.5, width - 3, headerHeight, 6, Forest);
        DrawBadgeLogo(content, margin + 3, height - headerHeight + 6, Math.Clamp(headerHeight * 0.55, 13, 22), White);
        DrawTextColoredRight(content, width - margin, height - 13, 5.2, "Cracha de identificacao", White, maxWidth: width * 0.36);

        var bodyY = footerHeight + 5;
        var bodyHeight = height - headerHeight - footerHeight - 9;
        DrawEllipse(content, width * 0.9, bodyY + (bodyHeight * 0.1), width * 0.47, bodyHeight * 0.75, Cloud);
        DrawEllipse(content, width * 0.92, bodyY + (bodyHeight * 0.15), width * 0.38, bodyHeight * 0.55, new PdfColor(0.93, 0.97, 0.95));

        var photoWidth = fields.Contains(DocumentField.BirdPhoto) ? Math.Min(width * 0.35, bodyHeight * 0.84) : 0;
        if (photoWidth > 0)
        {
            DrawBadgePhoto(content, snapshot, margin, bodyY + 3, photoWidth, bodyHeight - 6, Forest);
        }

        var fieldsX = photoWidth > 0 ? margin + photoWidth + 9 : margin;
        var fieldsWidth = width - fieldsX - margin;
        DrawBadgeInfoGrid(content, snapshot, fields, fieldsX, bodyY + 2, fieldsWidth, bodyHeight - 4, Ink);
        DrawClassicFooter(content, width, footerHeight);
    }

    private static void DrawMinimalistBadge(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        IReadOnlyCollection<DocumentField> fields,
        double width,
        double height)
    {
        var margin = Math.Clamp(width * 0.05, 8, 15);
        var footerHeight = Math.Clamp(height * 0.17, 22, 34);
        DrawRoundedRectangle(content, 1, 1, width - 2, height - 2, 7, White, Line, 0.8);
        DrawFilledRectangle(content, 1, height - 4, width - 2, 3, Forest);
        DrawFilledRectangle(content, 1, 1, 4, height - 2, Mint);
        DrawBadgeLogo(content, margin, height - 24, Math.Clamp(height * 0.1, 14, 20), Forest);
        DrawTextColoredRight(content, width - margin, height - 13, 4.7, "Identificacao que conecta geracoes.", Muted, maxWidth: width * 0.39);
        DrawLeaf(content, width - margin - 2, height - 26, 10, 15, Sage, mirrored: true);

        var bodyY = footerHeight + 7;
        var bodyHeight = height - footerHeight - 38;
        var diameter = fields.Contains(DocumentField.BirdPhoto) ? Math.Min(width * 0.33, bodyHeight * 0.9) : 0;
        if (diameter > 0)
        {
            DrawBadgeCircularPhoto(content, snapshot, margin, bodyY + ((bodyHeight - diameter) / 2), diameter, Sage);
        }

        var fieldsX = diameter > 0 ? margin + diameter + 12 : margin;
        DrawBadgeInfoGrid(content, snapshot, fields, fieldsX, bodyY, width - fieldsX - margin, bodyHeight, Ink);
        DrawMinimalistFooter(content, width, footerHeight);
    }

    private static void DrawCompetitionBadge(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        IReadOnlyCollection<DocumentField> fields,
        double width,
        double height)
    {
        var margin = Math.Clamp(width * 0.035, 7, 11);
        var footerHeight = Math.Clamp(height * 0.17, 23, 38);
        DrawFilledRectangle(content, 0, 0, width, height, DeepForest);
        DrawRoundedRectangle(content, 1, 1, width - 2, height - 2, 7, DeepForest, Gold, 1);
        DrawEllipse(content, width * 0.93, height * 0.95, width * 0.42, height * 0.68, new PdfColor(0.08, 0.29, 0.24));
        DrawEllipse(content, width * 0.92, height * 0.96, width * 0.32, height * 0.52, new PdfColor(0.1, 0.34, 0.27));
        DrawStar(content, width * 0.18, height - 21, 10, 4.2, GoldLight);
        DrawTextColoredBold(content, width * 0.52, height - 20, Math.Clamp(height * 0.065, 7, 13), "CRIATORIO VIRTUAL", GoldLight, width * 0.52);
        DrawTextColored(content, width * 0.52, height - 31, Math.Clamp(height * 0.027, 4.2, 6), "EXCELENCIA EM AVES ORNAMENTAIS", GoldLight, width * 0.43);
        DrawLaurelSprig(content, margin + 3, height - 32, GoldLight, mirrored: false);
        DrawLine(content, margin, height - 38, width - margin, height - 38, Gold, 1.1);

        var panelX = margin;
        var panelY = footerHeight + 7;
        var panelWidth = width - (2 * margin);
        var panelHeight = height - footerHeight - 52;
        DrawRoundedRectangle(content, panelX, panelY, panelWidth, panelHeight, 7, Paper, Gold, 1);
        var photoWidth = fields.Contains(DocumentField.BirdPhoto) ? Math.Min(panelWidth * 0.4, panelHeight * 0.9) : 0;
        if (photoWidth > 0)
        {
            DrawBadgePhoto(content, snapshot, panelX + 6, panelY + 6, photoWidth, panelHeight - 12, Forest);
        }

        var fieldsX = photoWidth > 0 ? panelX + photoWidth + 12 : panelX + 8;
        DrawBadgeInfoGrid(
            content,
            snapshot,
            fields,
            fieldsX,
            panelY + 7,
            panelWidth - (fieldsX - panelX) - 14,
            panelHeight - 14,
            Ink,
            labelColor: new PdfColor(0.42, 0.35, 0.21));
        DrawCompetitionSeal(content, width - margin - 16, footerHeight + 15, Math.Clamp(height * 0.09, 12, 23));
        DrawCompetitionFooter(content, width, footerHeight);
    }

    private static void DrawPhotographicBadge(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        IReadOnlyCollection<DocumentField> fields,
        double width,
        double height)
    {
        var margin = Math.Clamp(width * 0.025, 5, 9);
        var footerHeight = Math.Clamp(height * 0.17, 23, 38);
        var bodyY = footerHeight;
        var bodyHeight = height - footerHeight;
        DrawRoundedRectangle(content, 1, 1, width - 2, height - 2, 7, DeepForest, Line, 0.8);
        var photoWidth = fields.Contains(DocumentField.BirdPhoto) ? width : 0;
        if (photoWidth > 0)
        {
            DrawBadgePhoto(content, snapshot, margin, bodyY + 1, width - (2 * margin), bodyHeight - 2, DeepForest);
        }
        else
        {
            DrawFilledRectangle(content, margin, bodyY + 1, width - (2 * margin), bodyHeight - 2, DeepForest);
            DrawLeaf(content, margin + 10, bodyY + bodyHeight * 0.34, 20, 29, Sage);
        }

        DrawBadgeLogo(content, margin + 5, height - 25, Math.Clamp(height * 0.1, 15, 22), White, stacked: true);
        DrawTextColoredRight(content, width - margin - 5, height - 13, 4.6, "A natureza em boa companhia.", White, maxWidth: width * 0.35);

        var panelX = width * 0.5;
        var panelY = bodyY + (bodyHeight * 0.11);
        var panelWidth = width - panelX - margin - 3;
        var panelHeight = bodyHeight * 0.78;
        DrawRoundedRectangle(content, panelX, panelY, panelWidth, panelHeight, 7, new PdfColor(0.98, 0.99, 0.97), new PdfColor(0.8, 0.88, 0.83), 0.8);
        DrawBadgeInfoGrid(content, snapshot, fields, panelX + 8, panelY + 8, panelWidth - 16, panelHeight - 16, Ink, labelColor: Muted);
        DrawPhotographicFooter(content, width, footerHeight);
    }

    private static void DrawBadgePhoto(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double x,
        double y,
        double width,
        double height,
        PdfColor accent)
    {
        DrawRoundedRectangle(content, x, y, width, height, 6, new PdfColor(0.88, 0.94, 0.91), accent, 0.8);
        var drawn = snapshot.Photo is { } photo && TryDrawImage(content, x + 2, y + 2, width - 4, height - 4, photo.ContentType, photo.Content, cover: true);
        if (!drawn)
        {
            DrawLeaf(content, x + (width * 0.26), y + (height * 0.42), width * 0.38, height * 0.34, accent);
            DrawTextCentered(content, x + (width / 2), y + (height * 0.21), 5.2, snapshot.Photo is null ? "Foto indisponivel" : "Visualizacao da foto indisponivel", maxWidth: width - 6);
        }

        DrawRoundedRectangleOutline(content, x, y, width, height, 6, accent, 0.8);
    }

    private static void DrawBadgeCircularPhoto(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double x,
        double y,
        double diameter,
        PdfColor accent)
    {
        var center = x + (diameter / 2);
        var radius = diameter / 2;
        DrawCircle(content, center, y + radius, radius, new PdfColor(0.88, 0.94, 0.91), accent, 0.8);
        var drawn = snapshot.Photo is { } photo && TryDrawImage(content, x, y, diameter, diameter, photo.ContentType, photo.Content, cover: true, circleClip: true);
        if (!drawn)
        {
            DrawLeaf(content, x + (diameter * 0.31), y + (diameter * 0.39), diameter * 0.38, diameter * 0.33, accent);
            DrawTextCentered(content, center, y + (diameter * 0.2), 5.1, snapshot.Photo is null ? "Foto indisponivel" : "Foto indisponivel", maxWidth: diameter - 6);
        }

        DrawEllipseOutline(content, center, y + radius, radius - 0.5, radius - 0.5, accent, 0.8);
    }

    private static void DrawBadgeInfoGrid(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        IReadOnlyCollection<DocumentField> selectedFields,
        double x,
        double y,
        double width,
        double height,
        PdfColor textColor,
        PdfColor? cardFill = null,
        PdfColor? border = null,
        PdfColor? labelColor = null)
    {
        var fields = OrderBadgeFields(selectedFields)
            .Where(field => field is not DocumentField.BirdPhoto and not DocumentField.GenealogyTree)
            .Select(field => GetFieldValue(field, snapshot))
            .ToArray();
        if (fields.Length == 0)
        {
            DrawTextColored(content, x, y + (height / 2), 5.8, "Nenhum campo de identificacao selecionado.", Muted, width);
            return;
        }

        var columns = fields.Length > 1 && width >= 80 ? 2 : 1;
        var rows = (int)Math.Ceiling(fields.Length / (double)columns);
        var gap = Math.Max(4, Math.Min(8, width * 0.035));
        var cellWidth = (width - ((columns - 1) * gap)) / columns;
        var cellHeight = (height - ((rows - 1) * gap)) / rows;
        var effectiveLabelColor = labelColor ?? Muted;
        for (var index = 0; index < fields.Length; index++)
        {
            var column = index % columns;
            var row = index / columns;
            var cellX = x + (column * (cellWidth + gap));
            var cellY = y + height - ((row + 1) * cellHeight) - (row * gap);
            if (cardFill is { } fill)
            {
                DrawRoundedRectangle(content, cellX, cellY, cellWidth, cellHeight, Math.Min(4, cellHeight * 0.2), fill, border ?? Line, 0.55);
            }

            var innerX = cardFill is null ? cellX : cellX + 5;
            var innerWidth = cardFill is null ? cellWidth : cellWidth - 10;
            var labelSize = Math.Clamp(
                Math.Min(cellHeight * 0.16, innerWidth / (Math.Max(1, fields[index].Label.Length) * 0.52)),
                3.5,
                6.2);
            var valueSize = Math.Clamp(
                Math.Min(cellHeight * 0.25, innerWidth / (Math.Max(1, fields[index].Value.Length) * 0.52)),
                4.6,
                9.1);
            DrawTextColored(content, innerX, cellY + cellHeight - labelSize - 2, labelSize, fields[index].Label, effectiveLabelColor, innerWidth);
            DrawTextColoredBold(content, innerX, cellY + Math.Max(3, cellHeight * 0.16), valueSize, fields[index].Value, textColor, innerWidth);
        }
    }

    private static void DrawClassicFooter(StringBuilder content, double width, double height)
    {
        DrawFilledRectangle(content, 1, 1, width - 2, height, White);
        DrawLine(content, 8, height, width - 8, height, Mint, 0.65);
        DrawBadgeLogo(content, 10, height * 0.42, Math.Clamp(height * 0.42, 10, 16), Forest);
        DrawTextColored(content, 10, Math.Max(3, height * 0.12), 4.2, "Qualidade em cada geracao.", Muted, width * 0.42);
        DrawLeaf(content, width - 23, 7, 13, 18, Sage, mirrored: true);
    }

    private static void DrawMinimalistFooter(StringBuilder content, double width, double height)
    {
        DrawFilledRectangle(content, 1, 1, width - 2, height, White);
        DrawLine(content, 10, height, width - 10, height, Mint, 0.65);
        DrawBadgeLogo(content, 10, height * 0.42, Math.Clamp(height * 0.42, 10, 15), Forest);
        DrawTextColored(content, 10, Math.Max(3, height * 0.12), 4.1, "Passaro por voce, hoje e sempre.", Muted, width * 0.46);
        DrawLeaf(content, width - 23, 7, 13, 18, Sage, mirrored: true);
    }

    private static void DrawCompetitionFooter(StringBuilder content, double width, double height)
    {
        DrawFilledRectangle(content, 0, 0, width, height, DeepForest);
        DrawLine(content, 9, height - 1, width - 9, height - 1, Gold, 0.9);
        DrawTextColoredBold(content, width * 0.28, height * 0.47, Math.Clamp(height * 0.31, 7, 12), "Criatorio Virtual", GoldLight, width * 0.44);
        DrawTextColored(content, width * 0.29, height * 0.16, Math.Clamp(height * 0.13, 4.2, 6), "Tradicao em todas as geracoes", GoldLight, width * 0.62);
    }

    private static void DrawPhotographicFooter(StringBuilder content, double width, double height)
    {
        DrawFilledRectangle(content, 0, 0, width, height, DeepForest);
        DrawBadgeLogo(content, 10, height * 0.42, Math.Clamp(height * 0.42, 10, 16), White);
        DrawTextColoredRight(content, width - 10, height * 0.22, Math.Clamp(height * 0.16, 4.4, 6.3), "Paixao que gera vida.", GoldLight, maxWidth: width * 0.42);
    }

    private static void DrawBadgeLogo(
        StringBuilder content,
        double x,
        double y,
        double height,
        PdfColor color,
        bool stacked = false)
    {
        var iconWidth = height * 0.55;
        DrawLeaf(content, x, y + (height * 0.16), iconWidth * 0.75, height * 0.75, color);
        DrawLeaf(content, x + (iconWidth * 0.36), y, iconWidth * 0.54, height * 0.55, color, mirrored: true);
        DrawLine(content, x + (iconWidth * 0.1), y + (height * 0.12), x + (iconWidth * 0.72), y + (height * 0.84), color, Math.Max(0.45, height * 0.035));
        var textX = x + iconWidth + 3;
        var textSize = Math.Clamp(height * (stacked ? 0.38 : 0.43), 5.2, 11.5);
        if (stacked)
        {
            DrawTextColoredBold(content, textX, y + (height * 0.48), textSize, "Criatorio", color, height * 4.6);
            DrawTextColoredBold(content, textX, y + (height * 0.08), textSize, "Virtual", color, height * 4.6);
        }
        else
        {
            DrawTextColoredBold(content, textX, y + (height * 0.31), textSize, "Criatorio Virtual", color, height * 5.2);
        }
    }

    private static void DrawCompetitionSeal(StringBuilder content, double centerX, double centerY, double radius)
    {
        DrawCircle(content, centerX, centerY, radius, GoldLight, Gold, 1);
        DrawCircle(content, centerX, centerY, radius - 3, new PdfColor(0.97, 0.91, 0.69), Gold, 0.5);
        DrawTextCentered(content, centerX, centerY + 2, Math.Clamp(radius * 0.36, 4.3, 7), "Qualidade", bold: true, maxWidth: radius * 1.65);
        DrawTextCentered(content, centerX, centerY - (radius * 0.42), Math.Clamp(radius * 0.3, 4.1, 6.2), "que inspira", bold: true, maxWidth: radius * 1.65);
    }

    private static void DrawLaurelSprig(StringBuilder content, double x, double y, PdfColor color, bool mirrored)
    {
        var direction = mirrored ? -1 : 1;
        for (var index = 0; index < 4; index++)
        {
            var leafX = x + (index * 5 * direction);
            var leafY = y - (index * 2.2);
            DrawLeaf(content, leafX, leafY, 5.5, 9, color, mirrored: direction < 0);
        }
    }

    private static string CreateBadgeGenealogyPage(
        BirdDocumentSnapshot snapshot,
        double widthMillimeters,
        double heightMillimeters,
        BadgeModelId modelId)
    {
        var width = widthMillimeters * PointsPerMillimeter;
        var height = heightMillimeters * PointsPerMillimeter;
        return modelId switch
        {
            BadgeModelId.Classic => CreateClassicBadgeGenealogyPage(snapshot, width, height),
            BadgeModelId.Minimalist => CreateMinimalistBadgeGenealogyPage(snapshot, width, height),
            BadgeModelId.Competition => CreateCompetitionBadgeGenealogyPage(snapshot, width, height),
            BadgeModelId.Photographic => CreatePhotographicBadgeGenealogyPage(snapshot, width, height),
            _ => throw new ArgumentOutOfRangeException(nameof(modelId), "The badge model is invalid.")
        };
    }

    private static string CreateClassicBadgeGenealogyPage(BirdDocumentSnapshot snapshot, double width, double height)
    {
        var content = new StringBuilder();
        var margin = Math.Clamp(width * 0.035, 6, 10);
        var headerHeight = Math.Clamp(height * 0.2, 24, 38);
        var footerHeight = Math.Clamp(height * 0.16, 20, 31);
        DrawRoundedRectangle(content, 1, 1, width - 2, height - 2, 7, White, Line, 0.8);
        DrawTextColoredBold(content, margin, height - 19, Math.Clamp(height * 0.065, 7, 11), "Arvore genealogica", Ink, width * 0.55);
        DrawLeaf(content, width - margin - 14, height - 25, 13, 19, Sage, mirrored: true);
        DrawBadgeGenealogyCards(content, snapshot, snapshot.Genealogy, margin, footerHeight + 8, width - (2 * margin), height - headerHeight - footerHeight, Forest, White, Line, Ink, Muted);
        DrawTextColoredRight(content, width - margin, Math.Max(4, footerHeight * 0.25), 4.2, "Linhas que perpetuam a vida.", Muted, width * 0.47);
        return content.ToString();
    }

    private static string CreateMinimalistBadgeGenealogyPage(BirdDocumentSnapshot snapshot, double width, double height)
    {
        var content = new StringBuilder();
        var margin = Math.Clamp(width * 0.05, 8, 15);
        var headerHeight = Math.Clamp(height * 0.2, 25, 38);
        var footerHeight = Math.Clamp(height * 0.16, 20, 31);
        DrawRoundedRectangle(content, 1, 1, width - 2, height - 2, 7, White, Line, 0.8);
        DrawFilledRectangle(content, 1, height - 4, width - 2, 3, Forest);
        DrawTextColoredBold(content, margin, height - 19, Math.Clamp(height * 0.06, 7, 10.5), "Arvore genealogica", Ink, width * 0.55);
        DrawTextColoredRight(content, width - margin, height - 18, 4.1, "Tradicao que gera o futuro.", Muted, width * 0.34);
        DrawBadgeGenealogyCards(content, snapshot, snapshot.Genealogy, margin, footerHeight + 8, width - (2 * margin), height - headerHeight - footerHeight, Sage, White, Line, Ink, Muted);
        DrawBadgeLogo(content, margin, 6, Math.Clamp(footerHeight * 0.45, 10, 15), Forest);
        DrawTextColoredRight(content, width - margin, 9, 4.1, "A genetica e a base de grandes historias.", Muted, width * 0.53);
        return content.ToString();
    }

    private static string CreateCompetitionBadgeGenealogyPage(BirdDocumentSnapshot snapshot, double width, double height)
    {
        var content = new StringBuilder();
        var margin = Math.Clamp(width * 0.035, 7, 11);
        var headerHeight = Math.Clamp(height * 0.2, 25, 39);
        var footerHeight = Math.Clamp(height * 0.16, 21, 33);
        DrawRoundedRectangle(content, 1, 1, width - 2, height - 2, 7, DeepForest, Gold, 1);
        DrawFilledRectangle(content, 1, height - headerHeight, width - 2, headerHeight, DeepForest);
        DrawCrown(content, margin + 12, height - 19, 14, 10, GoldLight);
        DrawTextColoredBold(content, margin + 28, height - 20, Math.Clamp(height * 0.065, 7, 11), "Arvore genealogica", GoldLight, width * 0.6);
        DrawTextColoredRight(content, width - margin, height - 31, 4.2, "Grandes aves deixam grandes historias.", GoldLight, width * 0.43);
        DrawLine(content, margin, height - headerHeight, width - margin, height - headerHeight, Gold, 0.9);
        DrawBadgeGenealogyCards(content, snapshot, snapshot.Genealogy, margin, footerHeight + 8, width - (2 * margin), height - headerHeight - footerHeight, Gold, Paper, Gold, Ink, Muted);
        DrawCompetitionFooter(content, width, footerHeight);
        return content.ToString();
    }

    private static string CreatePhotographicBadgeGenealogyPage(BirdDocumentSnapshot snapshot, double width, double height)
    {
        var content = new StringBuilder();
        var margin = Math.Clamp(width * 0.035, 7, 11);
        var headerHeight = Math.Clamp(height * 0.2, 25, 38);
        var footerHeight = Math.Clamp(height * 0.16, 21, 33);
        DrawRoundedRectangle(content, 1, 1, width - 2, height - 2, 7, White, Line, 0.8);
        DrawTextColoredBold(content, margin, height - 19, Math.Clamp(height * 0.065, 7, 11), "Arvore genealogica", Ink, width * 0.52);
        DrawTextColoredRight(content, width - margin, height - 18, 4.2, "Mesma origem. Novos horizontes.", Muted, width * 0.38);
        DrawLeaf(content, width - margin - 12, height - 30, 11, 17, Sage, mirrored: true);
        DrawBadgeGenealogyCards(content, snapshot, snapshot.Genealogy, margin, footerHeight + 8, width - (2 * margin), height - headerHeight - footerHeight, Sage, White, Line, Ink, Muted);
        DrawBadgeLogo(content, margin, 6, Math.Clamp(footerHeight * 0.45, 10, 15), Forest);
        DrawTextColoredRight(content, width - margin, 9, 4.1, "Genetica hoje, mais vida amanha.", Muted, width * 0.43);
        return content.ToString();
    }

    private static void DrawBadgeGenealogyCards(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        IReadOnlyCollection<GenealogySnapshotNode> nodes,
        double x,
        double y,
        double width,
        double height,
        PdfColor accent,
        PdfColor cardFill,
        PdfColor border,
        PdfColor textColor,
        PdfColor labelColor)
    {
        var allNodes = nodes.ToArray();
        if (allNodes.Length == 0)
        {
            DrawTextColored(content, x, y + (height / 2), 6.4, "Nenhum ancestral registrado na genealogia autorizada.", labelColor, width);
            return;
        }

        var cardGap = Math.Clamp(height * 0.045, 4, 8);
        var parentWidth = Math.Clamp(width * 0.27, 58, width * 0.32);
        var ancestorWidth = (width - parentWidth - (cardGap * 2)) / 2;
        if (ancestorWidth < 35)
        {
            parentWidth = width * 0.25;
            ancestorWidth = (width - parentWidth - (cardGap * 2)) / 2;
        }

        var parentHeight = Math.Min(42, (height - cardGap) / 2);
        var ancestorHeight = Math.Min(35, (height - cardGap) / 2);
        var parentNodes = allNodes
            .Where(node => GetNodeGeneration(node) == 1)
            .OrderBy(GetGenealogyPositionOrder)
            .Take(2)
            .ToArray();
        var ancestorNodes = allNodes
            .Where(node => GetNodeGeneration(node) == 2)
            .OrderBy(GetGenealogyPositionOrder)
            .Take(4)
            .ToArray();
        var parentBoxes = new List<BadgeNodeBox>(parentNodes.Length);
        var ancestorBoxes = new List<BadgeNodeBox>(ancestorNodes.Length);

        var parentGroupHeight = (2 * parentHeight) + cardGap;
        var ancestorGroupHeight = (2 * ancestorHeight) + cardGap;
        var groupHeight = Math.Max(parentGroupHeight, ancestorGroupHeight);
        var treeBottom = Math.Max(y, (height - groupHeight) / 2);
        var parentBottom = treeBottom + ((groupHeight - parentGroupHeight) / 2);
        for (var index = 0; index < parentNodes.Length; index++)
        {
            var parentY = parentNodes.Length == 1
                ? parentBottom + ((parentGroupHeight - parentHeight) / 2)
                : parentBottom + ((parentNodes.Length - 1 - index) * (parentHeight + cardGap));
            parentBoxes.Add(new BadgeNodeBox(parentNodes[index], x, parentY, parentWidth, parentHeight));
        }

        var ancestorX = x + parentWidth + cardGap;
        var ancestorBottom = treeBottom + ((groupHeight - ancestorGroupHeight) / 2);
        var branches = new[] { "father", "mother" };
        for (var branchIndex = 0; branchIndex < branches.Length; branchIndex++)
        {
            var branch = branches[branchIndex];
            var branchNodes = ancestorNodes
                .Where(node => node.Position.StartsWith(branch + ".", StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            var branchY = ancestorBottom + ((branches.Length - 1 - branchIndex) * (ancestorHeight + cardGap));
            for (var index = 0; index < branchNodes.Length; index++)
            {
                var ancestorXForNode = ancestorX + (index * (ancestorWidth + cardGap));
                ancestorBoxes.Add(new BadgeNodeBox(branchNodes[index], ancestorXForNode, branchY, ancestorWidth, ancestorHeight));
            }
        }

        foreach (var parent in parentBoxes)
        {
            var branch = parent.Node.Position.Split('.', StringSplitOptions.RemoveEmptyEntries)[0];
            var branchAncestors = ancestorBoxes
                .Where(box => box.Node.Position.StartsWith(branch + ".", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (branchAncestors.Length == 0)
            {
                continue;
            }

            var junctionX = parent.X + parent.Width + (cardGap / 2);
            DrawLine(content, parent.X + parent.Width, parent.CenterY, junctionX, parent.CenterY, accent, 0.7);
            foreach (var ancestor in branchAncestors)
            {
                DrawLine(content, junctionX, parent.CenterY, junctionX, ancestor.CenterY, accent, 0.7);
                DrawLine(content, junctionX, ancestor.CenterY, ancestor.X, ancestor.CenterY, accent, 0.7);
            }
        }

        foreach (var box in parentBoxes.Concat(ancestorBoxes))
        {
            DrawBadgeAncestorCard(content, snapshot, box.Node, box.X, box.Y, box.Width, box.Height, accent, cardFill, border, textColor, labelColor);
        }

        var visibleCount = parentBoxes.Count + ancestorBoxes.Count;
        var omitted = allNodes.Length - visibleCount;
        if (omitted > 0)
        {
            DrawTextColoredRight(content, x + width, y + 1, 4.2, $"+{omitted} ancestrais", labelColor, width * 0.35);
        }
    }

    private static void DrawBadgeAncestorCard(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        GenealogySnapshotNode node,
        double x,
        double y,
        double width,
        double height,
        PdfColor accent,
        PdfColor cardFill,
        PdfColor border,
        PdfColor textColor,
        PdfColor labelColor)
    {
        DrawRoundedRectangle(content, x, y, width, height, Math.Min(5, height * 0.16), cardFill, border, 0.65);
        var thumbSize = Math.Min(height - 8, width * 0.27);
        var thumbX = x + 4;
        var thumbY = y + ((height - thumbSize) / 2);
        DrawRoundedRectangle(content, thumbX, thumbY, thumbSize, thumbSize, 3, new PdfColor(0.91, 0.95, 0.92), accent, 0.45);
        var drawn = snapshot.Photo is { } photo && TryDrawImage(content, thumbX + 1, thumbY + 1, thumbSize - 2, thumbSize - 2, photo.ContentType, photo.Content, cover: true);
        if (!drawn)
        {
            DrawLeaf(content, thumbX + (thumbSize * 0.22), thumbY + (thumbSize * 0.3), thumbSize * 0.48, thumbSize * 0.43, accent);
        }

        DrawRoundedRectangleOutline(content, thumbX, thumbY, thumbSize, thumbSize, 3, accent, 0.45);
        var textX = thumbX + thumbSize + 5;
        var textWidth = Math.Max(15, width - (textX - x) - 5);
        var labelSize = Math.Clamp(height * 0.18, 4, 6.2);
        var nameSize = Math.Clamp(height * 0.25, 5.1, 8.5);
        DrawTextColored(content, textX, y + height - labelSize - 3, labelSize, GetPositionLabel(node.Position), labelColor, textWidth);
        DrawTextColoredBold(content, textX, y + height - labelSize - nameSize - 7, nameSize, string.IsNullOrWhiteSpace(node.Name) ? "Nao informado" : node.Name, textColor, textWidth);
        var ring = string.IsNullOrWhiteSpace(node.RingNumber) ? "Anilha nao informada" : $"Anilha {node.RingNumber}";
        DrawTextColored(content, textX, y + 5, Math.Clamp(height * 0.17, 4, 5.8), ring, labelColor, textWidth);
    }

    private static void DrawCrown(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        PdfColor color)
    {
        DrawPolygon(
            content,
            [
                (x, y),
                (x + (width * 0.15), y + height),
                (x + (width * 0.38), y + (height * 0.5)),
                (x + (width * 0.58), y + height),
                (x + (width * 0.82), y + (height * 0.5)),
                (x + width, y + height),
                (x + (width * 0.9), y),
            ],
            color);
        DrawLine(content, x, y, x + width, y, color, Math.Max(0.5, height * 0.08));
    }

    private static IReadOnlyCollection<DocumentField> OrderBadgeFields(IReadOnlyCollection<DocumentField> selectedFields)
    {
        var preferredOrder = new[]
        {
            DocumentField.Name,
            DocumentField.Sex,
            DocumentField.RingNumber,
            DocumentField.Species,
            DocumentField.BirthDate,
            DocumentField.BreedingFarmName
        };

        return preferredOrder
            .Where(selectedFields.Contains)
            .Concat(selectedFields.Where(field => !preferredOrder.Contains(field)))
            .ToArray();
    }

    private static int GetGenealogyPositionOrder(GenealogySnapshotNode node)
    {
        var position = node.Position.ToLowerInvariant();
        return position switch
        {
            "father" => 0,
            "mother" => 1,
            "father.father" => 2,
            "father.mother" => 3,
            "mother.father" => 4,
            "mother.mother" => 5,
            _ => 10 + position.Length
        };
    }

    private static IReadOnlyList<string> CreateGenealogyCertificatePages(
        BirdDocumentSnapshot snapshot,
        double widthMillimeters,
        double heightMillimeters,
        GenealogyCertificateModelId modelId)
    {
        return
        [
            CreateGenealogyCertificatePage(snapshot, widthMillimeters, heightMillimeters, modelId)
        ];
    }

    private static string CreateGenealogyCertificatePage(
        BirdDocumentSnapshot snapshot,
        double widthMillimeters,
        double heightMillimeters,
        GenealogyCertificateModelId modelId)
    {
        var width = widthMillimeters * PointsPerMillimeter;
        var height = heightMillimeters * PointsPerMillimeter;
        return modelId switch
        {
            GenealogyCertificateModelId.ClassicPremium => CreateClassicPremiumCertificatePage(snapshot, width, height),
            GenealogyCertificateModelId.Institutional => CreateInstitutionalCertificatePage(snapshot, width, height),
            GenealogyCertificateModelId.Modern => CreateModernCertificatePage(snapshot, width, height),
            _ => throw new ArgumentOutOfRangeException(nameof(modelId), "The genealogy certificate model is invalid.")
        };
    }

    private static CertificatePalette GetCertificatePalette(GenealogyCertificateModelId modelId) => modelId switch
    {
        GenealogyCertificateModelId.ClassicPremium => new CertificatePalette(
            IsDark: true,
            Background: new PdfColor(0.025, 0.11, 0.09),
            HeaderFill: new PdfColor(0.035, 0.15, 0.12),
            Surface: new PdfColor(0.045, 0.17, 0.13),
            TreeFill: new PdfColor(0.035, 0.14, 0.11),
            ColumnFill: new PdfColor(0.06, 0.21, 0.16),
            Text: White,
            NodeText: Ink,
            Muted: new PdfColor(0.72, 0.79, 0.72),
            Primary: GoldLight,
            Secondary: Gold,
            Border: new PdfColor(0.62, 0.47, 0.20),
            Connector: new PdfColor(0.88, 0.69, 0.31),
            MaleFill: new PdfColor(0.88, 0.95, 0.86),
            MaleBorder: new PdfColor(0.48, 0.73, 0.48),
            MaleAccent: new PdfColor(0.08, 0.37, 0.80),
            FemaleFill: new PdfColor(1, 0.82, 0.82),
            FemaleBorder: new PdfColor(0.93, 0.43, 0.47),
            FemaleAccent: new PdfColor(0.86, 0.05, 0.18),
            UnknownFill: new PdfColor(0.82, 0.86, 0.80),
            UnknownBorder: new PdfColor(0.55, 0.62, 0.54),
            UnknownAccent: new PdfColor(0.30, 0.36, 0.32),
            FooterFill: new PdfColor(0.02, 0.10, 0.08),
            FooterText: White,
            FooterMuted: new PdfColor(0.71, 0.77, 0.70),
            FooterLine: Gold,
            Watermark: new PdfColor(0.04, 0.18, 0.14),
            LogoEmblemFill: new PdfColor(0.02, 0.10, 0.08),
            LogoBird: new PdfColor(0.015, 0.025, 0.023),
            LogoChest: new PdfColor(0.70, 0.28, 0.08)),
        GenealogyCertificateModelId.Institutional => new CertificatePalette(
            IsDark: false,
            Background: Paper,
            HeaderFill: Paper,
            Surface: White,
            TreeFill: new PdfColor(0.995, 0.998, 0.995),
            ColumnFill: new PdfColor(0.92, 0.95, 0.93),
            Text: Ink,
            NodeText: Ink,
            Muted: new PdfColor(0.36, 0.44, 0.42),
            Primary: DeepForest,
            Secondary: Gold,
            Border: new PdfColor(0.76, 0.83, 0.79),
            Connector: new PdfColor(0.63, 0.43, 0.10),
            MaleFill: new PdfColor(0.89, 0.96, 0.87),
            MaleBorder: new PdfColor(0.44, 0.68, 0.40),
            MaleAccent: new PdfColor(0.04, 0.37, 0.76),
            FemaleFill: new PdfColor(1, 0.89, 0.89),
            FemaleBorder: new PdfColor(0.94, 0.47, 0.53),
            FemaleAccent: new PdfColor(0.88, 0.06, 0.18),
            UnknownFill: new PdfColor(0.96, 0.97, 0.95),
            UnknownBorder: new PdfColor(0.79, 0.84, 0.81),
            UnknownAccent: new PdfColor(0.36, 0.44, 0.42),
            FooterFill: new PdfColor(0.985, 0.99, 0.985),
            FooterText: Ink,
            FooterMuted: Muted,
            FooterLine: new PdfColor(0.83, 0.67, 0.29),
            Watermark: new PdfColor(0.93, 0.96, 0.94),
            LogoEmblemFill: White,
            LogoBird: new PdfColor(0.015, 0.025, 0.023),
            LogoChest: new PdfColor(0.70, 0.28, 0.08)),
        GenealogyCertificateModelId.Modern => new CertificatePalette(
            IsDark: false,
            Background: White,
            HeaderFill: White,
            Surface: new PdfColor(0.985, 0.99, 0.985),
            TreeFill: White,
            ColumnFill: new PdfColor(0.91, 0.94, 0.93),
            Text: Ink,
            NodeText: Ink,
            Muted: new PdfColor(0.38, 0.46, 0.50),
            Primary: DeepForest,
            Secondary: Gold,
            Border: new PdfColor(0.78, 0.84, 0.82),
            Connector: new PdfColor(0.25, 0.48, 0.32),
            MaleFill: new PdfColor(0.84, 0.93, 1),
            MaleBorder: new PdfColor(0.19, 0.57, 0.94),
            MaleAccent: new PdfColor(0.03, 0.34, 0.82),
            FemaleFill: new PdfColor(1, 0.87, 0.89),
            FemaleBorder: new PdfColor(0.96, 0.36, 0.48),
            FemaleAccent: new PdfColor(0.88, 0.02, 0.17),
            UnknownFill: new PdfColor(0.95, 0.96, 0.96),
            UnknownBorder: new PdfColor(0.72, 0.78, 0.78),
            UnknownAccent: new PdfColor(0.35, 0.43, 0.44),
            FooterFill: DeepForest,
            FooterText: White,
            FooterMuted: new PdfColor(0.78, 0.86, 0.82),
            FooterLine: Gold,
            Watermark: new PdfColor(0.94, 0.97, 0.95),
            LogoEmblemFill: White,
            LogoBird: new PdfColor(0.015, 0.025, 0.023),
            LogoChest: new PdfColor(0.70, 0.28, 0.08)),
        _ => throw new ArgumentOutOfRangeException(nameof(modelId), "The genealogy certificate model is invalid.")
    };

    private static string CreateClassicPremiumCertificatePage(
        BirdDocumentSnapshot snapshot,
        double width,
        double height)
    {
        var content = new StringBuilder();
        var palette = GetCertificatePalette(GenealogyCertificateModelId.ClassicPremium);

        DrawClassicPremiumCanvas(content, width, height, palette);
        DrawClassicPremiumHeader(content, snapshot, width, height, palette);
        DrawClassicPremiumIdentity(content, snapshot, 23, 61, 218, 424, palette);
        DrawClassicPremiumTree(content, snapshot, 255, 61, width - 278, 424, palette);
        DrawClassicPremiumFooter(content, snapshot, width, 54, palette);
        return content.ToString();
    }

    private static string CreateInstitutionalCertificatePage(
        BirdDocumentSnapshot snapshot,
        double width,
        double height)
    {
        var content = new StringBuilder();
        var palette = GetCertificatePalette(GenealogyCertificateModelId.Institutional);

        DrawInstitutionalCanvas(content, width, height, palette);
        DrawInstitutionalHeader(content, snapshot, width, height, palette);
        DrawInstitutionalIdentity(content, snapshot, 24, 73, 211, 407, palette);
        DrawInstitutionalTree(content, snapshot, 249, 66, width - 270, 420, palette);
        DrawInstitutionalFooter(content, snapshot, width, 52, palette);
        return content.ToString();
    }

    private static string CreateModernCertificatePage(
        BirdDocumentSnapshot snapshot,
        double width,
        double height)
    {
        var content = new StringBuilder();
        var palette = GetCertificatePalette(GenealogyCertificateModelId.Modern);

        DrawModernCanvas(content, width, height, palette);
        DrawModernHeader(content, snapshot, width, height, palette);
        DrawModernIdentity(content, snapshot, 24, 65, 184, 431, palette);
        DrawModernTree(content, snapshot, 226, 65, width - 250, 431, palette);
        DrawModernFooter(content, snapshot, width, 47, palette);
        return content.ToString();
    }

    private static void DrawClassicPremiumCanvas(
        StringBuilder content,
        double width,
        double height,
        CertificatePalette palette)
    {
        DrawFilledRectangle(content, 0, 0, width, height, palette.Background);
        DrawEllipse(content, width * 0.72, height * 0.52, width * 0.37, height * 0.30, new PdfColor(0.03, 0.16, 0.13));
        DrawEllipse(content, width * 0.73, height * 0.52, width * 0.27, height * 0.22, new PdfColor(0.035, 0.19, 0.15));
        DrawStrokedRectangle(content, 7, 7, width - 14, height - 14, palette.Secondary, 1.25);
        DrawStrokedRectangle(content, 12, 12, width - 24, height - 24, palette.Border, 0.65);
        DrawStrokedRectangle(content, 17, 17, width - 34, height - 34, palette.Secondary, 0.35);

        DrawLeaf(content, 18, height - 63, 24, 43, palette.Watermark);
        DrawLeaf(content, 42, height - 42, 18, 31, palette.Watermark, mirrored: true);
        DrawLeaf(content, width - 18, 18, 24, 43, palette.Watermark, mirrored: true);
        DrawLeaf(content, width - 42, 18, 18, 31, palette.Watermark);
        DrawLeaf(content, 29, 29, 21, 35, palette.Watermark);
        DrawLeaf(content, width - 29, height - 64, 21, 35, palette.Watermark, mirrored: true);

        DrawLine(content, 12, height - 28, 31, height - 12, palette.Secondary, 0.8);
        DrawLine(content, 12, height - 12, 31, height - 28, palette.Secondary, 0.8);
        DrawLine(content, width - 12, height - 28, width - 31, height - 12, palette.Secondary, 0.8);
        DrawLine(content, width - 12, height - 12, width - 31, height - 28, palette.Secondary, 0.8);
        DrawLine(content, 12, 28, 31, 12, palette.Secondary, 0.8);
        DrawLine(content, 12, 12, 31, 28, palette.Secondary, 0.8);
        DrawLine(content, width - 12, 28, width - 31, 12, palette.Secondary, 0.8);
        DrawLine(content, width - 12, 12, width - 31, 28, palette.Secondary, 0.8);
    }

    private static void DrawClassicPremiumHeader(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double width,
        double height,
        CertificatePalette palette)
    {
        DrawFilledRectangle(content, 20, height - 108, width - 40, 87, palette.HeaderFill);
        DrawCertificateLogo(
            content,
            30,
            height - 92,
            195,
            72,
            palette.Text,
            palette.Secondary,
            palette.Surface,
            palette.LogoBird,
            palette.LogoChest);
        DrawLine(content, 238, height - 29, 238, height - 92, palette.Secondary, 0.8);
        DrawCertificateFarmBlock(content, snapshot, 253, height - 37, 178, palette, compact: false);

        var titleX = 448d;
        DrawTextColoredRight(content, width - 25, height - 31, 5.2, "AVES  -  GENETICA  -  RESULTADOS", palette.Muted, 185);
        DrawTextColoredBold(content, titleX, height - 52, 19.2, "CERTIFICADO DE GENEALOGIA", palette.Primary, width - titleX - 20, "F5");
        DrawLaurelSprig(content, titleX + 10, height - 76, palette.Secondary, mirrored: false);
        DrawLine(content, titleX + 28, height - 77, width - 72, height - 77, palette.Secondary, 0.65);
        DrawLine(content, titleX + 28, height - 81, width - 72, height - 81, palette.Border, 0.3);
        DrawLaurelSprig(content, width - 44, height - 76, palette.Secondary, mirrored: true);
        DrawTextCenteredColored(
            content,
            titleX + ((width - titleX - 20) / 2),
            height - 94,
            7.8,
            "Genetica, manejo e paixao em harmonia.",
            palette.Text,
            fontResource: "F6",
            maxWidth: width - titleX - 42);
        DrawTextColoredRight(content, width - 25, height - 103, 4.5, "CLASSICO PREMIUM", palette.Muted, 100);
        DrawLine(content, 20, height - 110, width - 20, height - 110, palette.Secondary, 0.8);
    }

    private static void DrawClassicPremiumIdentity(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double x,
        double y,
        double width,
        double height,
        CertificatePalette palette)
    {
        DrawRoundedRectangle(content, x, y, width, height, 9, palette.Surface, palette.Secondary, 1.15);
        DrawRoundedRectangle(content, x + 5, y + 5, width - 10, height - 10, 7, palette.Surface, palette.Border, 0.45);

        var top = y + height;
        var tagY = top - 39;
        DrawRoundedRectangle(content, x + 12, tagY, 72, 24, 4, palette.Primary, palette.Primary, 0.5);
        DrawTextColoredBold(content, x + 23, tagY + 8, 8, "AVE", DeepForest, 50, "F5");
        DrawRoundedRectangle(content, x + width - 111, tagY, 99, 24, 4, palette.Surface, palette.Secondary, 0.8);
        DrawTextCenteredColored(content, x + width - 61.5, tagY + 8, 5.7, snapshot.RingNumber is null ? "ANILHA -" : $"ANILHA {snapshot.RingNumber}", palette.Text, bold: true, maxWidth: 91);
        DrawTextColoredBold(content, x + 13, top - 68, 15.5, snapshot.Name, palette.Text, width - 26, "F5");
        DrawLine(content, x + 13, top - 78, x + width - 13, top - 78, palette.Border, 0.5);

        var cursor = top - 96;
        DrawCertificateInfoRow(content, x + 13, ref cursor, width - 26, "Especie", snapshot.Species, palette);
        DrawCertificateInfoRow(content, x + 13, ref cursor, width - 26, "Numero da anilha", snapshot.RingNumber ?? "Nao informado", palette);
        DrawCertificateInfoRow(content, x + 13, ref cursor, width - 26, "Sexo", GetSexLabel(snapshot.Sex), palette, snapshot.Sex);
        DrawCertificateInfoRow(content, x + 13, ref cursor, width - 26, "Nascimento", FormatDate(snapshot.BirthDate), palette);

        var photoBottom = y + 29;
        var photoTop = cursor - 5;
        DrawCertificatePhoto(content, snapshot, x + 12, photoBottom, width - 24, Math.Max(78, photoTop - photoBottom), palette);
        DrawTextCenteredColored(content, x + (width / 2), y + 14, 4.7, "QUALIDADE  -  TRADICAO  -  PRESERVACAO", palette.Muted, maxWidth: width - 25);
    }

    private static void DrawInstitutionalCanvas(
        StringBuilder content,
        double width,
        double height,
        CertificatePalette palette)
    {
        DrawFilledRectangle(content, 0, 0, width, height, palette.Background);
        DrawRoundedRectangle(content, 8, 8, width - 16, height - 16, 8, palette.Background, palette.Primary, 1.05);
        DrawRoundedRectangle(content, 13, 13, width - 26, height - 26, 6, palette.Background, palette.Secondary, 0.45);
        DrawLeaf(content, 18, height - 61, 34, 57, palette.Watermark);
        DrawLeaf(content, 51, height - 45, 25, 42, palette.Watermark, mirrored: true);
        DrawLeaf(content, width - 18, height - 51, 38, 61, palette.Watermark, mirrored: true);
        DrawLeaf(content, width - 53, height - 34, 26, 43, palette.Watermark);
        DrawLeaf(content, 20, 22, 32, 50, palette.Watermark, mirrored: true);
        DrawLeaf(content, width - 20, 22, 31, 48, palette.Watermark);
    }

    private static void DrawInstitutionalHeader(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double width,
        double height,
        CertificatePalette palette)
    {
        DrawCertificateLogo(
            content,
            27,
            height - 89,
            197,
            70,
            palette.Text,
            palette.Secondary,
            White,
            palette.LogoBird,
            palette.LogoChest);
        DrawLine(content, 236, height - 28, 236, height - 91, palette.Border, 0.8);
        DrawCertificateFarmBlock(content, snapshot, 252, height - 36, 174, palette, compact: false);

        var titleX = 442d;
        DrawTextColoredRight(content, width - 26, height - 31, 5.1, "AVES  -  GENETICA  -  RESULTADOS", palette.Muted, 184);
        DrawTextColoredBold(content, titleX, height - 54, 19.4, "CERTIFICADO DE GENEALOGIA", palette.Primary, width - titleX - 18, "F5");
        DrawRoundedRectangle(content, titleX + 41, height - 80, 263, 17, 8.5, new PdfColor(0.96, 0.89, 0.70), palette.Secondary, 0.55);
        DrawTextCenteredColored(content, titleX + 172.5, height - 74, 5.4, "DOCUMENTO INTERNO DO CRIATORIO VIRTUAL", Ink, bold: true, maxWidth: 247);
        DrawTextCenteredColored(content, titleX + 172.5, height - 96, 8.1, "Genetica, manejo e paixao em harmonia.", palette.Primary, fontResource: "F6", maxWidth: 280);
        DrawTextColoredRight(content, width - 26, height - 103, 4.4, "INSTITUCIONAL CLARO", palette.Muted, 105);
        DrawLine(content, 23, height - 109, width - 23, height - 109, palette.Secondary, 0.7);
    }

    private static void DrawInstitutionalIdentity(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double x,
        double y,
        double width,
        double height,
        CertificatePalette palette)
    {
        DrawRoundedRectangle(content, x, y, width, height, 8, palette.Surface, palette.Border, 0.8);
        DrawRoundedRectangle(content, x + 10, y + height - 37, width - 20, 23, 5, palette.Primary, palette.Primary, 0.3);
        DrawTextColoredBold(content, x + 21, y + height - 29, 7.7, "AVE", White, 50, "F5");
        DrawRoundedRectangle(content, x + width - 111, y + height - 37, 101, 23, 5, new PdfColor(0.96, 0.89, 0.70), palette.Secondary, 0.55);
        DrawTextCenteredColored(content, x + width - 60.5, y + height - 29, 5.4, snapshot.RingNumber is null ? "ANILHA -" : $"ANILHA {snapshot.RingNumber}", Ink, bold: true, maxWidth: 93);
        DrawTextColoredBold(content, x + 12, y + height - 64, 15.2, snapshot.Name, palette.Text, width - 24, "F5");

        var cursor = y + height - 84;
        DrawInstitutionalInfoRow(content, x + 12, ref cursor, width - 24, "Especie", snapshot.Species, palette);
        DrawInstitutionalInfoRow(content, x + 12, ref cursor, width - 24, "Anilha", snapshot.RingNumber ?? "Nao informado", palette);
        DrawInstitutionalInfoRow(content, x + 12, ref cursor, width - 24, "Sexo", GetSexLabel(snapshot.Sex), palette, snapshot.Sex);
        DrawInstitutionalInfoRow(content, x + 12, ref cursor, width - 24, "Nascimento", FormatDate(snapshot.BirthDate), palette);

        var photoBottom = y + 21;
        var photoTop = cursor - 6;
        DrawCertificatePhoto(content, snapshot, x + 11, photoBottom, width - 22, Math.Max(78, photoTop - photoBottom), palette);
        DrawTextCenteredColored(content, x + (width / 2), y + 8, 4.6, "IDENTIDADE DO CRIATORIO", palette.Muted, maxWidth: width - 22);
    }

    private static void DrawInstitutionalInfoRow(
        StringBuilder content,
        double x,
        ref double y,
        double width,
        string label,
        string value,
        CertificatePalette palette,
        BirdSex? sex = null)
    {
        DrawTextColored(content, x, y, 4.6, label.ToUpperInvariant(), palette.Muted, width);
        if (sex is { } birdSex)
        {
            DrawSexSymbol(content, x + 5, y - 9, 6.9, birdSex, palette, palette.Surface);
            DrawTextColoredBold(content, x + 15, y - 12, 7.3, value, palette.Text, width - 15);
        }
        else
        {
            DrawTextColoredBold(content, x, y - 12, 7.3, value, palette.Text, width);
        }

        y -= 24;
    }

    private static void DrawModernCanvas(
        StringBuilder content,
        double width,
        double height,
        CertificatePalette palette)
    {
        DrawFilledRectangle(content, 0, 0, width, height, palette.Background);
        DrawStrokedRectangle(content, 8, 8, width - 16, height - 16, palette.Primary, 0.9);
        DrawFilledRectangle(content, 0, height - 79, width, 79, palette.Primary);
        DrawPolygon(
            content,
            [(width - 150, height - 79), (width, height - 79), (width, height - 13), (width - 86, height - 13)],
            new PdfColor(0.08, 0.31, 0.26));
        DrawPolygon(
            content,
            [(0, height - 79), (88, height - 79), (44, height - 13), (0, height - 13)],
            new PdfColor(0.12, 0.39, 0.32));
        DrawLeaf(content, width - 29, height - 19, 22, 35, new PdfColor(0.34, 0.58, 0.47), mirrored: true);
        DrawLeaf(content, width - 56, height - 29, 17, 28, new PdfColor(0.27, 0.50, 0.41), mirrored: true);
        DrawLeaf(content, 25, 31, 20, 33, palette.Watermark);
        DrawLeaf(content, 47, 20, 16, 26, palette.Watermark, mirrored: true);
    }

    private static void DrawModernHeader(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double width,
        double height,
        CertificatePalette palette)
    {
        DrawCertificateLogo(
            content,
            28,
            height - 71,
            191,
            55,
            White,
            GoldLight,
            palette.Primary,
            palette.LogoBird,
            palette.LogoChest);
        DrawLine(content, 236, height - 24, 236, height - 67, new PdfColor(0.42, 0.67, 0.57), 0.55);
        DrawCertificateFarmBlock(content, snapshot, 251, height - 31, 172, palette with { Text = White, Muted = new PdfColor(0.78, 0.88, 0.82) }, compact: true);

        var titleX = 445d;
        DrawTextColoredRight(content, width - 28, height - 27, 4.8, "AVES  -  GENETICA  -  RESULTADOS", new PdfColor(0.78, 0.88, 0.82), 180);
        DrawTextColoredBold(content, titleX, height - 48, 18.7, "CERTIFICADO DE GENEALOGIA", White, width - titleX - 24, "F5");
        DrawLine(content, titleX, height - 61, width - 44, height - 61, GoldLight, 0.9);
        DrawTextColored(content, titleX, height - 72, 6.6, "LINHAGEM EM FOCO  /  MODELO MODERNO", new PdfColor(0.88, 0.94, 0.89), width - titleX - 32, "F6");
        DrawTextColoredRight(content, width - 28, height - 72, 4.4, "MODERNO", GoldLight, 70);
    }

    private static void DrawModernIdentity(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double x,
        double y,
        double width,
        double height,
        CertificatePalette palette)
    {
        DrawFilledRectangle(content, x, y, width, height, palette.Surface, palette.Border, 0.8);
        DrawFilledRectangle(content, x, y, 5, height, palette.Primary);
        DrawTextColored(content, x + 15, y + height - 23, 5.3, "01  PERFIL DA AVE", palette.Muted, width - 27);
        DrawTextColoredBold(content, x + 15, y + height - 51, 15.8, snapshot.Name, palette.Text, width - 27, "F5");
        DrawLine(content, x + 15, y + height - 62, x + width - 14, y + height - 62, palette.Border, 0.6);

        var cursor = y + height - 79;
        DrawModernInfoRow(content, x + 15, ref cursor, width - 29, "ESPECIE", snapshot.Species, palette);
        DrawModernInfoRow(content, x + 15, ref cursor, width - 29, "ANILHA", snapshot.RingNumber ?? "Nao informado", palette);
        DrawModernInfoRow(content, x + 15, ref cursor, width - 29, "SEXO", GetSexLabel(snapshot.Sex), palette, snapshot.Sex);
        DrawModernInfoRow(content, x + 15, ref cursor, width - 29, "NASCIMENTO", FormatDate(snapshot.BirthDate), palette);

        var photoBottom = y + 46;
        var photoTop = cursor - 10;
        DrawCertificatePhoto(content, snapshot, x + 14, photoBottom, width - 28, Math.Max(76, photoTop - photoBottom), palette);
        DrawTextColored(content, x + 15, y + 27, 4.5, "REGISTRO VISUAL", palette.Muted, width - 29);
        DrawTextColoredBold(content, x + 15, y + 16, 5.3, "CRIATORIO VIRTUAL", palette.Primary, width - 29);
    }

    private static void DrawModernInfoRow(
        StringBuilder content,
        double x,
        ref double y,
        double width,
        string label,
        string value,
        CertificatePalette palette,
        BirdSex? sex = null)
    {
        DrawTextColored(content, x, y, 4.2, label, palette.Muted, width);
        if (sex is { } birdSex)
        {
            DrawSexSymbol(content, x + 5, y - 8, 6.5, birdSex, palette, palette.Surface);
            DrawTextColoredBold(content, x + 15, y - 11, 7, value, palette.Text, width - 15);
        }
        else
        {
            DrawTextColoredBold(content, x, y - 11, 7, value, palette.Text, width);
        }

        y -= 23;
    }

    private static void DrawClassicPremiumTree(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double x,
        double y,
        double width,
        double height,
        CertificatePalette palette)
    {
        DrawFilledRectangle(content, x, y, width, height, palette.TreeFill);
        DrawStrokedRectangle(content, x, y, width, height, palette.Border, 0.75);
        DrawLeaf(content, x + 12, y + height - 31, 15, 25, palette.Primary);
        DrawTextColoredBold(content, x + 34, y + height - 24, 13, "ARVORE GENEALOGICA", palette.Primary, width - 205, "F5");
        DrawTextColoredRight(content, x + width - 12, y + height - 22, 4.4, "TRADICAO  -  CONHECIMENTO  -  PRESERVACAO", palette.Muted, 153);
        DrawLine(content, x + 205, y + height - 19, x + width - 166, y + height - 19, palette.Secondary, 0.7);
        DrawLine(content, x + 13, y + height - 45, x + width - 13, y + height - 45, palette.Border, 0.45);

        var columnGap = 7d;
        var columnHeaderY = y + height - 69;
        var innerX = x + 8;
        var innerWidth = width - 16;
        var columnWidth = (innerWidth - (4 * columnGap)) / 5;
        var labels = new[] { "AVE", "PAIS", "AVOS", "BISAVOS", "TRISAVOS" };
        for (var level = 0; level <= 4; level++)
        {
            var columnX = innerX + (level * (columnWidth + columnGap));
            DrawRoundedRectangle(content, columnX, columnHeaderY, columnWidth, 17, 3.5, palette.ColumnFill, palette.Secondary, 0.45);
            DrawTextCenteredColored(content, columnX + (columnWidth / 2), columnHeaderY + 5.8, 5.1, labels[level], palette.Text, bold: true, maxWidth: columnWidth - 6, fontResource: "F5");
        }

        DrawCertificateWatermark(content, x, y, width, height, palette);
        var boxes = BuildCertificateNodeBoxes(
            snapshot,
            innerX,
            y + 7,
            innerWidth,
            columnHeaderY - (y + 7) - 8,
            columnGap,
            [61, 47, 36, 27, 18],
            [0, 8, 5, 3, 2]);
        DrawCertificateTreeConnections(content, boxes, palette.Connector, 0.9);
        foreach (var box in boxes.Values)
        {
            DrawClassicPremiumNode(content, box, palette);
        }
    }

    private static void DrawInstitutionalTree(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double x,
        double y,
        double width,
        double height,
        CertificatePalette palette)
    {
        DrawLeaf(content, x + 4, y + height - 27, 16, 26, palette.Primary);
        DrawTextColoredBold(content, x + 28, y + height - 22, 12.6, "ARVORE GENEALOGICA", palette.Text, width - 230, "F5");
        DrawTextColoredRight(content, x + width - 4, y + height - 20, 4.35, "TRADICAO  -  CONHECIMENTO  -  PRESERVACAO", palette.Muted, 157);
        DrawLine(content, x + 200, y + height - 17, x + width - 178, y + height - 17, palette.Primary, 0.65);

        var columnGap = 7d;
        var columnHeaderY = y + height - 48;
        var innerX = x + 3;
        var innerWidth = width - 6;
        var columnWidth = (innerWidth - (4 * columnGap)) / 5;
        var labels = new[] { "AVE", "PAIS", "AVOS", "BISAVOS", "TRISAVOS" };
        for (var level = 0; level <= 4; level++)
        {
            var columnX = innerX + (level * (columnWidth + columnGap));
            DrawRoundedRectangle(content, columnX, columnHeaderY, columnWidth, 18, 5, palette.ColumnFill, palette.ColumnFill, 0.2);
            DrawTextCenteredColored(content, columnX + (columnWidth / 2), columnHeaderY + 6, 5.1, labels[level], palette.Text, bold: true, maxWidth: columnWidth - 6, fontResource: "F2");
        }

        DrawInstitutionalWatermark(content, x, y, width, height, palette);
        var boxes = BuildCertificateNodeBoxes(
            snapshot,
            innerX,
            y + 7,
            innerWidth,
            columnHeaderY - (y + 7) - 8,
            columnGap,
            [58, 46, 35, 26, 17],
            [0, 8, 5, 3, 2]);
        DrawCertificateTreeConnections(content, boxes, palette.Connector, 0.78);
        foreach (var box in boxes.Values)
        {
            DrawInstitutionalNode(content, box, palette);
        }
    }

    private static void DrawModernTree(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double x,
        double y,
        double width,
        double height,
        CertificatePalette palette)
    {
        DrawTextColoredBold(content, x, y + height - 20, 8.8, "02  LINHAGEM EM FOCO", palette.Primary, 175, "F5");
        DrawTextColoredRight(content, x + width, y + height - 19, 4.35, "AVE  /  PAIS  /  AVOS  /  BISAVOS  /  TRISAVOS", palette.Muted, 205);
        DrawLine(content, x, y + height - 33, x + width, y + height - 33, palette.Primary, 0.85);

        var columnGap = 6d;
        var columnHeaderY = y + height - 59;
        var innerX = x;
        var innerWidth = width;
        var columnWidth = (innerWidth - (4 * columnGap)) / 5;
        var labels = new[] { "00  AVE", "01  PAIS", "02  AVOS", "03  BISAVOS", "04  TRISAVOS" };
        for (var level = 0; level <= 4; level++)
        {
            var columnX = innerX + (level * (columnWidth + columnGap));
            DrawTextColored(content, columnX, columnHeaderY + 8, 4.3, labels[level], palette.Muted, columnWidth, "F2");
            DrawLine(content, columnX, columnHeaderY, columnX + columnWidth, columnHeaderY, palette.Border, 0.7);
        }

        DrawModernWatermark(content, x, y, width, height, palette);
        var boxes = BuildCertificateNodeBoxes(
            snapshot,
            innerX,
            y + 7,
            innerWidth,
            columnHeaderY - (y + 7) - 8,
            columnGap,
            [56, 44, 33, 25, 16],
            [0, 7, 5, 3, 2]);
        DrawCertificateTreeConnections(content, boxes, palette.Connector, 0.75, direct: true);
        foreach (var box in boxes.Values)
        {
            DrawModernNode(content, box, palette);
        }
    }

    private static void DrawClassicPremiumFooter(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double width,
        double height,
        CertificatePalette palette)
    {
        DrawFilledRectangle(content, 0, 0, width, height, palette.FooterFill);
        DrawLine(content, 20, height - 1, width - 20, height - 1, palette.Secondary, 0.85);
        DrawTextColored(content, 25, height - 18, 4.7, "DATA DE EMISSAO", palette.FooterMuted, 120);
        DrawTextColoredBold(content, 25, height - 34, 7, snapshot.IssuedAtUtc?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Nao informada", palette.FooterText, 120, "F5");
        DrawLine(content, 157, 9, 157, height - 9, palette.FooterLine, 0.55);
        DrawTextColored(content, 174, height - 18, 4.7, "DOCUMENTO INTERNO", palette.FooterMuted, 190);
        DrawTextColoredBold(content, 174, height - 34, 5.2, snapshot.InternalDocumentIdentifier ?? "CV-GEN-NAO-INFORMADO", palette.FooterText, 190);
        DrawLine(content, 385, 9, 385, height - 9, palette.FooterLine, 0.55);
        DrawTextColoredBold(content, 407, height - 19, 6.3, "GERADO PELO CRIATORIO VIRTUAL", palette.FooterText, 204);
        DrawTextColored(content, 407, height - 34, 4.6, "Documento interno de genealogia. Nao substitui registros oficiais.", palette.FooterMuted, 204);
        DrawTextColoredRight(content, width - 24, height - 18, 5, "CLASSICO PREMIUM", palette.FooterText, 110);
        DrawTextColoredRight(content, width - 24, height - 34, 4.5, "A4  -  PAISAGEM", palette.FooterMuted, 110);
    }

    private static void DrawInstitutionalFooter(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double width,
        double height,
        CertificatePalette palette)
    {
        DrawFilledRectangle(content, 0, 0, width, height, palette.FooterFill);
        DrawLine(content, 21, height - 1, width - 21, height - 1, palette.Secondary, 0.8);
        DrawTextColored(content, 27, height - 17, 4.7, "DATA DE EMISSAO", palette.Muted, 120);
        DrawTextColoredBold(content, 27, height - 32, 6.9, snapshot.IssuedAtUtc?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Nao informada", palette.Text, 120);
        DrawLine(content, 158, 10, 158, height - 10, palette.Border, 0.55);
        DrawTextColored(content, 176, height - 17, 4.7, "DOCUMENTO INTERNO", palette.Muted, 190);
        DrawTextColoredBold(content, 176, height - 32, 5.1, snapshot.InternalDocumentIdentifier ?? "CV-GEN-NAO-INFORMADO", palette.Text, 190);
        DrawLine(content, 388, 10, 388, height - 10, palette.Border, 0.55);
        DrawTextColoredBold(content, 409, height - 18, 6, "GERADO PELO CRIATORIO VIRTUAL", palette.Text, 205);
        DrawTextColored(content, 409, height - 32, 4.45, "Documento interno para organizacao e identificacao genealogica.", palette.Muted, 205);
        DrawTextColoredRight(content, width - 25, height - 18, 4.6, "INSTITUCIONAL CLARO", palette.Primary, 116);
        DrawTextColoredRight(content, width - 25, height - 32, 4.4, "A4  -  PAISAGEM", palette.Muted, 116);
    }

    private static void DrawModernFooter(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double width,
        double height,
        CertificatePalette palette)
    {
        DrawFilledRectangle(content, 0, 0, width, height, palette.FooterFill);
        DrawTextColored(content, 24, height - 17, 4.6, "EMISSAO", new PdfColor(0.77, 0.88, 0.82), 75);
        DrawTextColoredBold(content, 24, height - 32, 6.7, snapshot.IssuedAtUtc?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Nao informada", White, 100);
        DrawLine(content, 143, 9, 143, height - 9, new PdfColor(0.42, 0.67, 0.57), 0.55);
        DrawTextColored(content, 160, height - 17, 4.6, "ID DO DOCUMENTO", new PdfColor(0.77, 0.88, 0.82), 150);
        DrawTextColoredBold(content, 160, height - 32, 5.1, snapshot.InternalDocumentIdentifier ?? "CV-GEN-NAO-INFORMADO", White, 150);
        DrawLine(content, 338, 9, 338, height - 9, new PdfColor(0.42, 0.67, 0.57), 0.55);
        DrawTextColoredBold(content, 358, height - 18, 6.1, "GERADO PELO CRIATORIO VIRTUAL", White, 205);
        DrawTextColored(content, 358, height - 32, 4.45, "LINHAGEM EM FOCO  /  A4 PAISAGEM", new PdfColor(0.77, 0.88, 0.82), 205);
        DrawTextColoredRight(content, width - 24, height - 18, 5, "MODERNO", GoldLight, 70);
        DrawTextColoredRight(content, width - 24, height - 32, 4.4, "CV  /  2026", new PdfColor(0.77, 0.88, 0.82), 70);
    }

    private static void DrawCertificateFarmBlock(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double x,
        double topY,
        double width,
        CertificatePalette palette,
        bool compact)
    {
        var headingSize = compact ? 8.5 : 10.2;
        var detailSize = compact ? 5.3 : 6.3;
        var lineGap = compact ? 11 : 13;
        DrawTextColoredBold(content, x, topY, headingSize, snapshot.BreedingFarmName, palette.Text, width, "F5");
        DrawTextColored(content, x, topY - lineGap, detailSize, $"Responsavel: {snapshot.BreedingFarmDetails?.ResponsibleName ?? "Nao informado"}", palette.Text, width);
        DrawTextColored(content, x, topY - (lineGap * 2), detailSize, $"Registro/CTF: {snapshot.BreedingFarmDetails?.OfficialRegistrationNumber ?? "Nao informado"}", palette.Muted, width);

        var contact = string.Join(
            "  |  ",
            new[] { snapshot.BreedingFarmDetails?.ContactPhone, snapshot.BreedingFarmDetails?.ContactEmail }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!));
        DrawTextColored(content, x, topY - (lineGap * 3), compact ? 4.8 : 5.6, contact.Length == 0 ? "Contato nao informado" : contact, palette.Muted, width);
    }

    private static Dictionary<string, CertificateNodeBox> BuildCertificateNodeBoxes(
        BirdDocumentSnapshot snapshot,
        double x,
        double y,
        double width,
        double height,
        double columnGap,
        IReadOnlyList<double> cardHeights,
        IReadOnlyList<double> cardGaps)
    {
        if (cardHeights.Count != 5 || cardGaps.Count != 5)
        {
            throw new ArgumentException("Certificate layouts require five generation metrics.", nameof(cardHeights));
        }

        var innerColumnWidth = (width - (4 * columnGap)) / 5;
        var nodesByPosition = snapshot.Genealogy
            .Where(node => !string.IsNullOrWhiteSpace(node.Position) &&
                           !string.Equals(node.Position, GenealogyNode.RootPosition, StringComparison.OrdinalIgnoreCase) &&
                           GetCertificateGeneration(node.Position) <= 4)
            .GroupBy(node => node.Position, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var boxes = new Dictionary<string, CertificateNodeBox>(StringComparer.OrdinalIgnoreCase);

        for (var level = 0; level <= 4; level++)
        {
            var positions = GetCertificatePositions(level);
            var cardHeight = cardHeights[level];
            var cardGap = cardGaps[level];
            var totalHeight = (positions.Count * cardHeight) + (Math.Max(0, positions.Count - 1) * cardGap);
            var groupTop = y + ((height + totalHeight) / 2);
            var columnX = x + (level * (innerColumnWidth + columnGap));
            for (var index = 0; index < positions.Count; index++)
            {
                var position = positions[index];
                var node = level == 0
                    ? new CertificateTreeNode("root", snapshot.Name, snapshot.RingNumber, snapshot.Sex)
                    : nodesByPosition.TryGetValue(position, out var source)
                        ? new CertificateTreeNode(
                            position,
                            string.IsNullOrWhiteSpace(source.Name) ? "Nao informado" : source.Name,
                            source.RingNumber,
                            source.Sex)
                        : new CertificateTreeNode(position, "Nao informado", null, null);
                var boxY = groupTop - ((index + 1) * cardHeight) - (index * cardGap);
                boxes[position == "root" ? "root" : position] = new CertificateNodeBox(
                    level,
                    columnX,
                    boxY,
                    innerColumnWidth,
                    cardHeight,
                    node);
            }
        }

        return boxes;
    }

    private static void DrawCertificateTreeConnections(
        StringBuilder content,
        IReadOnlyDictionary<string, CertificateNodeBox> boxes,
        PdfColor connector,
        double lineWidth,
        bool direct = false)
    {
        foreach (var parent in boxes.Values.Where(box => box.Level < 4))
        {
            foreach (var side in new[] { "father", "mother" })
            {
                var childPosition = parent.Node.Position == "root"
                    ? side
                    : $"{parent.Node.Position}.{side}";
                var child = boxes[childPosition];
                if (direct)
                {
                    DrawLine(content, parent.X + parent.Width, parent.CenterY, child.X, child.CenterY, connector, lineWidth);
                    continue;
                }

                var branchX = (parent.X + parent.Width + child.X) / 2;
                DrawLine(content, parent.X + parent.Width, parent.CenterY, branchX, parent.CenterY, connector, lineWidth);
                DrawLine(content, branchX, parent.CenterY, branchX, child.CenterY, connector, lineWidth);
                DrawLine(content, branchX, child.CenterY, child.X, child.CenterY, connector, lineWidth);
            }
        }
    }

    private static void DrawClassicPremiumNode(
        StringBuilder content,
        CertificateNodeBox box,
        CertificatePalette palette)
    {
        var style = GetCertificateNodeStyle(box, palette);
        var radius = box.Level == 0 ? 7 : box.Level == 4 ? 2.5 : 4.5;
        DrawRoundedRectangle(content, box.X, box.Y, box.Width, box.Height, radius, style.Fill, style.Border, box.Level == 0 ? 1.1 : 0.75);
        if (box.Level < 4)
        {
            DrawRoundedRectangle(content, box.X + 2, box.Y + 2, box.Width - 4, box.Height - 4, Math.Max(1, radius - 1.5), style.Fill, palette.Border, 0.25);
        }

        var iconSize = Math.Clamp(box.Height * 0.31, 5.6, box.Level == 0 ? 12 : 8.8);
        DrawSexSymbol(content, box.X + 10, box.CenterY, iconSize, box.Node.Sex, palette, style.Fill, style.Icon);
        var textX = box.X + 19;
        var textWidth = box.Width - 25;
        if (box.Level == 0)
        {
            DrawLeaf(content, box.X + box.Width - 24, box.Y + 11, 8, 17, palette.Secondary);
            DrawTextColored(content, textX, box.Y + box.Height - 14, 5, "AVE PRINCIPAL", palette.Primary, textWidth);
            DrawTextColoredBold(content, textX, box.Y + 23, 8.5, box.Node.Name, palette.Primary, textWidth, "F5");
            DrawTextColored(content, textX, box.Y + 9, 4.8, box.Node.RingNumber is null ? "Anilha nao informada" : $"Anilha {box.Node.RingNumber}", palette.Muted, textWidth);
            return;
        }

        if (box.Level == 4)
        {
            DrawTextColoredBold(content, textX, box.Y + 5, 4.35, box.Node.Name, style.Text, textWidth);
            return;
        }

        var labelSize = box.Level == 1 ? 4.7 : box.Level == 2 ? 4.2 : 3.75;
        var nameSize = box.Level == 1 ? 6.9 : box.Level == 2 ? 5.7 : 5;
        DrawTextColored(content, textX, box.Y + box.Height - labelSize - 5, labelSize, GetCertificateNodeRole(box.Node.Position), palette.Muted, textWidth);
        DrawTextColoredBold(content, textX, box.Y + (box.Level == 3 ? 8 : 13), nameSize, box.Node.Name, style.Text, textWidth, "F5");
        if (box.Level <= 2 && !string.IsNullOrWhiteSpace(box.Node.RingNumber))
        {
            DrawTextColored(content, textX, box.Y + 5, 4.2, $"Anilha {box.Node.RingNumber}", palette.Muted, textWidth);
        }
    }

    private static void DrawInstitutionalNode(
        StringBuilder content,
        CertificateNodeBox box,
        CertificatePalette palette)
    {
        var style = GetCertificateNodeStyle(box, palette);
        DrawRoundedRectangle(content, box.X, box.Y, box.Width, box.Height, box.Level == 4 ? 3 : 6, style.Fill, style.Border, box.Level == 0 ? 1.1 : 0.7);
        if (box.Level == 0)
        {
            DrawRoundedRectangle(content, box.X + 2, box.Y + 2, box.Width - 4, box.Height - 4, 5, style.Fill, palette.Secondary, 0.35);
        }

        var iconSize = Math.Clamp(box.Height * 0.3, 5.4, box.Level == 0 ? 11 : 8.5);
        DrawSexSymbol(content, box.X + 10, box.CenterY, iconSize, box.Node.Sex, palette, style.Fill, style.Icon);
        var textX = box.X + 19;
        var textWidth = box.Width - 25;
        if (box.Level == 0)
        {
            DrawTextColored(content, textX, box.Y + box.Height - 14, 4.8, "AVE PRINCIPAL", palette.Muted, textWidth);
            DrawTextColoredBold(content, textX, box.Y + 23, 8.3, box.Node.Name, palette.Text, textWidth, "F5");
            DrawTextColored(content, textX, box.Y + 9, 4.6, box.Node.RingNumber is null ? "Anilha nao informada" : $"Anilha {box.Node.RingNumber}", palette.Muted, textWidth);
            return;
        }

        if (box.Level == 4)
        {
            DrawTextColoredBold(content, textX, box.Y + 4.8, 4.3, box.Node.Name, palette.Text, textWidth);
            return;
        }

        var labelSize = box.Level == 1 ? 4.5 : box.Level == 2 ? 4.1 : 3.65;
        var nameSize = box.Level == 1 ? 6.7 : box.Level == 2 ? 5.55 : 4.9;
        DrawTextColored(content, textX, box.Y + box.Height - labelSize - 5, labelSize, GetCertificateNodeRole(box.Node.Position), palette.Muted, textWidth);
        DrawTextColoredBold(content, textX, box.Y + (box.Level == 3 ? 7.5 : 12.5), nameSize, box.Node.Name, palette.Text, textWidth, "F2");
        if (box.Level <= 2 && !string.IsNullOrWhiteSpace(box.Node.RingNumber))
        {
            DrawTextColored(content, textX, box.Y + 4.5, 4.1, $"Anilha {box.Node.RingNumber}", palette.Muted, textWidth);
        }
    }

    private static void DrawModernNode(
        StringBuilder content,
        CertificateNodeBox box,
        CertificatePalette palette)
    {
        var style = GetCertificateNodeStyle(box, palette);
        var radius = box.Level == 0 ? 5 : 2.5;
        DrawRoundedRectangle(content, box.X, box.Y, box.Width, box.Height, radius, style.Fill, palette.Border, 0.65);
        DrawFilledRectangle(content, box.X, box.Y, 3, box.Height, style.Icon);
        var iconSize = Math.Clamp(box.Height * 0.29, 5.2, box.Level == 0 ? 10.5 : 8);
        DrawSexSymbol(content, box.X + 11, box.CenterY, iconSize, box.Node.Sex, palette, style.Fill, style.Icon);
        var textX = box.X + 20;
        var textWidth = box.Width - 25;
        if (box.Level == 0)
        {
            DrawTextColored(content, textX, box.Y + box.Height - 13, 4.6, "AVE PRINCIPAL", palette.Muted, textWidth);
            DrawTextColoredBold(content, textX, box.Y + 22, 8.1, box.Node.Name, palette.Text, textWidth, "F5");
            DrawTextColored(content, textX, box.Y + 8, 4.45, box.Node.RingNumber is null ? "Anilha nao informada" : $"Anilha {box.Node.RingNumber}", palette.Muted, textWidth);
            return;
        }

        if (box.Level == 4)
        {
            DrawTextColoredBold(content, textX, box.Y + 4.4, 4.15, box.Node.Name, palette.Text, textWidth, "F2");
            return;
        }

        var labelSize = box.Level == 1 ? 4.4 : box.Level == 2 ? 4 : 3.6;
        var nameSize = box.Level == 1 ? 6.5 : box.Level == 2 ? 5.35 : 4.8;
        DrawTextColored(content, textX, box.Y + box.Height - labelSize - 5, labelSize, GetCertificateNodeRole(box.Node.Position), palette.Muted, textWidth);
        DrawTextColoredBold(content, textX, box.Y + (box.Level == 3 ? 7 : 12), nameSize, box.Node.Name, palette.Text, textWidth, "F2");
        if (box.Level <= 2 && !string.IsNullOrWhiteSpace(box.Node.RingNumber))
        {
            DrawTextColored(content, textX, box.Y + 4, 4, $"Anilha {box.Node.RingNumber}", palette.Muted, textWidth);
        }
    }

    private static string GetCertificateNodeRole(string position)
    {
        var generation = GetCertificateGeneration(position);
        return generation switch
        {
            1 => position.Equals("father", StringComparison.OrdinalIgnoreCase) ? "PAI" : "MAE",
            2 => "AVO",
            3 => "BISAVO",
            4 => "TRISAVO",
            _ => "ANCESTRAL"
        };
    }

    private static void DrawInstitutionalWatermark(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        CertificatePalette palette)
    {
        DrawLeaf(content, x + (width * 0.13), y + (height * 0.14), 25, 76, palette.Watermark);
        DrawLeaf(content, x + (width * 0.18), y + (height * 0.08), 21, 61, palette.Watermark, mirrored: true);
        DrawLeaf(content, x + (width * 0.81), y + (height * 0.12), 25, 73, palette.Watermark, mirrored: true);
        DrawLeaf(content, x + (width * 0.87), y + (height * 0.23), 16, 49, palette.Watermark);
    }

    private static void DrawModernWatermark(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        CertificatePalette palette)
    {
        DrawLeaf(content, x + (width * 0.27), y + (height * 0.12), 28, 82, palette.Watermark);
        DrawLeaf(content, x + (width * 0.34), y + (height * 0.06), 20, 62, palette.Watermark, mirrored: true);
        DrawLine(content, x + (width * 0.25), y + 20, x + (width * 0.38), y + height - 45, palette.Watermark, 0.55);
    }

    private static void DrawCertificateCanvas(
        StringBuilder content,
        double width,
        double height,
        CertificatePalette palette)
    {
        DrawFilledRectangle(content, 0, 0, width, height, palette.Background);
        DrawStrokedRectangle(content, 8, 8, width - 16, height - 16, palette.Border, palette.IsDark ? 1.1 : 0.9);
        DrawStrokedRectangle(content, 13, 13, width - 26, height - 26, palette.Secondary, palette.IsDark ? 0.5 : 0.35);
        DrawLeaf(content, 17, height - 65, 24, 42, palette.Watermark);
        DrawLeaf(content, 40, height - 45, 21, 34, palette.Watermark, mirrored: true);
        DrawLeaf(content, width - 17, 19, 25, 43, palette.Watermark, mirrored: true);
        DrawLeaf(content, width - 41, 19, 19, 31, palette.Watermark);
        if (palette.IsDark)
        {
            DrawLeaf(content, width * 0.45, 74, 42, 96, palette.Watermark);
            DrawLeaf(content, width * 0.55, 103, 38, 88, palette.Watermark, mirrored: true);
        }
    }

    private static void DrawCertificateHeader(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double width,
        double height,
        GenealogyCertificateModelId modelId,
        CertificatePalette palette)
    {
        if (palette.IsDark)
        {
            DrawFilledRectangle(content, 18, height - 103, width - 36, 84, palette.HeaderFill);
        }

        DrawCertificateLogo(
            content,
            27,
            height - 84,
            174,
            59,
            palette.Text,
            palette.Secondary,
            palette.LogoEmblemFill,
            palette.LogoBird,
            palette.LogoChest);
        DrawLine(content, 216, height - 27, 216, height - 91, palette.Border, 0.8);

        var farmX = 229d;
        var details = snapshot.BreedingFarmDetails;
        DrawTextColoredBold(content, farmX, height - 38, 10.5, snapshot.BreedingFarmName, palette.Text, 184);
        DrawTextColored(content, farmX, height - 54, 6.7, $"Responsavel: {details?.ResponsibleName ?? "Nao informado"}", palette.Text, 184);
        DrawTextColored(
            content,
            farmX,
            height - 67,
            6.5,
            $"Registro/CTF: {details?.OfficialRegistrationNumber ?? "Nao informado"}",
            palette.Muted,
            184);
        var contact = string.Join(
            "  |  ",
            new[] { details?.ContactPhone, details?.ContactEmail }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!));
        DrawTextColored(content, farmX, height - 80, 5.8, contact.Length == 0 ? "Contato nao informado" : contact, palette.Muted, 184);

        var titleX = 432d;
        DrawTextColoredRight(content, width - 22, height - 29, 5.3, "AVES  -  GENETICA  -  RESULTADOS", palette.Muted, 190);
        DrawTextColoredBold(content, titleX, height - 49, 18.5, "CERTIFICADO DE GENEALOGIA", palette.Text, width - titleX - 18);

        var pillWidth = 272d;
        var pillX = titleX + 34;
        DrawRoundedRectangle(content, pillX, height - 78, pillWidth, 17, 8.5, palette.IsDark ? palette.Surface : new PdfColor(0.96, 0.89, 0.70), palette.Secondary, 0.7);
        DrawTextCenteredColored(
            content,
            pillX + (pillWidth / 2),
            height - 72,
            5.8,
            "DOCUMENTO INTERNO DO CRIATORIO VIRTUAL",
            palette.IsDark ? palette.Text : Ink,
            bold: true,
            maxWidth: pillWidth - 20);
        DrawTextColored(content, titleX + 93, height - 96, 8.2, "Genetica, manejo e paixao em harmonia.", palette.Primary, width - titleX - 100);
        DrawTextColoredRight(content, width - 22, height - 101, 4.8, GetCertificateModelLabel(modelId).ToUpperInvariant(), palette.Muted, 95);
        DrawLine(content, 21, height - 108, width - 21, height - 108, palette.Secondary, 0.7);
    }

    private static void DrawCertificateLogo(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        PdfColor textColor,
        PdfColor accent,
        PdfColor emblemFill,
        PdfColor birdColor,
        PdfColor chestColor)
    {
        var emblem = Math.Min(height * 0.94, width * 0.32);
        var centerX = x + (emblem / 2);
        var centerY = y + (height * 0.51);
        var radius = (emblem / 2) - 1;
        var drawn = TryDrawImage(
            content,
            centerX - (radius - 4),
            centerY - (radius - 4),
            (radius - 4) * 2,
            (radius - 4) * 2,
            "image/png",
            CriatorioVirtualSymbolPng,
            transparentBackground: emblemFill);
        if (!drawn)
        {
            DrawLeaf(content, centerX - (emblem * 0.16), centerY - (emblem * 0.28), emblem * 0.22, emblem * 0.46, accent);
            DrawEllipse(content, centerX + (emblem * 0.08), centerY, emblem * 0.14, emblem * 0.23, birdColor);
            DrawEllipse(content, centerX + (emblem * 0.15), centerY - (emblem * 0.03), emblem * 0.07, emblem * 0.14, chestColor);
        }

        DrawEllipseOutline(content, centerX, centerY, radius, radius, accent, 1.35);
        DrawEllipseOutline(content, centerX, centerY, radius - 5, radius - 5, accent, 0.5);

        var textX = x + emblem + 10;
        var textWidth = width - (textX - x) - 4;
        DrawTextColored(content, textX, y + (height * 0.67), height * 0.15, "CRIATORIO", textColor, textWidth, "F1");
        DrawTextColoredBold(content, textX, y + (height * 0.34), height * 0.255, "VIRTUAL", textColor, textWidth, "F2");
        DrawTextColored(content, textX, y + (height * 0.10), height * 0.073, "GESTAO COM PAIXAO", textColor, textWidth, "F6");
    }

    private static void DrawCertificateIdentityPanel(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double x,
        double y,
        double width,
        double height,
        CertificatePalette palette)
    {
        DrawRoundedRectangle(content, x, y, width, height, 8, palette.Surface, palette.Border, 0.8);
        var top = y + height;
        var tagY = top - 36;
        var tagWidth = width * 0.36;
        DrawRoundedRectangle(content, x + 10, tagY, tagWidth, 24, 5, palette.Primary, palette.Primary, 0.4);
        DrawTextColoredBold(content, x + 20, tagY + 8, 7.7, "AVE", palette.IsDark ? DeepForest : White, tagWidth - 20);
        var ringText = snapshot.RingNumber is null ? "ANILHA -" : $"ANILHA {snapshot.RingNumber}";
        DrawRoundedRectangle(content, x + width - 104, tagY, 94, 24, 5, palette.IsDark ? palette.Surface : new PdfColor(0.96, 0.89, 0.70), palette.Secondary, 0.6);
        DrawTextCenteredColored(
            content,
            x + width - 57,
            tagY + 8,
            5.8,
            ringText,
            palette.IsDark ? palette.Text : Ink,
            bold: true,
            maxWidth: 84);
        DrawTextColoredBold(content, x + 12, top - 64, 13.5, snapshot.Name, palette.Text, width - 24);

        var cursor = top - 91;
        DrawCertificateInfoRow(content, x + 12, ref cursor, width - 24, "Especie", snapshot.Species, palette);
        DrawCertificateInfoRow(content, x + 12, ref cursor, width - 24, "Numero da anilha", snapshot.RingNumber ?? "Nao informado", palette);
        DrawCertificateInfoRow(content, x + 12, ref cursor, width - 24, "Sexo", GetSexLabel(snapshot.Sex), palette, snapshot.Sex);
        DrawCertificateInfoRow(content, x + 12, ref cursor, width - 24, "Nascimento", FormatDate(snapshot.BirthDate), palette);

        var photoBottom = y + 12;
        var photoTop = cursor - 8;
        if (snapshot.Photo is not null && photoTop - photoBottom > 48)
        {
            DrawCertificatePhoto(content, snapshot, x + 10, photoBottom, width - 20, photoTop - photoBottom, palette);
        }
        else if (photoTop - photoBottom > 30)
        {
            DrawCertificatePhotoPlaceholder(
                content,
                x + 10,
                photoBottom,
                width - 20,
                photoTop - photoBottom,
                palette);
        }
    }

    private static void DrawCertificateInfoRow(
        StringBuilder content,
        double x,
        ref double y,
        double width,
        string label,
        string value,
        CertificatePalette palette,
        BirdSex? sex = null)
    {
        DrawTextColored(content, x, y, 5.5, label.ToUpperInvariant(), palette.Muted, width);
        if (sex is not null)
        {
            DrawSexSymbol(content, x + 6, y - 10, 7.5, sex, palette, palette.Surface);
            DrawTextColoredBold(content, x + 16, y - 13, 8, value, palette.Text, width - 16);
        }
        else
        {
            DrawTextColoredBold(content, x, y - 13, 8, value, palette.Text, width);
        }

        y -= 28;
    }

    private static void DrawCertificatePhoto(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double x,
        double y,
        double width,
        double height,
        CertificatePalette palette)
    {
        DrawRoundedRectangle(content, x, y, width, height, 6, palette.IsDark ? palette.TreeFill : Cloud, palette.Secondary, 0.8);
        var drawn = snapshot.Photo is { } photo && TryDrawImage(content, x + 2, y + 2, width - 4, height - 4, photo.ContentType, photo.Content, cover: true);
        if (!drawn)
        {
            DrawCertificatePhotoPlaceholder(content, x, y, width, height, palette);
        }
    }

    private static void DrawCertificatePhotoPlaceholder(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        CertificatePalette palette)
    {
        var fill = palette.IsDark ? palette.Surface : Cloud;
        var border = palette.IsDark ? palette.Secondary : palette.Border;
        DrawRoundedRectangle(content, x, y, width, height, 6, fill, border, 0.7);
        var centerX = x + (width / 2);
        var emblemRadius = Math.Min(width, height) * 0.17;
        var emblemCenterY = y + (height * 0.58);
        DrawCircle(
            content,
            centerX,
            emblemCenterY,
            emblemRadius,
            palette.IsDark ? palette.Background : White,
            palette.Secondary,
            0.9);
        DrawLeaf(content, centerX - (emblemRadius * 0.24), emblemCenterY - (emblemRadius * 0.55), emblemRadius * 0.34, emblemRadius * 0.75, palette.Primary);
        DrawLeaf(content, centerX + (emblemRadius * 0.02), emblemCenterY - (emblemRadius * 0.60), emblemRadius * 0.27, emblemRadius * 0.61, palette.Primary, mirrored: true);
        DrawLine(content, centerX - (emblemRadius * 0.38), emblemCenterY - (emblemRadius * 0.45), centerX + (emblemRadius * 0.44), emblemCenterY + (emblemRadius * 0.40), palette.Secondary, 0.65);
        var placeholderText = palette.IsDark ? palette.Text : palette.NodeText;
        DrawTextCenteredColored(content, centerX, y + (height * 0.24), 6.1, "FOTO OPCIONAL", placeholderText, bold: true, maxWidth: width - 18);
        DrawTextCenteredColored(content, centerX, y + (height * 0.14), 5, "NAO INFORMADA", placeholderText, maxWidth: width - 18);
    }

    private static void DrawCertificateTree(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double x,
        double y,
        double width,
        double height,
        CertificatePalette palette)
    {
        DrawRoundedRectangle(content, x, y, width, height, 8, palette.TreeFill, palette.Border, 0.8);
        DrawLeaf(content, x + 15, y + height - 35, 14, 24, palette.Primary);
        DrawTextColoredBold(content, x + 35, y + height - 24, 12.8, "ARVORE GENEALOGICA", palette.Text, width - 220);
        DrawLine(content, x + 188, y + height - 19, x + width - 155, y + height - 19, palette.Secondary, 0.7);
        DrawTextColoredRight(content, x + width - 14, y + height - 22, 4.7, "TRADICAO  -  CONHECIMENTO  -  PRESERVACAO", palette.Muted, 142);

        var innerX = x + 9;
        var innerWidth = width - 18;
        var columnGap = 6d;
        var columnWidth = (innerWidth - (4 * columnGap)) / 5;
        var columnHeaderY = y + height - 54;
        var columnHeaderHeight = 18d;
        var labels = new[] { "", "PAIS", "AVOS", "BISAVOS", "TRISAVOS" };
        for (var level = 1; level <= 4; level++)
        {
            var columnX = innerX + (level * (columnWidth + columnGap));
            DrawRoundedRectangle(content, columnX, columnHeaderY, columnWidth, columnHeaderHeight, 4, palette.ColumnFill, palette.ColumnFill, 0.2);
            DrawTextCenteredColored(
                content,
                columnX + (columnWidth / 2),
                columnHeaderY + 6,
                5.5,
                labels[level],
                palette.IsDark ? palette.Text : palette.NodeText,
                bold: true,
                maxWidth: columnWidth - 6);
        }

        DrawCertificateWatermark(content, x, y, width, height, palette);
        var nodesByPosition = snapshot.Genealogy
            .Where(node => !string.IsNullOrWhiteSpace(node.Position) &&
                           !string.Equals(node.Position, GenealogyNode.RootPosition, StringComparison.OrdinalIgnoreCase) &&
                           GetCertificateGeneration(node.Position) <= 4)
            .GroupBy(node => node.Position, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var boxes = new Dictionary<string, CertificateNodeBox>(StringComparer.OrdinalIgnoreCase);
        var cardBottom = y + 9;
        var cardAreaHeight = columnHeaderY - cardBottom - 9;
        var cardHeights = new[] { 58d, 43d, 33d, 26d, 18d };
        var cardGaps = new[] { 0d, 8d, 5d, 3d, 2d };

        for (var level = 0; level <= 4; level++)
        {
            var positions = GetCertificatePositions(level);
            var cardHeight = cardHeights[level];
            var cardGap = cardGaps[level];
            var totalHeight = (positions.Count * cardHeight) + (Math.Max(0, positions.Count - 1) * cardGap);
            var groupTop = cardBottom + ((cardAreaHeight + totalHeight) / 2);
            var columnX = innerX + (level * (columnWidth + columnGap));
            for (var index = 0; index < positions.Count; index++)
            {
                var position = positions[index];
                var node = level == 0
                    ? new CertificateTreeNode("root", snapshot.Name, snapshot.RingNumber, snapshot.Sex)
                    : nodesByPosition.TryGetValue(position, out var source)
                        ? new CertificateTreeNode(
                            position,
                            string.IsNullOrWhiteSpace(source.Name) ? "Nao informado" : source.Name,
                            source.RingNumber,
                            source.Sex)
                        : new CertificateTreeNode(position, "Nao informado", null, null);
                var boxY = groupTop - ((index + 1) * cardHeight) - (index * cardGap);
                boxes.Add(position == "root" ? "root" : position, new CertificateNodeBox(level, columnX, boxY, columnWidth, cardHeight, node));
            }
        }

        foreach (var parent in boxes.Values.Where(box => box.Level < 4))
        {
            foreach (var side in new[] { "father", "mother" })
            {
                var childPosition = parent.Node.Position == "root"
                    ? side
                    : $"{parent.Node.Position}.{side}";
                var child = boxes[childPosition];
                var branchX = (parent.X + parent.Width + child.X) / 2;
                DrawLine(content, parent.X + parent.Width, parent.CenterY, branchX, parent.CenterY, palette.Connector, 0.8);
                DrawLine(content, branchX, parent.CenterY, branchX, child.CenterY, palette.Connector, 0.8);
                DrawLine(content, branchX, child.CenterY, child.X, child.CenterY, palette.Connector, 0.8);
            }
        }

        foreach (var box in boxes.Values)
        {
            DrawCertificateNode(content, box, palette);
        }
    }

    private static void DrawCertificateWatermark(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        CertificatePalette palette)
    {
        DrawLeaf(content, x + (width * 0.30), y + (height * 0.23), width * 0.08, height * 0.40, palette.Watermark);
        DrawLeaf(content, x + (width * 0.40), y + (height * 0.12), width * 0.09, height * 0.34, palette.Watermark, mirrored: true);
        DrawLeaf(content, x + (width * 0.70), y + (height * 0.32), width * 0.07, height * 0.28, palette.Watermark, mirrored: true);
    }

    private static void DrawCertificateNode(
        StringBuilder content,
        CertificateNodeBox box,
        CertificatePalette palette)
    {
        var style = GetCertificateNodeStyle(box, palette);
        DrawRoundedRectangle(content, box.X, box.Y, box.Width, box.Height, box.Level == 4 ? 3 : 5, style.Fill, style.Border, box.Level == 0 ? 1.1 : 0.7);
        var iconSize = Math.Clamp(box.Height * 0.28, 5.5, box.Level == 0 ? 11 : 8.5);
        DrawSexSymbol(content, box.X + 10, box.CenterY, iconSize, box.Node.Sex, palette, style.Fill, style.Icon);
        var textX = box.X + 18;
        var textWidth = box.Width - 24;

        if (box.Level == 0)
        {
            DrawTextColored(content, textX, box.Y + box.Height - 14, 5.4, "AVE PRINCIPAL", style.Text, textWidth);
            DrawTextColoredBold(content, textX, box.Y + 24, 8.7, box.Node.Name, style.Text, textWidth);
            DrawTextColored(content, textX, box.Y + 10, 5.4, box.Node.RingNumber is null ? "Anilha nao informada" : $"Anilha {box.Node.RingNumber}", style.Text, textWidth);
            return;
        }

        if (box.Level == 4)
        {
            DrawTextColoredBold(content, textX, box.Y + 5.5, 4.6, box.Node.Name, style.Text, textWidth);
            return;
        }

        var labelSize = box.Level == 1 ? 4.8 : box.Level == 2 ? 4.2 : 3.8;
        var nameSize = box.Level == 1 ? 7.1 : box.Level == 2 ? 5.8 : 5.1;
        DrawTextColored(content, textX, box.Y + box.Height - labelSize - 5, labelSize, GetPositionLabel(box.Node.Position), palette.Muted, textWidth);
        DrawTextColoredBold(content, textX, box.Y + (box.Level == 3 ? 8 : 13), nameSize, box.Node.Name, style.Text, textWidth);
        if (box.Level <= 2 && !string.IsNullOrWhiteSpace(box.Node.RingNumber))
        {
            DrawTextColored(content, textX, box.Y + 5, 4.4, $"Anilha {box.Node.RingNumber}", palette.Muted, textWidth);
        }
    }

    private static CertificateNodeStyle GetCertificateNodeStyle(
        CertificateNodeBox box,
        CertificatePalette palette)
    {
        if (box.Level == 0)
        {
            return new CertificateNodeStyle(
                palette.IsDark ? palette.Surface : White,
                palette.Secondary,
                palette.IsDark ? palette.Primary : palette.Text,
                palette.Secondary);
        }

        return box.Node.Sex switch
        {
            BirdSex.Male => new CertificateNodeStyle(palette.MaleFill, palette.MaleBorder, palette.NodeText, palette.MaleAccent),
            BirdSex.Female => new CertificateNodeStyle(palette.FemaleFill, palette.FemaleBorder, palette.NodeText, palette.FemaleAccent),
            _ => new CertificateNodeStyle(palette.UnknownFill, palette.UnknownBorder, palette.NodeText, palette.UnknownAccent)
        };
    }

    private static void DrawSexSymbol(
        StringBuilder content,
        double centerX,
        double centerY,
        double size,
        BirdSex? sex,
        CertificatePalette palette,
        PdfColor background,
        PdfColor? symbolColor = null)
    {
        var color = symbolColor ?? sex switch
        {
            BirdSex.Male => palette.MaleAccent,
            BirdSex.Female => palette.FemaleAccent,
            _ => palette.UnknownAccent
        };
        var radius = size * 0.38;
        if (sex == BirdSex.Male)
        {
            DrawCircle(content, centerX, centerY, radius, background, color, Math.Max(0.8, size * 0.12));
            var endX = centerX + (size * 0.55);
            var endY = centerY + (size * 0.55);
            DrawLine(content, centerX + (radius * 0.65), centerY + (radius * 0.65), endX, endY, color, Math.Max(0.8, size * 0.12));
            DrawLine(content, endX, endY, endX - (size * 0.25), endY, color, Math.Max(0.8, size * 0.12));
            DrawLine(content, endX, endY, endX, endY - (size * 0.25), color, Math.Max(0.8, size * 0.12));
        }
        else if (sex == BirdSex.Female)
        {
            DrawCircle(content, centerX, centerY + (size * 0.12), radius, background, color, Math.Max(0.8, size * 0.12));
            DrawLine(content, centerX, centerY - (radius * 0.95), centerX, centerY - (size * 0.62), color, Math.Max(0.8, size * 0.12));
            DrawLine(content, centerX - (size * 0.26), centerY - (size * 0.62), centerX + (size * 0.26), centerY - (size * 0.62), color, Math.Max(0.8, size * 0.12));
        }
        else
        {
            DrawCircle(content, centerX, centerY, radius * 0.7, background, color, Math.Max(0.7, size * 0.1));
            DrawLine(content, centerX - (size * 0.22), centerY, centerX + (size * 0.22), centerY, color, Math.Max(0.7, size * 0.1));
        }
    }

    private static void DrawCertificateFooter(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double width,
        double height,
        GenealogyCertificateModelId modelId,
        CertificatePalette palette)
    {
        DrawFilledRectangle(content, 0, 0, width, height, palette.FooterFill);
        DrawLine(content, 20, height - 1, width - 20, height - 1, palette.FooterLine, 0.8);
        DrawTextColored(content, 23, height - 17, 5, "DATA DE EMISSAO", palette.FooterMuted, 120);
        DrawTextColoredBold(content, 23, height - 33, 7.1, snapshot.IssuedAtUtc?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Nao informada", palette.FooterText, 120);
        DrawLine(content, 157, 10, 157, height - 10, palette.FooterLine, 0.5);
        DrawTextColored(content, 174, height - 17, 5, "DOCUMENTO INTERNO", palette.FooterMuted, 205);
        DrawTextColored(content, 174, height - 33, 5.1, snapshot.InternalDocumentIdentifier ?? "CV-GEN-NAO-INFORMADO", palette.FooterText, 205);
        DrawLine(content, 395, 10, 395, height - 10, palette.FooterLine, 0.5);
        DrawTextColoredBold(content, 425, height - 19, 7.2, "GERADO PELO CRIATORIO VIRTUAL", palette.FooterText, 190);
        DrawTextColored(content, 425, height - 34, 5.1, "Documento interno para organizacao e identificacao genealogica.", palette.FooterMuted, 190);
        DrawTextColoredRight(content, width - 23, height - 18, 5.1, GetCertificateModelLabel(modelId), palette.FooterText, 125);
        DrawTextColoredRight(content, width - 23, height - 33, 4.7, "A4  -  PAISAGEM", palette.FooterMuted, 125);
    }

    private static IReadOnlyList<string> GetCertificatePositions(int level)
    {
        if (level == 0)
        {
            return ["root"];
        }

        var positions = new List<string> { "father", "mother" };
        for (var generation = 2; generation <= level; generation++)
        {
            positions = positions
                .SelectMany(position => new[] { $"{position}.father", $"{position}.mother" })
                .ToList();
        }

        return positions;
    }

    private static int GetCertificateGeneration(string position) =>
        position.Count(character => character == '.') + 1;

    private static string GetCertificateModelLabel(GenealogyCertificateModelId modelId) => modelId switch
    {
        GenealogyCertificateModelId.ClassicPremium => "Classico Premium",
        GenealogyCertificateModelId.Institutional => "Institucional Claro",
        GenealogyCertificateModelId.Modern => "Moderno",
        _ => throw new ArgumentOutOfRangeException(nameof(modelId), "The genealogy certificate model is invalid.")
    };

    private static byte[] LoadEmbeddedLogoAsset()
    {
        const string resourceName = "CriatorioVirtual.Infrastructure.Documents.Assets.criatorio-virtual-symbol.png";
        using var stream = typeof(PdfDocumentRenderer).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return [];
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static IReadOnlyList<string> CreateProvenancePages(
        BirdDocumentSnapshot snapshot,
        double widthMillimeters,
        double heightMillimeters)
    {
        var nodes = snapshot.Genealogy.ToArray();
        const int nodesPerPage = 12;
        var pageCount = Math.Max(1, (int)Math.Ceiling(nodes.Length / (double)nodesPerPage));
        var pages = new List<string>(pageCount);
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            pages.Add(CreateProvenancePage(
                snapshot,
                widthMillimeters,
                heightMillimeters,
                nodes.Skip(pageIndex * nodesPerPage).Take(nodesPerPage).ToArray(),
                pageIndex > 0,
                pageIndex + 1,
                pageCount));
        }

        return pages;
    }

    private static string CreateProvenancePage(
        BirdDocumentSnapshot snapshot,
        double widthMillimeters,
        double heightMillimeters,
        IReadOnlyCollection<GenealogySnapshotNode> nodes,
        bool continuation,
        int pageNumber,
        int pageCount)
    {
        var width = widthMillimeters * PointsPerMillimeter;
        var height = heightMillimeters * PointsPerMillimeter;
        var content = new StringBuilder();
        DrawProvenanceCanvas(content, width, height);
        if (continuation)
        {
            DrawProvenanceHeader(content, snapshot, width, height, continuation);
            DrawAncestorGrid(content, nodes, 28, 92, width - 56, height - 196, "Pais e ancestrais registrados");
            DrawProvenanceFooter(content, snapshot, width, pageNumber, pageCount, continuation: true);
        }
        else
        {
            DrawProvenanceHeader(content, snapshot, width, height, continuation);
            DrawProvenanceTitle(content, width);
            DrawProvenanceIdentification(content, snapshot, width);
            DrawProvenanceAncestry(content, snapshot, nodes, width);
            DrawProvenanceDeclaration(content, snapshot, width);
            DrawProvenanceFooter(content, snapshot, width, pageNumber, pageCount, continuation: false);
        }

        return content.ToString();
    }

    private static void DrawProvenanceCanvas(StringBuilder content, double width, double height)
    {
        DrawFilledRectangle(content, 0, 0, width, height, Paper);
        DrawRoundedRectangle(content, 7, 7, width - 14, height - 14, 11, Paper, DeepForest, 2.2);
        DrawRoundedRectangle(content, 11, 11, width - 22, height - 22, 8, Paper, Gold, 0.8);

        var watermark = new PdfColor(0.92, 0.96, 0.92);
        DrawLeaf(content, width - 46, height - 76, 29, 43, watermark, mirrored: true);
        DrawLeaf(content, width - 67, height - 55, 22, 33, watermark, mirrored: true);
        DrawLeaf(content, 33, 402, 32, 48, watermark);
        DrawLeaf(content, width - 40, 121, 24, 38, watermark, mirrored: true);
    }

    private static void DrawProvenanceHeader(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double width,
        double height,
        bool continuation)
    {
        var headerBottom = height - 113;
        DrawProvenanceLogo(content, 26, height - 96, 190, 64);
        DrawLine(content, 228, height - 31, 228, height - 102, Sage, 0.8);

        var farm = snapshot.BreedingFarmDetails;
        var farmName = string.IsNullOrWhiteSpace(snapshot.BreedingFarmName)
            ? "Criatorio Virtual"
            : snapshot.BreedingFarmName;
        DrawTextColoredBold(content, 246, height - 34, 14.2, farmName, DeepForest, 182);
        DrawProvenanceHeaderInfoRow(
            content,
            247,
            height - 52,
            "Responsavel",
            farm?.ResponsibleName ?? "Nao informado",
            "person");
        DrawProvenanceHeaderInfoRow(
            content,
            247,
            height - 68,
            "CTF",
            farm?.OfficialRegistrationNumber ?? "Nao informado",
            "document");
        DrawProvenanceHeaderInfoRow(
            content,
            247,
            height - 84,
            "Telefone",
            farm?.ContactPhone ?? "Nao informado",
            "phone");
        DrawProvenanceHeaderInfoRow(
            content,
            247,
            height - 100,
            "Contato",
            farm?.ContactEmail ?? "Nao informado",
            "mail");

        DrawLeaf(content, width - 86, height - 61, 34, 49, new PdfColor(0.78, 0.87, 0.76), mirrored: true);
        DrawLeaf(content, width - 58, height - 68, 25, 39, new PdfColor(0.84, 0.91, 0.82), mirrored: true);
        DrawTrackedText(content, width - 48, height - 57, 5.4, "MAIS AVES", DeepForest, 0.45, bold: false);
        DrawTrackedText(content, width - 48, height - 68, 5.4, "HISTORIAS", DeepForest, 0.45, bold: false);
        DrawTrackedText(content, width - 48, height - 79, 5.4, "QUE VOAM", DeepForest, 0.45, bold: false);
        DrawLine(content, width - 66, height - 91, width - 31, height - 91, Gold, 1.7);

        DrawLine(content, 26, headerBottom, width - 26, headerBottom, Gold, 0.7);
        if (continuation)
        {
            DrawTextColoredBold(content, 26, headerBottom - 19, 12, "Documento de procedencia - continuacao", DeepForest, width - 52);
        }
    }

    private static void DrawProvenanceTitle(StringBuilder content, double width)
    {
        const double titleY = 666;
        DrawLine(content, 32, titleY + 8, 91, titleY + 8, Gold, 1.8);
        DrawLine(content, width - 91, titleY + 8, width - 32, titleY + 8, Gold, 1.8);
        DrawTextColoredCenteredBold(content, width / 2, titleY, 22.8, "DOCUMENTO DE PROCEDENCIA", DeepForest, width - 170);

        const double pillX = 112;
        const double pillY = 632;
        const double pillWidth = 371;
        DrawRoundedRectangle(content, pillX, pillY, pillWidth, 23, 11.5, GoldLight, Gold, 0.8);
        DrawTrackedText(content, width / 2, pillY + 8, 7.4, "DOCUMENTO INTERNO DO CRIATORIO VIRTUAL", Ink, 0.75, bold: false);
        DrawTextItalic(content, 166, 607, 11.5, "Genetica, manejo e paixao em harmonia.", maxWidth: width - 332);

        DrawLine(content, 32, 586, 151, 586, Gold, 0.7);
        DrawLine(content, width - 151, 586, width - 32, 586, Gold, 0.7);
        DrawTrackedText(content, width / 2, 582, 5.9, "TRADICAO   *   CONHECIMENTO   *   PRESERVACAO", Forest, 0.65, bold: false);
    }

    private static void DrawProvenanceIdentification(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double width)
    {
        const double x = 18;
        const double y = 394;
        const double panelWidth = 559;
        const double panelHeight = 192;
        DrawRoundedRectangle(content, x, y, panelWidth, panelHeight, 8, White, Gold, 0.9);
        DrawProvenanceSectionHeader(content, x + 8, y + panelHeight - 30, panelWidth - 16, 28, "IDENTIFICACAO DA AVE", null);

        var reference = snapshot.RingNumber is { } ring
            ? $"ID: CV-{ring}"
            : $"ID: CV-{snapshot.BirdId.ToString("N")[..8].ToUpperInvariant()}";
        DrawRoundedRectangle(content, x + panelWidth - 123, y + panelHeight - 30, 115, 27, 7, GoldLight, Gold, 0.4);
        DrawTextCentered(content, x + panelWidth - 65.5, y + panelHeight - 20, 8.9, reference, bold: true, maxWidth: 105);

        const double photoX = 26;
        const double photoY = 407;
        const double photoWidth = 170;
        const double photoHeight = 143;
        DrawProvenancePhoto(content, snapshot, photoX, photoY, photoWidth, photoHeight);

        var infoX = 215d;
        var valueX = 309d;
        var rowY = 543d;
        const double rowStep = 20.3;
        DrawProvenanceInfoRow(content, infoX, valueX, rowY, "Nome da ave", snapshot.Name, emphasize: true);
        rowY -= rowStep;
        DrawProvenanceInfoRow(content, infoX, valueX, rowY, "Especie", snapshot.Species);
        rowY -= rowStep;
        DrawProvenanceInfoRow(content, infoX, valueX, rowY, "Anilha", snapshot.RingNumber ?? "Nao informado");
        rowY -= rowStep;
        DrawProvenanceInfoRow(content, infoX, valueX, rowY, "Sexo", GetSexLabel(snapshot.Sex), sex: snapshot.Sex);
        rowY -= rowStep;
        DrawProvenanceInfoRow(content, infoX, valueX, rowY, "Nascimento", FormatDate(snapshot.BirthDate));
        rowY -= rowStep;
        DrawProvenanceInfoRow(content, infoX, valueX, rowY, "Criatorio", snapshot.BreedingFarmName);
        rowY -= rowStep;
        DrawProvenanceInfoRow(content, infoX, valueX, rowY, "Observacoes", $"Ave registrada no sistema do {snapshot.BreedingFarmName}.", maxValueWidth: width - valueX - 29);
    }

    private static void DrawProvenancePhoto(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double x,
        double y,
        double width,
        double height)
    {
        DrawRoundedRectangle(content, x, y, width, height, 7, new PdfColor(0.92, 0.96, 0.9), Forest, 0.8);
        var drawn = snapshot.Photo is { } photo && TryDrawImage(content, x + 2, y + 2, width - 4, height - 4, photo.ContentType, photo.Content);
        if (drawn)
        {
            return;
        }

        DrawLeaf(content, x + 57, y + 64, 31, 52, Forest);
        DrawLeaf(content, x + 86, y + 48, 22, 39, Sage, mirrored: true);
        DrawLine(content, x + 32, y + 33, x + width - 32, y + 33, Gold, 0.7);
        DrawTextCentered(content, x + (width / 2), y + 20, 7.2, "Foto da ave nao informada", bold: true, maxWidth: width - 18);
    }

    private static void DrawProvenanceInfoRow(
        StringBuilder content,
        double labelX,
        double valueX,
        double y,
        string label,
        string value,
        bool emphasize = false,
        BirdSex? sex = null,
        double? maxValueWidth = null)
    {
        DrawTextColoredBold(content, labelX, y, 8.5, $"{label}:", Ink, valueX - labelX - 8);
        if (sex is { } knownSex)
        {
            DrawSexSymbol(content, valueX + 5, y + 3, knownSex, 4.4);
            DrawTextColored(content, valueX + 17, y, 8.9, value, Ink, maxValueWidth ?? 235);
        }
        else if (emphasize)
        {
            DrawTextColoredBold(content, valueX, y, 9.7, value, Ink, maxValueWidth ?? 235);
        }
        else
        {
            DrawTextColored(content, valueX, y, 8.9, value, Ink, maxValueWidth ?? 235);
        }
    }

    private static void DrawProvenanceAncestry(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        IReadOnlyCollection<GenealogySnapshotNode> nodes,
        double width)
    {
        const double x = 18;
        const double y = 210;
        const double panelWidth = 559;
        const double panelHeight = 174;
        DrawRoundedRectangle(content, x, y, panelWidth, panelHeight, 8, White, Gold, 0.9);
        DrawProvenanceSectionHeader(content, x + 8, y + panelHeight - 30, panelWidth - 16, 28, "ASCENDENCIA", "LINHAGENS QUE CONSTROEM HISTORIA");

        var father = FindDirectParent(nodes, "father");
        var mother = FindDirectParent(nodes, "mother");
        const double cardY = 259;
        const double cardHeight = 74;
        const double leftX = 26;
        const double leftWidth = 178;
        const double centerX = 226;
        const double centerWidth = 143;
        const double rightX = 391;
        const double rightWidth = 178;
        var connectorY = cardY + (cardHeight / 2);
        DrawLine(content, leftX + leftWidth, connectorY, centerX, connectorY, Gold, 1.1);
        DrawLine(content, centerX + centerWidth, connectorY, rightX, connectorY, Gold, 1.1);
        DrawParentCard(content, father, "PAI", leftX, cardY, leftWidth, cardHeight, new PdfColor(0.92, 0.97, 0.9), new PdfColor(0.66, 0.82, 0.61));
        DrawCentralBirdCard(content, snapshot, centerX, cardY, centerWidth, cardHeight);
        DrawParentCard(content, mother, "MAE", rightX, cardY, rightWidth, cardHeight, new PdfColor(1, 0.93, 0.94), new PdfColor(0.96, 0.59, 0.64));

        const double summaryX = 26;
        const double summaryY = 220;
        const double summaryWidth = 543;
        const double summaryHeight = 26;
        DrawRoundedRectangle(content, summaryX, summaryY, summaryWidth, summaryHeight, 6, new PdfColor(0.95, 0.97, 0.94), new PdfColor(0.83, 0.88, 0.82), 0.6);
        DrawTextColoredBold(content, summaryX + 14, summaryY + 15, 7.3, "Resumo da linhagem:", Ink, 102);
        if (nodes.Count == 0)
        {
            DrawTextColored(content, summaryX + 126, summaryY + 15, 7.1, "Nenhuma ascendencia foi registrada no sistema.", Ink, summaryWidth - 140);
        }
        else
        {
            DrawTextColored(content, summaryX + 126, summaryY + 15, 7.1, $"Ave proveniente de linhagem registrada no {snapshot.BreedingFarmName},", Ink, summaryWidth - 140);
            DrawTextColored(content, summaryX + 126, summaryY + 6, 7.1, "conforme informacoes disponiveis no sistema.", Ink, summaryWidth - 140);
        }
    }

    private static GenealogySnapshotNode? FindDirectParent(
        IEnumerable<GenealogySnapshotNode> nodes,
        string position) => nodes.FirstOrDefault(node => string.Equals(node.Position, position, StringComparison.OrdinalIgnoreCase));

    private static void DrawParentCard(
        StringBuilder content,
        GenealogySnapshotNode? node,
        string heading,
        double x,
        double y,
        double width,
        double height,
        PdfColor fill,
        PdfColor stroke)
    {
        DrawRoundedRectangle(content, x, y, width, height, 6, fill, stroke, 0.8);
        DrawFilledRectangle(content, x + 1, y + height - 18, width - 2, 17, new PdfColor(
            Math.Min(1, fill.Red + 0.025),
            Math.Min(1, fill.Green + 0.025),
            Math.Min(1, fill.Blue + 0.025)));
        DrawTextColoredBold(content, x + 14, y + height - 13, 9.1, heading, Ink, width - 28);

        var name = node?.Name ?? "Nao informado";
        DrawSexSymbol(content, x + 20, y + 38, node?.Sex, 4.1, heading == "PAI" ? Forest : new PdfColor(0.87, 0.12, 0.25));
        DrawTextColoredBold(content, x + 37, y + height - 36, 9.1, name, Ink, width - 46);
        var ring = node?.RingNumber ?? "Nao informado";
        DrawTextColored(content, x + 37, y + 22, 7.1, $"Anilha {ring}", Ink, width - 46);
        var sex = node?.Sex is { } parentSex ? GetSexLabel(parentSex) : "Nao informado";
        DrawTextColored(content, x + 37, y + 12, 7.1, $"{sex} | Selvagem", Ink, width - 46);
        DrawTextColored(content, x + 37, y + 3, 6.9, $"Nascimento: {FormatDate(node?.BirthDate)}", Muted, width - 46);
    }

    private static void DrawCentralBirdCard(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double x,
        double y,
        double width,
        double height)
    {
        DrawRoundedRectangle(content, x, y, width, height, 6, White, Gold, 1.5);
        DrawLeaf(content, x + 16, y + 28, 14, 26, Forest);
        DrawLeaf(content, x + 29, y + 20, 10, 19, Sage, mirrored: true);
        DrawTextColoredBold(content, x + 53, y + height - 23, 8.8, snapshot.Name, Ink, width - 59);
        DrawTextColored(content, x + 53, y + 33, 6.8, $"CV 2.6 | {snapshot.RingNumber ?? "Nao informado"}", Ink, width - 59);
        DrawSexSymbol(content, x + 57, y + 19, snapshot.Sex, 3.7);
        DrawTextColored(content, x + 69, y + 17, 6.8, GetSexLabel(snapshot.Sex), Ink, width - 76);
    }

    private static void DrawProvenanceDeclaration(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double width)
    {
        const double x = 18;
        const double y = 93;
        const double panelWidth = 559;
        const double panelHeight = 101;
        DrawRoundedRectangle(content, x, y, panelWidth, panelHeight, 8, White, Gold, 0.9);
        DrawProvenanceSectionHeader(content, x + 8, y + panelHeight - 30, panelWidth - 16, 28, "DECLARACAO DE PROCEDENCIA", null);

        var issueDate = snapshot.IssuedAtUtc?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "nao informada";
        DrawTextColored(content, x + 23, y + 56, 8.5, "Declaramos, para fins de registro interno, que a ave acima identificada consta no plantel", Ink, panelWidth - 45);
        DrawTextColored(content, x + 23, y + 42, 8.5, $"do {snapshot.BreedingFarmName}, conforme as informacoes registradas no sistema na data de emissao", Ink, panelWidth - 45);
        DrawTextColored(content, x + 23, y + 28, 8.5, $"deste documento ({issueDate}).", Ink, panelWidth - 45);
        DrawTextColoredBold(content, x + 23, y + 17, 7.1, "Observacao: este documento nao substitui registros, declaracoes ou procedimentos oficiais do SISPASS/IBAMA.", Muted, panelWidth - 45);
    }

    private static void DrawProvenanceFooter(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double width,
        int pageNumber,
        int pageCount,
        bool continuation)
    {
        DrawLine(content, 26, 82, width - 26, 82, Gold, 0.8);
        DrawLine(content, 223, 25, 223, 75, Gold, 0.7);
        DrawLine(content, 394, 25, 394, 75, Gold, 0.7);

        DrawCalendarIcon(content, 37, 49, 15, DeepForest);
        DrawTextColored(content, 55, 61, 7.1, "Data de emissao:", Ink, 82);
        DrawTextColoredBold(content, 55, 48, 8.6, snapshot.IssuedAtUtc?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Nao informada", Ink, 82);

        DrawDocumentIcon(content, 145, 49, 15, DeepForest);
        DrawTextColored(content, 165, 61, 7.1, "Documento interno:", Ink, 94);
        DrawTextColoredBold(content, 165, 48, 8.6, GetProvenanceReference(snapshot), Ink, 94);

        var responsible = snapshot.BreedingFarmDetails?.ResponsibleName ?? "Responsavel pelo Criatorio";
        DrawTextItalic(content, 245, 57, 12.2, responsible, maxWidth: 126);
        DrawLine(content, 244, 49, 374, 49, Ink, 0.7);
        DrawTextCentered(content, 309, 37, 7.1, responsible.ToUpperInvariant(), bold: true, maxWidth: 130);
        DrawTextCentered(content, 309, 27, 6.5, "Responsavel pelo Criatorio", maxWidth: 145);

        DrawRoundedRectangle(content, 405, 27, 163, 47, 8, new PdfColor(0.94, 0.95, 0.94), null);
        DrawCircle(content, 425, 58, 8, new PdfColor(0.35, 0.39, 0.4));
        DrawTextCentered(content, 425, 54.5, 9.2, "i", bold: true);
        DrawTextColored(content, 441, 63, 6.1, "Este documento e um registro", Muted, 116);
        DrawTextColored(content, 441, 54, 6.1, "interno do Criatorio Virtual.", Muted, 116);
        DrawTextColored(content, 441, 45, 6.1, "Nao substitui registros", Muted, 116);
        DrawTextColored(content, 441, 36, 6.1, "oficiais do SISPASS/IBAMA.", Muted, 116);
        if (pageCount > 1)
        {
            DrawTextColoredRight(content, width - 27, 30, 5.4, $"Pagina {pageNumber} de {pageCount}", Muted, maxWidth: 70);
        }

        DrawLine(content, 26, 17, 110, 17, Gold, 0.7);
        DrawLine(content, width - 110, 17, width - 26, 17, Gold, 0.7);
        DrawTrackedText(content, width / 2, 13, 4.7, "CRIATORIO VIRTUAL   *   TECNOLOGIA A FAVOR DA SUA CRIACAO", Forest, 0.75, bold: false);
        DrawTextColoredRight(content, width - 28, 13, 4.2, "AVES  GENETICA  RESULTADOS", Forest, maxWidth: 105);
        if (continuation)
        {
            DrawTextColored(content, 28, 30, 5.4, "Ancestrais adicionais", Muted, 80);
        }
    }

    private static string GetProvenanceReference(BirdDocumentSnapshot snapshot) =>
        snapshot.RingNumber is { } ring
            ? $"DP-{ring}"
            : $"DP-{snapshot.BirdId.ToString("N")[..8].ToUpperInvariant()}";

    private static void DrawProvenanceSectionHeader(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        string heading,
        string? trailingText)
    {
        DrawRoundedRectangle(content, x, y, width, height, 6, DeepForest);
        DrawLeaf(content, x + 12, y + 5, 13, 18, White);
        DrawLeaf(content, x + 23, y + 4, 9, 14, Mint, mirrored: true);
        DrawTextColoredBold(content, x + 40, y + 8, 11.2, heading, White, trailingText is null ? width - 50 : width * 0.55);
        if (trailingText is not null)
        {
            DrawTrackedText(content, x + width - 116, y + 10, 4.6, trailingText, White, 0.35, bold: false);
        }
    }

    private static void DrawProvenanceLogo(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height)
    {
        DrawRoundedRectangle(content, x, y, width, height, 10, DeepForest, Gold, 1.15);
        if (BrandLogoBytes.Length > 0 &&
            TryDrawImage(
                content,
                x + 5,
                y + 4,
                width - 10,
                height - 8,
                "image/png",
                BrandLogoBytes,
                transparentBackground: DeepForest))
        {
            return;
        }

        // Keep a deterministic fallback if a deployment omits the embedded brand asset.
        DrawLeaf(content, x + 15, y + 16, 18, 29, GoldLight);
        DrawLeaf(content, x + 29, y + 11, 13, 23, Mint, mirrored: true);
        DrawTextColoredBold(content, x + 52, y + 35, 9.7, "CRIATORIO VIRTUAL", White, width - 61);
        DrawTrackedText(content, x + 52, y + 18, 3.8, "GESTAO COM PAIXAO", GoldLight, 0.65, bold: false);
    }

    private static byte[] LoadEmbeddedAsset(string resourceName)
    {
        using var stream = typeof(PdfDocumentRenderer).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return [];
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void DrawProvenanceHeaderInfoRow(
        StringBuilder content,
        double x,
        double y,
        string label,
        string value,
        string icon)
    {
        DrawHeaderIcon(content, x, y + 3, icon, Forest);
        var labelWidth = (label.Length * 4.05) + 5;
        DrawTextColoredBold(content, x + 15, y, 7.35, $"{label}:", Forest, labelWidth + 2);
        DrawTextColored(content, x + 15 + labelWidth, y, 7.35, value, Ink, 205 - labelWidth);
    }

    private static void DrawHeaderIcon(
        StringBuilder content,
        double x,
        double y,
        string icon,
        PdfColor color)
    {
        switch (icon)
        {
            case "person":
                DrawCircle(content, x, y + 3, 2.7, color);
                DrawRoundedRectangle(content, x - 4.2, y - 4.5, 8.4, 4.7, 2.1, color);
                break;
            case "document":
                DrawRoundedRectangle(content, x - 4.2, y - 5.5, 8.4, 11, 1.2, White, color, 1);
                DrawLine(content, x - 2.2, y + 1.7, x + 2.2, y + 1.7, color, 0.7);
                DrawLine(content, x - 2.2, y - 0.7, x + 2.2, y - 0.7, color, 0.7);
                DrawLine(content, x - 2.2, y - 3.1, x + 1, y - 3.1, color, 0.7);
                break;
            case "phone":
                DrawLine(content, x - 3.5, y + 4, x - 1.2, y + 1.7, color, 1.4);
                DrawLine(content, x - 1.2, y + 1.7, x + 3.4, y - 2.9, color, 1.4);
                DrawLine(content, x + 3.4, y - 2.9, x + 1.7, y - 4.2, color, 1.4);
                break;
            case "mail":
                DrawStrokedRectangle(content, x - 5, y - 4, 10, 7, color, 1);
                DrawLine(content, x - 4.5, y + 2.3, x, y - 1, color, 0.8);
                DrawLine(content, x, y - 1, x + 4.5, y + 2.3, color, 0.8);
                break;
            default:
                DrawCircle(content, x, y, 2, color);
                break;
        }
    }

    private static void DrawSexSymbol(
        StringBuilder content,
        double x,
        double y,
        BirdSex? sex,
        double radius,
        PdfColor? overrideColor = null)
    {
        var color = overrideColor ?? (sex == BirdSex.Female ? new PdfColor(0.87, 0.12, 0.25) : new PdfColor(0.05, 0.38, 0.82));
        if (sex is null or BirdSex.Unknown)
        {
            DrawCircle(content, x, y, radius, White, Muted, 0.8);
            return;
        }

        DrawCircle(content, x, y, radius, White, color, 1.25);
        if (sex == BirdSex.Male)
        {
            DrawLine(content, x + (radius * 0.65), y + (radius * 0.65), x + (radius * 1.65), y + (radius * 1.65), color, 1.25);
            DrawLine(content, x + (radius * 1.65), y + (radius * 1.65), x + (radius * 0.9), y + (radius * 1.65), color, 1.25);
            DrawLine(content, x + (radius * 1.65), y + (radius * 1.65), x + (radius * 1.65), y + (radius * 0.9), color, 1.25);
        }
        else
        {
            DrawLine(content, x, y - radius, x, y - (radius * 2.1), color, 1.25);
            DrawLine(content, x - (radius * 0.9), y - (radius * 2.1), x + (radius * 0.9), y - (radius * 2.1), color, 1.25);
        }
    }

    private static void DrawCalendarIcon(StringBuilder content, double x, double y, double size, PdfColor color)
    {
        DrawRoundedRectangle(content, x - (size / 2), y - (size / 2), size, size, 2, White, color, 1.2);
        DrawLine(content, x - (size / 2), y + 2, x + (size / 2), y + 2, color, 1.1);
        DrawLine(content, x - 4, y + (size / 2) + 1, x - 4, y + (size / 2) - 3, color, 1.4);
        DrawLine(content, x + 4, y + (size / 2) + 1, x + 4, y + (size / 2) - 3, color, 1.4);
        DrawCircle(content, x - 3.5, y - 2, 0.8, color);
        DrawCircle(content, x + 1, y - 2, 0.8, color);
        DrawCircle(content, x - 3.5, y - 5.5, 0.8, color);
        DrawCircle(content, x + 1, y - 5.5, 0.8, color);
    }

    private static void DrawDocumentIcon(StringBuilder content, double x, double y, double size, PdfColor color)
    {
        DrawRoundedRectangle(content, x - (size / 2), y - (size / 2), size, size + 2, 2, White, color, 1.2);
        DrawLine(content, x - 4, y + 4, x + 4, y + 4, color, 0.9);
        DrawLine(content, x - 4, y + 0.5, x + 4, y + 0.5, color, 0.9);
        DrawLine(content, x - 4, y - 3, x + 2, y - 3, color, 0.9);
        DrawLine(content, x - 2, y + 8, x + 2, y + 8, color, 1.2);
    }

    private static void DrawA4Canvas(StringBuilder content, double width, double height)
    {
        DrawFilledRectangle(content, 0, 0, width, height, Paper);
        DrawFilledRectangle(content, 0, height - 5, width, 5, Forest);
        DrawStrokedRectangle(content, 18, 18, width - 36, height - 36, Mint, 0.8);
        DrawLeaf(content, width - 76, 30, 34, 48, Mint, mirrored: true);
        DrawLeaf(content, 27, height - 83, 28, 38, Mint);
    }

    private static void DrawA4Header(
        StringBuilder content,
        double width,
        double height,
        string title,
        string subtitle,
        bool continuation)
    {
        var headerHeight = 86d;
        DrawFilledRectangle(content, 18, height - 18 - headerHeight, width - 36, headerHeight, DeepForest);
        DrawBrandLockup(content, 34, height - 83, 29, White);
        DrawTextColoredBold(content, 270, height - 48, 19, title, White, width - 340);
        DrawTextColored(content, 272, height - 68, 8.5, continuation ? $"{subtitle} - continuacao" : subtitle, Mint, width - 340);
        DrawTextColored(content, 272, height - 80, 6.2, "Identidade, organizacao e paixao pelo mundo das aves.", new PdfColor(0.75, 0.84, 0.8), width - 340);
        DrawLine(content, 34, height - 96, width - 34, height - 96, Sage, 0.7);
    }

    private static void DrawA4Footer(
        StringBuilder content,
        double width,
        int pageNumber,
        int pageCount,
        string legalText)
    {
        DrawLine(content, 34, 48, width - 34, 48, Mint, 0.8);
        DrawBrandLockup(content, 34, 25, 13, Forest, compact: true);
        DrawTextColored(content, 172, 27, 6.2, legalText, Muted, width - 280);
        DrawTextRight(content, width - 34, 27, 6.3, $"Pagina {pageNumber} de {pageCount}", true, 100);
    }

    private static void DrawIdentityPanel(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double x,
        double y,
        double width,
        double height,
        string heading)
    {
        DrawRoundedRectangle(content, x, y, width, height, 9, White, Mint, 1);
        DrawFilledRectangle(content, x, y + height - 44, width, 44, Forest);
        DrawLeaf(content, x + 14, y + height - 34, 15, 22, Mint);
        DrawTextColoredBold(content, x + 38, y + height - 25, 10.5, heading, White, width - 50);
        var cursor = y + height - 68;
        DrawA4InfoRow(content, x + 16, ref cursor, width - 32, "Ave", snapshot.Name);
        DrawA4InfoRow(content, x + 16, ref cursor, width - 32, "Numero da anilha", snapshot.RingNumber ?? "Nao informado");
        DrawA4InfoRow(content, x + 16, ref cursor, width - 32, "Sexo", GetSexLabel(snapshot.Sex));
        DrawA4InfoRow(content, x + 16, ref cursor, width - 32, "Especie", snapshot.Species);
        DrawA4InfoRow(content, x + 16, ref cursor, width - 32, "Data de nascimento", FormatDate(snapshot.BirthDate));
        DrawA4InfoRow(content, x + 16, ref cursor, width - 32, "Criatorio", snapshot.BreedingFarmName);

        if (snapshot.BreedingFarmDetails is { } farm)
        {
            DrawLine(content, x + 16, cursor + 8, x + width - 16, cursor + 8, Mint, 0.7);
            cursor -= 5;
            DrawA4InfoRow(content, x + 16, ref cursor, width - 32, "Responsavel", farm.ResponsibleName);
            DrawA4InfoRow(content, x + 16, ref cursor, width - 32, "Contato", farm.ContactEmail);
            if (!string.IsNullOrWhiteSpace(farm.OfficialRegistrationNumber))
            {
                DrawA4InfoRow(content, x + 16, ref cursor, width - 32, "Registro oficial", farm.OfficialRegistrationNumber);
            }
        }
    }

    private static void DrawA4InfoRow(
        StringBuilder content,
        double x,
        ref double y,
        double width,
        string label,
        string value)
    {
        DrawTextColored(content, x, y, 6.1, label.ToUpperInvariant(), Muted, width);
        DrawTextColoredBold(content, x, y - 11, 8.8, value, Ink, width);
        y -= 34;
    }

    private static void DrawCertificateSeal(StringBuilder content, double x, double y, double radius)
    {
        DrawCircle(content, x, y, radius, GoldLight, Gold, 1.2);
        DrawCircle(content, x, y, radius - 5, Paper, Gold, 0.6);
        DrawTextCentered(content, x, y + 3, 8.5, "CV", bold: true);
        DrawTextCentered(content, x, y - 9, 5.3, "REGISTRADA", bold: true, maxWidth: radius * 1.7);
    }

    private static void DrawSignatureBox(StringBuilder content, double x, double y, double width, double height)
    {
        DrawRoundedRectangle(content, x, y, width, height, 5, White, Mint, 0.8);
        DrawTextCentered(content, x + (width / 2), y + 22, 6.3, "Assinatura manual", bold: true, maxWidth: width - 20);
        DrawLine(content, x + 20, y + 10, x + width - 20, y + 10, Muted, 0.6);
    }

    private static void DrawGenealogyTree(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        IEnumerable<GenealogySnapshotNode> nodes,
        double x,
        double y,
        double width,
        double height,
        bool compact,
        PdfColor accent,
        PdfColor foreground)
    {
        DrawRoundedRectangle(content, x, y, width, height, 9, new PdfColor(0.98, 0.995, 0.98), Mint, 0.9);
        DrawTextColoredBold(content, x + 16, y + height - (compact ? 20 : 25), compact ? 7.5 : 12, "Posicoes genealogicas", foreground, width - 32);
        var treeX = x + 14;
        var treeY = y + 12;
        var treeWidth = width - 28;
        var treeHeight = height - (compact ? 34 : 44);
        var grouped = nodes
            .GroupBy(GetNodeGeneration)
            .Where(group => group.Key <= (compact ? 3 : 4))
            .OrderBy(group => group.Key)
            .Select(group => group.OrderBy(node => node.Position, StringComparer.Ordinal).ToArray())
            .ToArray();
        var columns = Math.Max(1, Math.Min(compact ? 4 : 5, grouped.Length + 1));
        var columnGap = compact ? 5 : 10;
        var columnWidth = (treeWidth - ((columns - 1) * columnGap)) / columns;
        var boxes = new List<NodeBox>();
        var rootHeight = compact ? Math.Min(31, treeHeight * 0.48) : Math.Min(68, treeHeight * 0.32);
        var rootY = treeY + ((treeHeight - rootHeight) / 2);
        boxes.Add(new NodeBox(
            0,
            treeX,
            rootY,
            columnWidth,
            rootHeight,
            "Ave",
            snapshot.Name,
            snapshot.RingNumber,
            GetSexLabel(snapshot.Sex)));

        var visibleNodeCount = 0;
        foreach (var group in grouped)
        {
            var level = group[0].Position.Count(character => character == '.') + 1;
            if (level >= columns)
            {
                break;
            }

            var maxNodes = compact ? 2 : Math.Min(group.Length, 6);
            var visible = group.Take(maxNodes).ToArray();
            visibleNodeCount += visible.Length;
            var gap = compact ? 4 : 8;
            var nodeHeight = Math.Min(compact ? 27 : 46, (treeHeight - ((visible.Length - 1) * gap)) / Math.Max(1, visible.Length));
            var startY = treeY + ((treeHeight - ((nodeHeight * visible.Length) + (gap * (visible.Length - 1)))) / 2);
            var nodeX = treeX + (level * (columnWidth + columnGap));
            for (var index = 0; index < visible.Length; index++)
            {
                var node = visible[index];
                boxes.Add(new NodeBox(
                    level,
                    nodeX,
                    startY + ((visible.Length - index - 1) * (nodeHeight + gap)),
                    columnWidth,
                    nodeHeight,
                    GetPositionLabel(node.Position),
                    string.IsNullOrWhiteSpace(node.Name) ? "Nao informado" : node.Name,
                    node.RingNumber,
                    node.Sex is { } sex ? GetSexLabel(sex) : null));
            }
        }

        foreach (var box in boxes.Where(box => box.Level > 0))
        {
            var parent = boxes
                .Where(candidate => candidate.Level == box.Level - 1)
                .OrderBy(candidate => Math.Abs(candidate.CenterY - box.CenterY))
                .First();
            DrawLine(content, parent.X + parent.Width, parent.CenterY, box.X, box.CenterY, accent, compact ? 0.55 : 0.9);
        }

        foreach (var box in boxes)
        {
            var fill = box.Level == 0 ? accent : White;
            var text = box.Level == 0 ? White : Ink;
            DrawRoundedRectangle(content, box.X, box.Y, box.Width, box.Height, compact ? 4 : 6, fill, box.Level == 0 ? accent : Line, 0.7);
            DrawTextColored(content, box.X + 5, box.Y + box.Height - (compact ? 8 : 14), compact ? 4.1 : 5.8, box.Position, box.Level == 0 ? Mint : Muted, box.Width - 10);
            DrawTextColoredBold(content, box.X + 5, box.Y + (compact ? 5 : 17), compact ? 5.5 : 8.2, box.Name, text, box.Width - 10);
            if (!string.IsNullOrWhiteSpace(box.RingNumber) && !compact)
            {
                DrawTextColored(content, box.X + 5, box.Y + 6, 5.7, $"Anilha {box.RingNumber}", box.Level == 0 ? Mint : Muted, box.Width - 10);
            }
        }

        var omitted = nodes.Count() - visibleNodeCount;
        if (omitted > 0)
        {
            DrawTextColoredRight(content, x + width - 14, y + 8, compact ? 4.2 : 6.2, $"+{omitted} ancestrais adicionais", Muted, maxWidth: width * 0.45);
        }
        if (!nodes.Any())
        {
            DrawTextColored(content, treeX + 10, treeY + (treeHeight / 2), compact ? 5.2 : 8, "Nenhum ancestral registrado na genealogia autorizada.", Muted, treeWidth - 20);
        }
    }

    private static void DrawAncestorGrid(
        StringBuilder content,
        IReadOnlyCollection<GenealogySnapshotNode> nodes,
        double x,
        double y,
        double width,
        double height,
        string heading)
    {
        DrawRoundedRectangle(content, x, y, width, height, 9, new PdfColor(0.98, 0.995, 0.98), Mint, 0.9);
        DrawTextColoredBold(content, x + 16, y + height - 26, 12, heading, Ink, width - 32);
        var cards = nodes.ToArray();
        if (cards.Length == 0)
        {
            DrawTextColored(content, x + 16, y + (height / 2), 8, "Nenhum pai ou ancestral registrado na genealogia autorizada.", Muted, width - 32);
            return;
        }

        var columns = width > 400 ? 2 : 1;
        var gap = 9d;
        var cardWidth = (width - 32 - ((columns - 1) * gap)) / columns;
        var rows = (int)Math.Ceiling(cards.Length / (double)columns);
        var cardGap = 7d;
        var cardHeight = Math.Min(58, (height - 48 - ((rows - 1) * cardGap)) / rows);
        for (var index = 0; index < cards.Length; index++)
        {
            var column = index % columns;
            var row = index / columns;
            var cardX = x + 16 + (column * (cardWidth + gap));
            var cardY = y + height - 38 - ((row + 1) * cardHeight) - (row * cardGap);
            DrawAncestorCard(content, cards[index], cardX, cardY, cardWidth, cardHeight);
        }
    }

    private static void DrawAncestorCard(
        StringBuilder content,
        GenealogySnapshotNode node,
        double x,
        double y,
        double width,
        double height)
    {
        DrawRoundedRectangle(content, x, y, width, height, 5, White, Line, 0.7);
        DrawFilledRectangle(content, x, y, 4, height, Forest);
        DrawTextColored(content, x + 11, y + height - 14, 6.2, GetPositionLabel(node.Position), Forest, width - 18);
        DrawTextColoredBold(content, x + 11, y + height - 28, 9, string.IsNullOrWhiteSpace(node.Name) ? "Nao informado" : node.Name, Ink, width - 18);
        var details = string.Join(
            " | ",
            new[]
            {
                string.IsNullOrWhiteSpace(node.RingNumber) ? "Anilha: Nao informado" : $"Anilha: {node.RingNumber}",
                node.Sex is { } sex ? $"Sexo: {GetSexLabel(sex)}" : "Sexo: Nao informado",
                node.BirthDate is { } birthDate ? $"Nascimento: {FormatDate(birthDate)}" : "Nascimento: Nao informado"
            });
        DrawTextColored(content, x + 11, y + 8, 6.1, details, Muted, width - 18);
    }

    private static (string Label, string Value) GetFieldValue(DocumentField field, BirdDocumentSnapshot snapshot) => field switch
    {
        DocumentField.Name => ("Nome", snapshot.Name),
        DocumentField.RingNumber => ("Numero da anilha", snapshot.RingNumber ?? "Nao informado"),
        DocumentField.Sex => ("Sexo", GetSexLabel(snapshot.Sex)),
        DocumentField.Species => ("Especie", snapshot.Species),
        DocumentField.BirthDate => ("Data de nascimento", FormatDate(snapshot.BirthDate)),
        DocumentField.BreedingFarmName => ("Criatorio", snapshot.BreedingFarmName),
        DocumentField.BirdPhoto => ("Foto da ave", snapshot.Photo is null ? "Nao informado" : snapshot.Photo.FileName),
        DocumentField.GenealogyTree => ("Arvore genealogica", snapshot.Genealogy.Count == 0 ? "Nao informado" : "Ver verso"),
        _ => throw new ArgumentOutOfRangeException(nameof(field), "The document field is invalid.")
    };

    private static string GetModelLabel(BadgeModelId modelId) => modelId switch
    {
        BadgeModelId.Classic => "Modelo classico",
        BadgeModelId.Minimalist => "Modelo minimalista",
        BadgeModelId.Competition => "Modelo competicao",
        BadgeModelId.Photographic => "Modelo fotografico",
        _ => throw new ArgumentOutOfRangeException(nameof(modelId), "The badge model is invalid.")
    };

    private static string GetSexLabel(BirdSex sex) => sex switch
    {
        BirdSex.Male => "Macho",
        BirdSex.Female => "Femea",
        BirdSex.Unknown => "Nao informado",
        _ => throw new ArgumentOutOfRangeException(nameof(sex), "The bird sex is invalid.")
    };

    private static string FormatDate(DateOnly? date) =>
        date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Nao informado";

    private static int GetNodeGeneration(GenealogySnapshotNode node) =>
        node.Position.Count(character => character == '.') + 1;

    private static string GetPositionLabel(string position)
    {
        var segments = position.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || string.Equals(position, "root", StringComparison.OrdinalIgnoreCase))
        {
            return "Ave";
        }

        if (segments.Length == 1)
        {
            return segments[0].Equals("father", StringComparison.OrdinalIgnoreCase) ? "Pai" : "Mae";
        }

        var side = segments[0].Equals("father", StringComparison.OrdinalIgnoreCase) ? "paterno" : "materno";
        var isMale = segments[^1].Equals("father", StringComparison.OrdinalIgnoreCase);
        var degree = segments.Length switch
        {
            2 => "Avo",
            3 => "Bisavo",
            4 => "Trisavo",
            _ => $"Ascendente de {segments.Length - 1}a geracao"
        };
        if (segments.Length <= 4)
        {
            return $"{degree} {side}{(isMale ? "o" : "a")}";
        }

        return $"{degree} {side}";
    }

    private static void DrawTextColoredRight(
        StringBuilder content,
        double rightX,
        double y,
        double fontSize,
        string value,
        PdfColor color,
        double? maxWidth = null) {
        var printableWidth = value.Normalize(NormalizationForm.FormD)
            .Count(character => char.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark) * fontSize * 0.52;
        var width = Math.Min(maxWidth ?? double.MaxValue, printableWidth);
        DrawTextColored(content, rightX - width, y, fontSize, value, color, maxWidth);
    }

    private static void DrawTextColoredCenteredBold(
        StringBuilder content,
        double centerX,
        double y,
        double fontSize,
        string value,
        PdfColor color,
        double? maxWidth = null)
    {
        var printableWidth = value.Normalize(NormalizationForm.FormD)
            .Count(character => char.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark) * fontSize * 0.52;
        var width = Math.Min(maxWidth ?? double.MaxValue, printableWidth);
        DrawTextColoredBold(content, centerX - (width / 2), y, fontSize, value, color, maxWidth);
    }

    private static void DrawTrackedText(
        StringBuilder content,
        double centerX,
        double y,
        double fontSize,
        string value,
        PdfColor color,
        double tracking,
        bool bold)
    {
        var printable = value.Normalize(NormalizationForm.FormD)
            .Where(character => char.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Select(character => character <= 127 ? character : '?')
            .ToArray();
        var glyphWidth = fontSize * 0.52;
        var totalWidth = (printable.Length * glyphWidth) + (Math.Max(0, printable.Length - 1) * tracking);
        var cursor = centerX - (totalWidth / 2);
        foreach (var character in printable)
        {
            if (bold)
            {
                DrawTextColoredBold(content, cursor, y, fontSize, character.ToString(), color);
            }
            else
            {
                DrawTextColored(content, cursor, y, fontSize, character.ToString(), color);
            }

            cursor += glyphWidth + tracking;
        }
    }

    private sealed record CertificatePalette(
        bool IsDark,
        PdfColor Background,
        PdfColor HeaderFill,
        PdfColor Surface,
        PdfColor TreeFill,
        PdfColor ColumnFill,
        PdfColor Text,
        PdfColor NodeText,
        PdfColor Muted,
        PdfColor Primary,
        PdfColor Secondary,
        PdfColor Border,
        PdfColor Connector,
        PdfColor MaleFill,
        PdfColor MaleBorder,
        PdfColor MaleAccent,
        PdfColor FemaleFill,
        PdfColor FemaleBorder,
        PdfColor FemaleAccent,
        PdfColor UnknownFill,
        PdfColor UnknownBorder,
        PdfColor UnknownAccent,
        PdfColor FooterFill,
        PdfColor FooterText,
        PdfColor FooterMuted,
        PdfColor FooterLine,
        PdfColor Watermark,
        PdfColor LogoEmblemFill,
        PdfColor LogoBird,
        PdfColor LogoChest);

    private sealed record CertificateTreeNode(
        string Position,
        string Name,
        string? RingNumber,
        BirdSex? Sex);

    private sealed record CertificateNodeBox(
        int Level,
        double X,
        double Y,
        double Width,
        double Height,
        CertificateTreeNode Node)
    {
        public double CenterY => Y + (Height / 2);
    }

    private readonly record struct CertificateNodeStyle(
        PdfColor Fill,
        PdfColor Border,
        PdfColor Text,
        PdfColor Icon);

    private sealed record NodeBox(
        int Level,
        double X,
        double Y,
        double Width,
        double Height,
        string Position,
        string Name,
        string? RingNumber,
        string? Sex)
    {
        public double CenterY => Y + (Height / 2);
    }

    private sealed record BadgeNodeBox(
        GenealogySnapshotNode Node,
        double X,
        double Y,
        double Width,
        double Height)
    {
        public double CenterY => Y + (Height / 2);
    }
}
