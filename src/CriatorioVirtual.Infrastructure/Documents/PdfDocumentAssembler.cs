using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Application.Storage;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

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

        var first = documents.First();
        using var output = new PdfDocument();
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

            using var input = new MemoryStream(document.Content, writable: false);
            using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
            if (source.Pages.Count != document.PageCount)
            {
                throw new ArgumentException(
                    "The rendered document page count does not match its PDF content.",
                    nameof(documents));
            }

            foreach (var page in source.Pages)
            {
                output.AddPage(page);
            }
        }

        if (output.PageCount == 0)
        {
            throw new ArgumentException("The rendered documents do not contain any PDF pages.", nameof(documents));
        }

        var pageCount = output.PageCount;
        using var content = new MemoryStream();
        output.Save(content, closeStream: false);

        return new RenderedDocument(
            content.ToArray(),
            fileName,
            "application/pdf",
            pageCount,
            first.WidthMillimeters,
            first.HeightMillimeters);
    }
}
