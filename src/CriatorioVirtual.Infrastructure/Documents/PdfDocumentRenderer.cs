using System.Globalization;
using System.Text;
using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.Documents;
using static CriatorioVirtual.Infrastructure.Documents.PdfDocumentPrimitives;

namespace CriatorioVirtual.Infrastructure.Documents;

/// <summary>
/// Renders the document contracts with a small dependency-free PDF adapter.
/// The renderer only consumes the authorized in-memory snapshot supplied by the caller.
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

    public Task<RenderedDocument> RenderAsync(
        DocumentRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Type == BirdDocumentType.GenealogyCertificate)
        {
            const double certificateWidthMillimeters = 297d;
            const double certificateHeightMillimeters = 210d;
            var certificatePages = CreateGenealogyCertificatePages(
                request.Snapshot,
                certificateWidthMillimeters,
                certificateHeightMillimeters);
            var certificateContent = CreateFile(
                certificatePages,
                certificateWidthMillimeters,
                certificateHeightMillimeters);
            return Task.FromResult(new RenderedDocument(
                certificateContent,
                $"bird-{request.Snapshot.BirdId:N}.pdf",
                "application/pdf",
                certificatePages.Count,
                certificateWidthMillimeters,
                certificateHeightMillimeters));
        }

        if (request.Type == BirdDocumentType.ProvenanceDocument)
        {
            const double provenanceWidthMillimeters = 297d;
            const double provenanceHeightMillimeters = 210d;
            var provenancePages = CreateProvenancePages(
                request.Snapshot,
                provenanceWidthMillimeters,
                provenanceHeightMillimeters);
            var provenanceContent = CreateFile(
                provenancePages,
                provenanceWidthMillimeters,
                provenanceHeightMillimeters);
            return Task.FromResult(new RenderedDocument(
                provenanceContent,
                $"bird-{request.Snapshot.BirdId:N}.pdf",
                "application/pdf",
                provenancePages.Count,
                provenanceWidthMillimeters,
                provenanceHeightMillimeters));
        }

        var (widthMillimeters, heightMillimeters, selectedFields, modelLabel) = request.Type switch
        {
            BirdDocumentType.Badge => CreateBadgeLayout(request),
            _ => throw new ArgumentOutOfRangeException(nameof(request), "The document type is invalid.")
        };

        var pages = new List<string>
        {
            CreateBirdPage(request.Snapshot, widthMillimeters, heightMillimeters, selectedFields, modelLabel)
        };

        if (request.Type == BirdDocumentType.Badge && selectedFields.Contains(DocumentField.GenealogyTree))
        {
            pages.Add(CreateGenealogyPage(request.Snapshot, widthMillimeters, heightMillimeters));
        }

        var content = CreateFile(pages, widthMillimeters, heightMillimeters);
        return Task.FromResult(new RenderedDocument(
            content,
            $"bird-{request.Snapshot.BirdId:N}.pdf",
            "application/pdf",
            pages.Count,
            widthMillimeters,
            heightMillimeters));
    }

    private static IReadOnlyList<string> CreateProvenancePages(
        BirdDocumentSnapshot snapshot,
        double widthMillimeters,
        double heightMillimeters)
    {
        var nodes = snapshot.Genealogy.ToArray();
        const int nodesPerPage = 8;
        var pageCount = Math.Max(1, (int)Math.Ceiling(nodes.Length / (double)nodesPerPage));
        var pages = new List<string>(pageCount);
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            pages.Add(CreateProvenancePage(
                snapshot,
                widthMillimeters,
                heightMillimeters,
                nodes.Skip(pageIndex * nodesPerPage).Take(nodesPerPage),
                pageIndex > 0,
                pageIndex == pageCount - 1));
        }

        return pages;
    }

    private static string CreateProvenancePage(
        BirdDocumentSnapshot snapshot,
        double widthMillimeters,
        double heightMillimeters,
        IEnumerable<GenealogySnapshotNode> nodes,
        bool continuation,
        bool lastPage)
    {
        var width = widthMillimeters * PointsPerMillimeter;
        var height = heightMillimeters * PointsPerMillimeter;
        var margin = Math.Max(24, Math.Min(width, height) * 0.07);
        var content = new StringBuilder();
        DrawRectangle(content, margin / 2, margin / 2, width - margin, height - margin);
        DrawText(content, margin, height - margin - 18, 22, "Criatorio Virtual");
        DrawText(content, margin, height - margin - 46, 17, continuation ? "Provenance document - continued" : "Provenance document");
        DrawText(content, margin, height - margin - 68, 8, "Internal document - does not replace SISPASS or IBAMA registration");
        DrawText(content, margin, height - margin - 81, 8, "Provenance is based only on registered records; it does not establish automatic legal validity", width - (2 * margin));
        DrawText(content, width - margin - 170, height - margin - 18, 8, $"Issued: {snapshot.IssuedAtUtc?.ToString("dd/MM/yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "Not informed"}", 170);

        var y = height - margin - 98;
        DrawText(content, margin, y, 10, $"Bird: {snapshot.Name}");
        DrawText(content, margin + 220, y, 10, $"Ring number: {snapshot.RingNumber}");
        DrawText(content, margin + 430, y, 10, $"Sex: {GetSexLabel(snapshot.Sex)}");
        y -= 20;
        DrawText(content, margin, y, 10, $"Species: {snapshot.Species}");
        DrawText(content, margin + 430, y, 10, $"Birth date: {snapshot.BirthDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Not informed"}");
        y -= 25;
        DrawText(content, margin, y, 10, $"Breeding farm: {snapshot.BreedingFarmName}", width - (2 * margin));

        if (snapshot.BreedingFarmDetails is { } farm)
        {
            y -= 19;
            DrawText(content, margin, y, 9, $"Responsible: {farm.ResponsibleName}", width - (2 * margin));
            y -= 17;
            DrawText(content, margin, y, 9, $"Contact: {farm.ContactEmail}{FormatOptionalValue(farm.ContactPhone, " | Phone: ")}", width - (2 * margin));
            if (!string.IsNullOrWhiteSpace(farm.OfficialRegistrationNumber))
            {
                y -= 17;
                DrawText(content, margin, y, 9, $"Official registration: {farm.OfficialRegistrationNumber}", width - (2 * margin));
            }
        }

        y -= 28;
        DrawText(content, margin, y, 11, "Registered parents and ancestors");
        y -= 21;
        var nodeList = nodes.ToArray();
        if (nodeList.Length == 0)
        {
            DrawText(content, margin + 10, y, 9, "No parents or ancestors recorded in the authorized genealogy.");
        }
        else
        {
            foreach (var node in nodeList)
            {
                var details = string.Join(
                    " | ",
                    new[]
                    {
                        string.IsNullOrWhiteSpace(node.Name) ? "Not informed" : node.Name,
                        string.IsNullOrWhiteSpace(node.RingNumber) ? null : $"Ring: {node.RingNumber}",
                        node.Sex is { } sex ? $"Sex: {GetSexLabel(sex)}" : null,
                        node.BirthDate is { } birthDate ? $"Birth: {birthDate:dd/MM/yyyy}" : null
                    }.Where(value => value is not null));
                DrawText(content, margin + 10, y, 9, $"{node.Position}: {details}", width - (2 * margin) - 10);
                y -= 18;
            }
        }

        if (lastPage)
        {
            var signatureX = width / 2;
            var signatureY = margin + 24;
            var signatureWidth = width - signatureX - margin;
            DrawRectangle(content, signatureX, signatureY, signatureWidth, 38);
            DrawText(content, signatureX + 8, signatureY + 14, 9, "Manual signature", signatureWidth - 16);
        }

        return content.ToString();
    }

    private static IReadOnlyList<string> CreateGenealogyCertificatePages(
        BirdDocumentSnapshot snapshot,
        double widthMillimeters,
        double heightMillimeters)
    {
        var nodes = snapshot.Genealogy.ToArray();
        const int nodesPerPage = 9;
        var pageCount = Math.Max(1, (int)Math.Ceiling(nodes.Length / (double)nodesPerPage));
        var pages = new List<string>(pageCount);
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            pages.Add(CreateGenealogyCertificatePage(
                snapshot,
                widthMillimeters,
                heightMillimeters,
                nodes.Skip(pageIndex * nodesPerPage).Take(nodesPerPage),
                pageIndex > 0));
        }

        return pages;
    }

    private static string CreateGenealogyCertificatePage(
        BirdDocumentSnapshot snapshot,
        double widthMillimeters,
        double heightMillimeters,
        IEnumerable<GenealogySnapshotNode> nodes,
        bool continuation)
    {
        var width = widthMillimeters * PointsPerMillimeter;
        var height = heightMillimeters * PointsPerMillimeter;
        var margin = Math.Max(24, Math.Min(width, height) * 0.07);
        var content = new StringBuilder();
        DrawRectangle(content, margin / 2, margin / 2, width - margin, height - margin);
        DrawText(content, margin, height - margin - 18, 22, "Criatorio Virtual");
        DrawText(content, margin, height - margin - 46, 17, continuation ? "Genealogy certificate - continued" : "Genealogy certificate");
        DrawText(content, margin, height - margin - 68, 8, "Internal document - does not replace official registration");

        var y = height - margin - 98;
        DrawText(content, margin, y, 10, $"Bird: {snapshot.Name}");
        DrawText(content, margin + 220, y, 10, $"Ring number: {snapshot.RingNumber}");
        DrawText(content, margin + 430, y, 10, $"Sex: {GetSexLabel(snapshot.Sex)}");
        y -= 20;
        DrawText(content, margin, y, 10, $"Species: {snapshot.Species}");
        DrawText(content, margin + 430, y, 10, $"Birth date: {snapshot.BirthDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Not informed"}");
        y -= 25;
        DrawText(content, margin, y, 10, $"Breeding farm: {snapshot.BreedingFarmName}", width - (2 * margin));

        if (snapshot.BreedingFarmDetails is { } farm)
        {
            y -= 19;
            DrawText(content, margin, y, 9, $"Responsible: {farm.ResponsibleName}", width - (2 * margin));
            y -= 17;
            DrawText(content, margin, y, 9, $"Contact: {farm.ContactEmail}{FormatOptionalValue(farm.ContactPhone, " | Phone: ")}", width - (2 * margin));
            if (!string.IsNullOrWhiteSpace(farm.OfficialRegistrationNumber))
            {
                y -= 17;
                DrawText(content, margin, y, 9, $"Official registration: {farm.OfficialRegistrationNumber}", width - (2 * margin));
            }
        }

        y -= 28;
        DrawText(content, margin, y, 11, "Genealogy positions");
        y -= 21;
        var nodeList = nodes.ToArray();
        if (nodeList.Length == 0)
        {
            DrawText(content, margin + 10, y, 9, "No ancestors recorded in the authorized genealogy.");
        }
        else
        {
            foreach (var node in nodeList)
            {
                var details = string.Join(
                    " | ",
                    new[]
                    {
                        string.IsNullOrWhiteSpace(node.Name) ? "Not informed" : node.Name,
                        string.IsNullOrWhiteSpace(node.RingNumber) ? null : $"Ring: {node.RingNumber}",
                        node.Sex is { } sex ? $"Sex: {GetSexLabel(sex)}" : null,
                        node.BirthDate is { } birthDate ? $"Birth: {birthDate:dd/MM/yyyy}" : null
                    }.Where(value => value is not null));
                DrawText(content, margin + 10, y, 9, $"{node.Position}: {details}", width - (2 * margin) - 10);
                y -= 18;
            }
        }

        return content.ToString();
    }

    private static (double Width, double Height, IReadOnlyCollection<DocumentField> Fields, string ModelLabel) CreateBadgeLayout(
        DocumentRenderRequest request)
    {
        var configuration = request.Badge ?? throw new ArgumentException("Badge configuration is required.", nameof(request));
        var dimensions = BadgeSizes[configuration.PrintSize];
        return (
            dimensions.Width,
            dimensions.Height,
            configuration.SelectedFields,
            GetModelLabel(configuration.ModelId));
    }

    private static string CreateBirdPage(
        BirdDocumentSnapshot snapshot,
        double widthMillimeters,
        double heightMillimeters,
        IReadOnlyCollection<DocumentField> selectedFields,
        string modelLabel)
    {
        var width = widthMillimeters * PointsPerMillimeter;
        var height = heightMillimeters * PointsPerMillimeter;
        var margin = Math.Max(18, Math.Min(width, height) * 0.08);
        var content = new StringBuilder();
        DrawRectangle(content, margin / 2, margin / 2, width - margin, height - margin);
        DrawText(content, margin, height - margin - 16, Math.Max(12, Math.Min(width, height) * 0.075), "Criatorio Virtual");
        DrawText(content, margin, height - margin - 32, Math.Max(7, Math.Min(width, height) * 0.045), modelLabel);

        var contentTop = height - margin - 60;
        var contentWidth = width - (2 * margin);
        var photoWidth = selectedFields.Contains(DocumentField.BirdPhoto) ? Math.Min(contentWidth * 0.3, 110) : 0;
        var textWidth = contentWidth - photoWidth - (photoWidth > 0 ? 12 : 0);
        var y = contentTop;
        foreach (var field in selectedFields.Where(field => field != DocumentField.GenealogyTree && field != DocumentField.BirdPhoto))
        {
            if (y < margin + 20)
            {
                break;
            }

            var (label, value) = GetFieldValue(field, snapshot);
            DrawText(content, margin, y, Math.Max(7, Math.Min(width, height) * 0.045), label);
            DrawText(content, margin, y - 11, Math.Max(7, Math.Min(width, height) * 0.045), value, textWidth);
            y -= 29;
        }

        if (photoWidth > 0)
        {
            var photoX = margin + contentWidth - photoWidth;
            var photoHeight = Math.Min(height - contentTop - margin + 24, photoWidth * 0.75);
            DrawRectangle(content, photoX, height - contentTop - photoHeight, photoWidth, photoHeight);
            DrawText(
                content,
                photoX + 4,
                height - contentTop - (photoHeight / 2),
                Math.Max(7, Math.Min(width, height) * 0.04),
                snapshot.Photo is null ? "Photo unavailable" : "Bird photo",
                photoWidth - 8);
        }

        return content.ToString();
    }

    private static string CreateGenealogyPage(
        BirdDocumentSnapshot snapshot,
        double widthMillimeters,
        double heightMillimeters)
    {
        var width = widthMillimeters * PointsPerMillimeter;
        var height = heightMillimeters * PointsPerMillimeter;
        var margin = Math.Max(18, Math.Min(width, height) * 0.08);
        var content = new StringBuilder();
        DrawRectangle(content, margin / 2, margin / 2, width - margin, height - margin);
        DrawText(content, margin, height - margin - 16, Math.Max(11, Math.Min(width, height) * 0.07), "Genealogy tree");
        DrawText(content, margin, height - margin - 42, Math.Max(7, Math.Min(width, height) * 0.045), $"Bird: {snapshot.Name} | {snapshot.Species}");

        var y = height - margin - 70;
        foreach (var node in snapshot.Genealogy)
        {
            if (y < margin + 20)
            {
                break;
            }

            var nodeName = string.IsNullOrWhiteSpace(node.Name) ? "Not informed" : node.Name;
            DrawText(
                content,
                margin + 12,
                y,
                Math.Max(7, Math.Min(width, height) * 0.045),
                $"{node.Position}: {nodeName}{FormatRingNumber(node.RingNumber)}",
                width - (2 * margin) - 12);
            y -= 22;
        }

        return content.ToString();
    }

    private static (string Label, string Value) GetFieldValue(DocumentField field, BirdDocumentSnapshot snapshot) => field switch
    {
        DocumentField.Name => ("Name", snapshot.Name),
        DocumentField.RingNumber => ("Ring number", snapshot.RingNumber ?? "Not informed"),
        DocumentField.Sex => ("Sex", GetSexLabel(snapshot.Sex)),
        DocumentField.Species => ("Species", snapshot.Species),
        DocumentField.BirthDate => ("Birth date", snapshot.BirthDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Not informed"),
        DocumentField.BreedingFarmName => ("Breeding farm", snapshot.BreedingFarmName),
        DocumentField.BirdPhoto => ("Bird photo", snapshot.Photo is null ? "Not informed" : snapshot.Photo.FileName),
        DocumentField.GenealogyTree => ("Genealogy tree", snapshot.Genealogy.Count == 0 ? "Not informed" : "See reverse"),
        _ => throw new ArgumentOutOfRangeException(nameof(field), "The document field is invalid.")
    };

    private static string GetModelLabel(BadgeModelId modelId) => modelId switch
    {
        BadgeModelId.Classic => "Classic model",
        BadgeModelId.Minimalist => "Minimalist model",
        BadgeModelId.Competition => "Competition model",
        BadgeModelId.Photographic => "Photographic model",
        _ => throw new ArgumentOutOfRangeException(nameof(modelId), "The badge model is invalid.")
    };

    private static string GetSexLabel(BirdSex sex) => sex switch
    {
        BirdSex.Male => "Male",
        BirdSex.Female => "Female",
        BirdSex.Unknown => "Not informed",
        _ => throw new ArgumentOutOfRangeException(nameof(sex), "The bird sex is invalid.")
    };

    private static string FormatRingNumber(string? ringNumber) =>
        string.IsNullOrWhiteSpace(ringNumber) ? string.Empty : $" | Ring: {ringNumber}";

    private static string FormatOptionalValue(string? value, string prefix) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : prefix + value;

}
