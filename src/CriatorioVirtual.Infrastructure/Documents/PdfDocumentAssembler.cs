using System.Text;
using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Application.Storage;

namespace CriatorioVirtual.Infrastructure.Documents;

public sealed class PdfDocumentAssembler : IPdfDocumentAssembler
{
    public RenderedDocument Assemble(
        IReadOnlyCollection<RenderedDocument> documents,
        string fileName)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (!PrivateObjectStorageFileValidation.TryValidateMetadata(
                fileName,
                "application/pdf",
                out var metadataError))
        {
            throw new ArgumentException(metadataError, nameof(fileName));
        }

        if (documents.Count == 0)
        {
            throw new ArgumentException("At least one rendered document is required.", nameof(documents));
        }

        var pages = new List<string>();
        var first = documents.First();
        foreach (var document in documents)
        {
            if (document.Content is null || document.Content.Length == 0 ||
                document.PageCount <= 0 ||
                document.WidthMillimeters <= 0 ||
                document.HeightMillimeters <= 0)
            {
                throw new ArgumentException("Every rendered document must contain valid PDF metadata.", nameof(documents));
            }

            if (Math.Abs(document.WidthMillimeters - first.WidthMillimeters) > 0.001 ||
                Math.Abs(document.HeightMillimeters - first.HeightMillimeters) > 0.001)
            {
                throw new ArgumentException(
                    "All rendered documents in a badge batch must use the same page dimensions.",
                    nameof(documents));
            }

            var documentPages = ExtractPageContents(document.Content);
            if (documentPages.Count != document.PageCount)
            {
                throw new ArgumentException(
                    "The rendered document page count does not match its PDF content.",
                    nameof(documents));
            }

            pages.AddRange(documentPages);
        }

        if (pages.Count == 0)
        {
            throw new ArgumentException("The rendered documents do not contain any PDF pages.", nameof(documents));
        }

        return new RenderedDocument(
            PdfDocumentPrimitives.CreateFile(
                pages,
                first.WidthMillimeters,
                first.HeightMillimeters),
            fileName,
            "application/pdf",
            pages.Count,
            first.WidthMillimeters,
            first.HeightMillimeters);
    }

    private static IReadOnlyCollection<string> ExtractPageContents(byte[] pdf)
    {
        var text = Encoding.ASCII.GetString(pdf);
        if (!text.StartsWith("%PDF-1.4", StringComparison.Ordinal))
        {
            throw new ArgumentException("The rendered document is not a supported PDF.", nameof(pdf));
        }

        const string streamMarker = "stream\n";
        const string endStreamMarker = "endstream";
        var pages = new List<string>();
        var searchStart = 0;
        while (true)
        {
            var streamStart = text.IndexOf(streamMarker, searchStart, StringComparison.Ordinal);
            if (streamStart < 0)
            {
                break;
            }

            var contentStart = streamStart + streamMarker.Length;
            var contentEnd = text.IndexOf(endStreamMarker, contentStart, StringComparison.Ordinal);
            if (contentEnd < 0)
            {
                throw new ArgumentException("The rendered document contains an incomplete PDF stream.", nameof(pdf));
            }

            pages.Add(text[contentStart..contentEnd]);
            searchStart = contentEnd + endStreamMarker.Length;
        }

        return pages;
    }
}
