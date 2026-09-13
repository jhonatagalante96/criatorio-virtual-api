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
            const double width = 297d;
            const double height = 210d;
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
                pageIndex == pageCount - 1,
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
        bool lastPage,
        int pageNumber,
        int pageCount)
    {
        var width = widthMillimeters * PointsPerMillimeter;
        var height = heightMillimeters * PointsPerMillimeter;
        var content = new StringBuilder();
        DrawA4Canvas(content, width, height);
        DrawA4Header(content, width, height, "Declaracao de procedencia", "Documento de procedencia", continuation);
        if (continuation)
        {
            DrawAncestorGrid(content, nodes, 34, 112, width - 68, height - 215, "Pais e ancestrais registrados");
        }
        else
        {
            DrawIdentityPanel(content, snapshot, 34, 112, 220, height - 215, "Procedencia da ave");
            DrawAncestorGrid(content, nodes, 272, 158, width - 306, height - 266, "Pais e ancestrais registrados");
            DrawTextColored(content, 272, 143, 6.2, "A procedencia considera apenas registros cadastrados; nao estabelece validade legal automatica", Muted, width - 306);
            if (lastPage)
            {
                DrawSignatureBox(content, width - 246, 114, 210, 35);
            }
        }

        DrawA4Footer(content, width, pageNumber, pageCount, "Documento interno - nao substitui o registro do SISPASS ou IBAMA");
        DrawTextRight(content, width - 36, height - 69, 6.5, $"Emitido: {snapshot.IssuedAtUtc?.ToString("dd/MM/yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "Nao informado"}", maxWidth: 190);
        return content.ToString();
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
