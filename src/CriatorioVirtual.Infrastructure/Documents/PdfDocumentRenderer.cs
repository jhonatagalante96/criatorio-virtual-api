using System.Globalization;
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
            var certificatePages = CreateGenealogyCertificatePages(request.Snapshot, width, height);
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
        var headerHeight = Math.Max(28, height * 0.25);
        var footerHeight = Math.Max(19, height * 0.15);
        DrawFilledRectangle(content, 0, height - headerHeight, width, headerHeight, Forest);
        DrawBrandLockup(content, 8, height - headerHeight + 5, Math.Clamp(headerHeight - 8, 14, 24), White, compact: true);
        DrawTextColoredRight(content, width - 8, height - 14, 5.4, "CRACHA DE IDENTIFICACAO", White, maxWidth: width * 0.38);
        DrawTextColoredRight(content, width - 8, height - 23, 4.5, "Modelo classico", Mint, maxWidth: width * 0.38);

        var bodyY = footerHeight + 5;
        var bodyHeight = height - headerHeight - footerHeight - 9;
        var photoWidth = fields.Contains(DocumentField.BirdPhoto) ? Math.Min(width * 0.29, bodyHeight * 0.9) : 0;
        if (photoWidth > 0)
        {
            DrawBadgePhoto(content, snapshot, 8, bodyY + 2, photoWidth, bodyHeight - 4, Forest);
        }

        var fieldsX = photoWidth > 0 ? 8 + photoWidth + 7 : 8;
        var fieldsWidth = width - fieldsX - 8;
        DrawBadgeFields(content, snapshot, fields, fieldsX, bodyY, fieldsWidth, bodyHeight, Ink, White);
        DrawBadgeFooter(content, snapshot, width, footerHeight, Forest, White, "Qualidade em cada geracao.");
    }

    private static void DrawMinimalistBadge(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        IReadOnlyCollection<DocumentField> fields,
        double width,
        double height)
    {
        var footerHeight = Math.Max(18, height * 0.14);
        DrawFilledRectangle(content, 0, height - 5, width, 5, Forest);
        DrawFilledRectangle(content, 0, 0, 5, height, Mint);
        DrawBrandLockup(content, 11, height - 24, Math.Clamp(height * 0.11, 14, 22), Forest, compact: true);
        DrawTextColoredRight(content, width - 9, height - 12, 5.2, "MINIMALISTA", Forest, maxWidth: width * 0.28);
        DrawTextColoredRight(content, width - 9, height - 20, 4.3, "Limpo. Moderno. Elegante.", Muted, maxWidth: width * 0.34);
        DrawLeaf(content, width - 22, footerHeight + 5, 13, 17, Mint, mirrored: true);

        var bodyY = footerHeight + 8;
        var bodyHeight = height - footerHeight - 36;
        var photoWidth = fields.Contains(DocumentField.BirdPhoto) ? Math.Min(width * 0.31, bodyHeight) : 0;
        if (photoWidth > 0)
        {
            DrawBadgePhoto(content, snapshot, 12, bodyY, photoWidth, bodyHeight, Sage);
        }

        var fieldsX = photoWidth > 0 ? 12 + photoWidth + 8 : 12;
        DrawBadgeFields(content, snapshot, fields, fieldsX, bodyY, width - fieldsX - 10, bodyHeight, Ink, White);
        DrawBadgeFooter(content, snapshot, width, footerHeight, White, Forest, "Uma historia que vive.", stroke: Mint);
    }

    private static void DrawCompetitionBadge(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        IReadOnlyCollection<DocumentField> fields,
        double width,
        double height)
    {
        var footerHeight = Math.Max(21, height * 0.17);
        DrawFilledRectangle(content, 0, 0, width, height, DeepForest);
        DrawEllipse(content, width * 0.9, height * 0.98, width * 0.43, height * 0.6, new PdfColor(0.13, 0.42, 0.33));
        DrawBrandLockup(content, 9, height - 25, Math.Clamp(height * 0.12, 14, 23), White, compact: true);
        DrawTextColoredRight(content, width - 9, height - 12, 5.4, "EXCELENCIA EM AVES ORNAMENTAIS", GoldLight, maxWidth: width * 0.53);
        DrawLine(content, 9, height - 31, width - 9, height - 31, Gold, 1.2);

        var panelX = 8;
        var panelY = footerHeight + 6;
        var panelWidth = width - 16;
        var panelHeight = height - footerHeight - 43;
        DrawRoundedRectangle(content, panelX, panelY, panelWidth, panelHeight, 7, Paper, new PdfColor(0.78, 0.62, 0.28), 1);
        var photoWidth = fields.Contains(DocumentField.BirdPhoto) ? Math.Min(panelWidth * 0.28, panelHeight * 0.9) : 0;
        if (photoWidth > 0)
        {
            DrawBadgePhoto(content, snapshot, panelX + 6, panelY + 6, photoWidth, panelHeight - 12, Forest);
        }

        var fieldsX = photoWidth > 0 ? panelX + photoWidth + 13 : panelX + 8;
        DrawBadgeFields(content, snapshot, fields, fieldsX, panelY + 6, panelWidth - (fieldsX - panelX) - 8, panelHeight - 12, Ink, White, Gold);
        DrawCircle(content, width - 22, footerHeight + 13, 12, GoldLight, Gold, 1);
        DrawTextCentered(content, width - 22, footerHeight + 11, 5.4, "CV", bold: true);
        DrawBadgeFooter(content, snapshot, width, footerHeight, DeepForest, GoldLight, "Prestigio. Identidade. Tradicao.", stroke: Gold);
    }

    private static void DrawPhotographicBadge(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        IReadOnlyCollection<DocumentField> fields,
        double width,
        double height)
    {
        var footerHeight = Math.Max(20, height * 0.16);
        var bodyY = footerHeight + 5;
        var bodyHeight = height - footerHeight - 10;
        var photoWidth = fields.Contains(DocumentField.BirdPhoto) ? width * 0.43 : 0;
        if (photoWidth > 0)
        {
            DrawBadgePhoto(content, snapshot, 6, bodyY, photoWidth, bodyHeight, DeepForest);
        }
        else
        {
            DrawFilledRectangle(content, 0, bodyY, width * 0.34, bodyHeight, DeepForest);
            DrawLeaf(content, 14, bodyY + bodyHeight * 0.34, 20, 29, Sage);
        }

        var fieldsX = photoWidth > 0 ? photoWidth + 2 : 8;
        var fieldsWidth = width - fieldsX - 6;
        DrawRoundedRectangle(content, fieldsX, bodyY + 4, fieldsWidth, bodyHeight - 8, 7, new PdfColor(0.98, 0.99, 0.97), new PdfColor(0.76, 0.86, 0.81), 0.8);
        DrawBrandLockup(content, fieldsX + 7, height - 23, Math.Clamp(height * 0.11, 14, 22), Forest, compact: true);
        DrawTextColored(content, fieldsX + 8, height - 31, 4.6, "A IMAGEM EM PRIMEIRO PLANO", Muted, fieldsWidth - 15);
        DrawBadgeFields(content, snapshot, fields, fieldsX + 7, bodyY + 10, fieldsWidth - 14, bodyHeight - 45, Ink, White, Sage);
        DrawBadgeFooter(content, snapshot, width, footerHeight, DeepForest, White, "Aves que fazem historia.");
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
        var drawn = snapshot.Photo is { } photo && TryDrawImage(content, x + 2, y + 2, width - 4, height - 4, photo.ContentType, photo.Content);
        if (!drawn)
        {
            DrawLeaf(content, x + (width * 0.26), y + (height * 0.42), width * 0.38, height * 0.34, accent);
            DrawTextCentered(content, x + (width / 2), y + (height * 0.21), 5.2, snapshot.Photo is null ? "Foto indisponivel" : "Visualizacao da foto indisponivel", maxWidth: width - 6);
        }
    }

    private static void DrawBadgeFields(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        IReadOnlyCollection<DocumentField> selectedFields,
        double x,
        double y,
        double width,
        double height,
        PdfColor textColor,
        PdfColor cardFill,
        PdfColor? accent = null)
    {
        var fields = selectedFields
            .Where(field => field is not DocumentField.BirdPhoto and not DocumentField.GenealogyTree)
            .Select(field => GetFieldValue(field, snapshot))
            .ToArray();
        if (fields.Length == 0)
        {
            DrawTextColored(content, x, y + (height / 2), 6, "Selecione os campos de identificacao", Muted, width);
            return;
        }

        var columns = fields.Length == 1 ? 1 : 2;
        var rows = (int)Math.Ceiling(fields.Length / (double)columns);
        var gap = Math.Max(3, Math.Min(5, width * 0.025));
        var cellWidth = (width - ((columns - 1) * gap)) / columns;
        var cellHeight = (height - ((rows - 1) * gap)) / rows;
        var border = accent ?? Line;
        for (var index = 0; index < fields.Length; index++)
        {
            var column = index % columns;
            var row = index / columns;
            var cellX = x + (column * (cellWidth + gap));
            var cellY = y + height - ((row + 1) * cellHeight) - (row * gap);
            DrawRoundedRectangle(content, cellX, cellY, cellWidth, cellHeight, Math.Min(4, cellHeight * 0.2), cardFill, border, 0.45);
            var labelSize = Math.Clamp(cellHeight * 0.18, 4.2, 6.4);
            var valueSize = Math.Clamp(cellHeight * 0.28, 5.7, 9.2);
            DrawTextColored(content, cellX + 5, cellY + cellHeight - labelSize - 3, labelSize, fields[index].Label, Muted, cellWidth - 10);
            DrawTextColoredBold(content, cellX + 5, cellY + 5, valueSize, fields[index].Value, textColor, cellWidth - 10);
        }
    }

    private static void DrawBadgeFooter(
        StringBuilder content,
        BirdDocumentSnapshot snapshot,
        double width,
        double height,
        PdfColor fill,
        PdfColor text,
        string tagline,
        PdfColor? stroke = null)
    {
        DrawFilledRectangle(content, 0, 0, width, height, fill, stroke, stroke is null ? 0 : 0.5);
        DrawBrandLockup(content, 7, Math.Max(2, height * 0.1), Math.Max(11, height * 0.73), text, compact: true);
        var identifier = snapshot.RingNumber is null ? "Identidade da ave" : $"Anilha {snapshot.RingNumber}";
        DrawTextColoredRight(content, width - 7, height * 0.57, 4.8, identifier, text, maxWidth: width * 0.35);
        DrawTextColoredRight(content, width - 7, height * 0.22, 4.1, tagline, text, maxWidth: width * 0.45);
    }

    private static string CreateBadgeGenealogyPage(
        BirdDocumentSnapshot snapshot,
        double widthMillimeters,
        double heightMillimeters,
        BadgeModelId modelId)
    {
        var width = widthMillimeters * PointsPerMillimeter;
        var height = heightMillimeters * PointsPerMillimeter;
        var content = new StringBuilder();
        var accent = modelId == BadgeModelId.Competition ? Gold : Forest;
        var background = modelId == BadgeModelId.Competition ? DeepForest : Cloud;
        var foreground = modelId == BadgeModelId.Competition ? White : Ink;
        DrawFilledRectangle(content, 0, 0, width, height, background);
        DrawFilledRectangle(content, 0, height - 29, width, 29, accent);
        DrawBrandLockup(content, 8, height - 25, Math.Max(13, height * 0.2), modelId == BadgeModelId.Competition ? White : White, compact: true);
        DrawTextColoredRight(content, width - 8, height - 12, 6.7, "Arvore genealogica", modelId == BadgeModelId.Competition ? GoldLight : White, maxWidth: width * 0.42);
        DrawTextColored(content, 9, height - 39, 5.5, $"Ave: {snapshot.Name} | {snapshot.Species}", foreground, width - 18);
        DrawGenealogyTree(content, snapshot, snapshot.Genealogy, 8, 8, width - 16, height - 52, compact: true, accent, foreground);
        DrawTextColoredRight(content, width - 8, 3, 4.2, GetModelLabel(modelId), foreground, maxWidth: width * 0.4);
        return content.ToString();
    }

    private static IReadOnlyList<string> CreateGenealogyCertificatePages(
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
            pages.Add(CreateGenealogyCertificatePage(
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

    private static string CreateGenealogyCertificatePage(
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
        DrawA4Canvas(content, width, height);
        DrawA4Header(content, width, height, "Certificado genealogico", "Certificado genealogico", continuation);
        if (continuation)
        {
            DrawAncestorGrid(content, nodes, 34, 112, width - 68, height - 215, "Posicoes genealogicas");
        }
        else
        {
            DrawIdentityPanel(content, snapshot, 34, 112, 220, height - 215, "Ave registrada");
            DrawGenealogyTree(content, snapshot, nodes, 272, 112, width - 306, height - 215, compact: false, Forest, Ink);
            DrawCertificateSeal(content, width - 96, 82, 35);
        }

        DrawA4Footer(content, width, pageNumber, pageCount, "Documento interno - nao substitui o registro oficial");
        return content.ToString();
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
        DrawProvenanceLogo(content, 26, height - 91, 190, 61);
        DrawLine(content, 228, height - 31, 228, height - 102, Sage, 0.8);

        var farm = snapshot.BreedingFarmDetails;
        var farmName = string.IsNullOrWhiteSpace(snapshot.BreedingFarmName)
            ? "Criatorio Virtual"
            : snapshot.BreedingFarmName;
        DrawTextColoredBold(content, 246, height - 34, 13.2, farmName, DeepForest, 182);
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

        DrawLeaf(content, width - 78, height - 67, 31, 45, new PdfColor(0.78, 0.87, 0.76), mirrored: true);
        DrawLeaf(content, width - 51, height - 75, 22, 35, new PdfColor(0.84, 0.91, 0.82), mirrored: true);
        DrawTrackedText(content, width - 48, height - 61, 5.3, "MAIS AVES", DeepForest, 1, bold: false);
        DrawTrackedText(content, width - 48, height - 71, 5.3, "HISTORIAS", DeepForest, 1, bold: false);
        DrawTrackedText(content, width - 48, height - 81, 5.3, "QUE VOAM", DeepForest, 1, bold: false);
        DrawLine(content, width - 66, height - 91, width - 31, height - 91, Gold, 1.7);

        DrawLine(content, 26, headerBottom, width - 26, headerBottom, Gold, 0.7);
        if (continuation)
        {
            DrawTextColoredBold(content, 26, headerBottom - 19, 12, "Documento de procedencia - continuacao", DeepForest, width - 52);
        }
    }

    private static void DrawProvenanceTitle(StringBuilder content, double width)
    {
        const double titleY = 665;
        DrawLine(content, 32, titleY + 8, 91, titleY + 8, Gold, 1.8);
        DrawLine(content, width - 91, titleY + 8, width - 32, titleY + 8, Gold, 1.8);
        DrawTextColoredCenteredBold(content, width / 2, titleY, 21.2, "DOCUMENTO DE PROCEDENCIA", DeepForest, width - 170);

        const double pillX = 119;
        const double pillY = 632;
        const double pillWidth = 357;
        DrawRoundedRectangle(content, pillX, pillY, pillWidth, 22, 11, GoldLight, Gold, 0.8);
        DrawTrackedText(content, width / 2, pillY + 8, 7.2, "DOCUMENTO INTERNO DO CRIATORIO VIRTUAL", Ink, 2.3, bold: false);
        DrawTextItalic(content, 179, 608, 10.5, "Genetica, manejo e paixao em harmonia.", maxWidth: width - 358);

        DrawLine(content, 32, 586, 151, 586, Gold, 0.7);
        DrawLine(content, width - 151, 586, width - 32, 586, Gold, 0.7);
        DrawTrackedText(content, width / 2, 582, 5.5, "TRADICAO   *   CONHECIMENTO   *   PRESERVACAO", Forest, 1.6, bold: false);
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
        DrawProvenanceSectionHeader(content, x + 8, y + panelHeight - 30, panelWidth - 16, 27, "IDENTIFICACAO DA AVE", null);

        var reference = snapshot.RingNumber is { } ring
            ? $"ID: CV-{ring}"
            : $"ID: CV-{snapshot.BirdId.ToString("N")[..8].ToUpperInvariant()}";
        DrawRoundedRectangle(content, x + panelWidth - 123, y + panelHeight - 30, 115, 27, 7, GoldLight, Gold, 0.4);
        DrawTextCentered(content, x + panelWidth - 65.5, y + panelHeight - 20, 8.6, reference, bold: true, maxWidth: 105);

        const double photoX = 26;
        const double photoY = 407;
        const double photoWidth = 166;
        const double photoHeight = 140;
        DrawProvenancePhoto(content, snapshot, photoX, photoY, photoWidth, photoHeight);

        var infoX = 209d;
        var valueX = 300d;
        var rowY = 543d;
        const double rowStep = 20.5;
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
        DrawRoundedRectangle(content, x, y, width, height, 6, new PdfColor(0.9, 0.95, 0.89), Forest, 0.8);
        var drawn = snapshot.Photo is { } photo && TryDrawImage(content, x + 2, y + 2, width - 4, height - 4, photo.ContentType, photo.Content);
        if (drawn)
        {
            return;
        }

        DrawLeaf(content, x + 57, y + 62, 29, 49, Forest);
        DrawLeaf(content, x + 83, y + 48, 20, 37, Sage, mirrored: true);
        DrawTextCentered(content, x + (width / 2), y + 25, 7.2, "Foto da ave nao informada", bold: true, maxWidth: width - 18);
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
        DrawTextColoredBold(content, labelX, y, 8.3, $"{label}:", Ink, valueX - labelX - 8);
        if (sex is { } knownSex)
        {
            DrawSexSymbol(content, valueX + 5, y + 3, knownSex, 4.4);
            DrawTextColored(content, valueX + 17, y, 8.6, value, Ink, maxValueWidth ?? 235);
        }
        else if (emphasize)
        {
            DrawTextColoredBold(content, valueX, y, 9.2, value, Ink, maxValueWidth ?? 235);
        }
        else
        {
            DrawTextColored(content, valueX, y, 8.6, value, Ink, maxValueWidth ?? 235);
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
        DrawProvenanceSectionHeader(content, x + 8, y + panelHeight - 30, panelWidth - 16, 27, "ASCENDENCIA", "LINHAGENS QUE CONSTROEM HISTORIA");

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
        DrawTextColoredBold(content, x + 14, y + height - 13, 8.8, heading, Ink, width - 28);

        var name = node?.Name ?? "Nao informado";
        DrawSexSymbol(content, x + 20, y + 38, node?.Sex, 4.1, heading == "PAI" ? Forest : new PdfColor(0.87, 0.12, 0.25));
        DrawTextColoredBold(content, x + 37, y + height - 36, 8.4, name, Ink, width - 46);
        var ring = node?.RingNumber ?? "Nao informado";
        DrawTextColored(content, x + 37, y + 22, 6.7, $"Anilha {ring}", Ink, width - 46);
        var sex = node?.Sex is { } parentSex ? GetSexLabel(parentSex) : "Nao informado";
        DrawTextColored(content, x + 37, y + 12, 6.7, $"{sex} | Selvagem", Ink, width - 46);
        DrawTextColored(content, x + 37, y + 3, 6.5, $"Nascimento: {FormatDate(node?.BirthDate)}", Muted, width - 46);
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
        DrawProvenanceSectionHeader(content, x + 8, y + panelHeight - 30, panelWidth - 16, 27, "DECLARACAO DE PROCEDENCIA", null);

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
            DrawTrackedText(content, x + width - 116, y + 10, 4.6, trailingText, White, 1.1, bold: false);
        }
    }

    private static void DrawProvenanceLogo(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height)
    {
        var centerX = x + 29;
        var centerY = y + (height / 2);
        var radius = 27d;
        DrawCircle(content, centerX, centerY, radius, White, Gold, 1.7);
        DrawCircle(content, centerX, centerY, radius - 4, White, new PdfColor(0.88, 0.78, 0.52), 0.45);
        DrawLine(content, centerX - 16, centerY - 13, centerX + 17, centerY - 7, Forest, 1.5);
        DrawLeaf(content, centerX - 13, centerY - 8, 12, 20, Forest);
        DrawLeaf(content, centerX - 3, centerY - 5, 10, 16, Forest, mirrored: true);
        DrawLeaf(content, centerX + 5, centerY - 6, 11, 18, Forest);
        DrawEllipse(content, centerX + 3, centerY + 2, 9, 15, new PdfColor(0.04, 0.05, 0.05));
        DrawEllipse(content, centerX + 5, centerY + 16, 6, 6, new PdfColor(0.03, 0.04, 0.04));
        DrawEllipse(content, centerX + 8, centerY - 2, 5, 10, new PdfColor(0.63, 0.25, 0.08));
        DrawEllipse(content, centerX + 7, centerY + 18, 1.1, 1.1, White);
        DrawLine(content, centerX + 11, centerY + 15, centerX + 18, centerY + 13, Gold, 1.1);

        var textX = x + 65;
        DrawTextColored(content, textX, y + 39, 10.5, "CRIATORIO", DeepForest, width - 66);
        DrawTextColoredBold(content, textX, y + 17, 17.5, "VIRTUAL", DeepForest, width - 66);
        DrawTrackedText(content, textX + 50, y + 6, 3.5, "TECNOLOGIA A FAVOR", Forest, 0.45, bold: false);
        DrawTrackedText(content, textX + 47, y - 1, 3.5, "DA SUA CRIACAO", Forest, 0.45, bold: false);
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
        DrawTextColored(content, x + 15, y, 7.7, $"{label}: {value}", Ink, 205);
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
}
