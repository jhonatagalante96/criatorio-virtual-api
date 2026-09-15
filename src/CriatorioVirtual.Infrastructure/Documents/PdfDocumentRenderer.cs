using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.Documents;

namespace CriatorioVirtual.Infrastructure.Documents;

/// <summary>
/// Renders the authorized document snapshots with the Criatorio Virtual visual system.
/// All HTML/CSS document templates are rendered by the shared Chromium HTML-to-PDF service.
/// </summary>
public sealed class PdfDocumentRenderer : IDocumentRenderer, IDisposable
{
    private const double BadgePageWidthMillimeters = 297d;
    private const double BadgePageHeightMillimeters = 210d;
    private readonly IHtmlToPdfRenderer htmlToPdfRenderer;

    public PdfDocumentRenderer()
        : this(new ChromiumHtmlToPdfRenderer())
    {
    }

    public PdfDocumentRenderer(IHtmlToPdfRenderer htmlToPdfRenderer)
    {
        ArgumentNullException.ThrowIfNull(htmlToPdfRenderer);
        this.htmlToPdfRenderer = htmlToPdfRenderer;
    }

    public void Dispose()
    {
        (htmlToPdfRenderer as IDisposable)?.Dispose();
    }

    private static readonly IReadOnlyDictionary<BadgePrintSize, (double Width, double Height)> BadgeSizes =
        new Dictionary<BadgePrintSize, (double Width, double Height)>
        {
            [BadgePrintSize.Small] = (85.60, 53.98),
            [BadgePrintSize.Medium] = (105, 74),
            [BadgePrintSize.Large] = (125, 88)
        };

    public async Task<RenderedDocument> RenderAsync(
        DocumentRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Type == BirdDocumentType.GenealogyCertificate)
        {
            const double width = 297d;
            const double height = 210d;
            var html = DocumentTemplateCatalog.BindGenealogyCertificate(
                request.Snapshot,
                request.Certificate?.ModelId ?? GenealogyCertificateRenderConfiguration.DefaultModelId);
            var renderedPdf = await htmlToPdfRenderer.RenderAsync(html, cancellationToken).ConfigureAwait(false);
            return CreateRenderedDocument(request.Snapshot, renderedPdf, width, height, 1);
        }

        if (request.Type == BirdDocumentType.ProvenanceDocument)
        {
            const double width = 210d;
            const double height = 297d;
            var html = DocumentTemplateCatalog.BindProvenanceDocument(request.Snapshot);
            var renderedPdf = await htmlToPdfRenderer.RenderAsync(html, cancellationToken).ConfigureAwait(false);
            return CreateRenderedDocument(request.Snapshot, renderedPdf, width, height, 1);
        }

        var configuration = request.Badge ?? throw new ArgumentException("Badge configuration is required.", nameof(request));
        var dimensions = BadgeSizes[configuration.PrintSize];
        var badgeHtml = BadgeTemplateCatalog.Bind(
            request.Snapshot,
            configuration,
            dimensions.Width,
            dimensions.Height);
        var badgePdf = await htmlToPdfRenderer.RenderAsync(badgeHtml, cancellationToken).ConfigureAwait(false);
        return CreateRenderedDocument(
            request.Snapshot,
            badgePdf,
            BadgePageWidthMillimeters,
            BadgePageHeightMillimeters,
            1);
    }

    private static RenderedDocument CreateRenderedDocument(
        BirdDocumentSnapshot snapshot,
        byte[] content,
        double width,
        double height,
        int pageCount)
    {
        return new RenderedDocument(
            content,
            $"bird-{snapshot.BirdId:N}.pdf",
            "application/pdf",
            pageCount,
            width,
            height);
    }

}
