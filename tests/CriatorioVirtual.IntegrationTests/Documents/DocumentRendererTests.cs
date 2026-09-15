using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.Documents;
using CriatorioVirtual.Infrastructure.Documents;
using UglyToad.PdfPig;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Documents;

public sealed class DocumentRendererTests
{
    public static IEnumerable<object[]> BadgeModelsAndSizes()
    {
        foreach (var model in Enum.GetValues<BadgeModelId>())
        {
            foreach (var size in Enum.GetValues<BadgePrintSize>())
            {
                yield return [model, size];
            }
        }
    }

    [Theory]
    [MemberData(nameof(BadgeModelsAndSizes))]
    public async Task RenderBadgeAsync_ProducesLandscapePdfForEveryFixedModelAndSize(
        BadgeModelId model,
        BadgePrintSize size)
    {
        var request = new DocumentRenderRequest(
            BirdDocumentType.Badge,
            CreateSnapshot(),
            new BadgeRenderConfiguration(
                model,
                size,
                [DocumentField.Name, DocumentField.RingNumber, DocumentField.Species]));

        using var renderer = new PdfDocumentRenderer();
        var rendered = await renderer.RenderAsync(request);
        var pdf = System.Text.Encoding.ASCII.GetString(rendered.Content);
        var text = ExtractPdfText(rendered.Content);

        Assert.Equal("application/pdf", rendered.ContentType);
        Assert.Equal("%PDF-1.4", pdf[..8]);
        Assert.Equal(1, rendered.PageCount);
        Assert.Equal(297, rendered.WidthMillimeters);
        Assert.Equal(210, rendered.HeightMillimeters);
        using var pdfDocument = PdfDocument.Open(rendered.Content);
        Assert.InRange(pdfDocument.GetPage(1).Width, 841, 843);
        Assert.InRange(pdfDocument.GetPage(1).Height, 594, 596);
        Assert.Contains("Nome", text, StringComparison.Ordinal);
        Assert.Contains("Número da anilha", text, StringComparison.Ordinal);
        Assert.Contains("123456", text, StringComparison.Ordinal);
        Assert.Contains("Espécie", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderBadgeAsync_PlacesFrontAndReverseOnTheSameSheetWhenGenealogyIsSelected()
    {
        var request = new DocumentRenderRequest(
            BirdDocumentType.Badge,
            CreateSnapshot(),
            new BadgeRenderConfiguration(
                BadgeModelId.Photographic,
                BadgePrintSize.Large,
                [DocumentField.Name, DocumentField.GenealogyTree]));

        using var renderer = new PdfDocumentRenderer();
        var rendered = await renderer.RenderAsync(request);
        var text = ExtractPdfText(rendered.Content);

        Assert.Equal(1, rendered.PageCount);
        Assert.Contains("Árvore Genealógica", text, StringComparison.Ordinal);
        Assert.Contains("Pai", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderBadgeAsync_UsesPortugueseFemaleLabel()
    {
        var htmlRenderer = new CapturingHtmlToPdfRenderer();
        var request = new DocumentRenderRequest(
            BirdDocumentType.Badge,
            CreateSnapshot(),
            new BadgeRenderConfiguration(
                BadgeModelId.Classic,
                BadgePrintSize.Medium,
                [DocumentField.Sex]));

        using var renderer = new PdfDocumentRenderer(htmlRenderer);
        await renderer.RenderAsync(request);

        Assert.Contains("F&#234;mea", htmlRenderer.Html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Femea<", htmlRenderer.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderBadgeAsync_UsesEachGenealogyBirdPhotoBeforeTheGenericFallback()
    {
        var htmlRenderer = new CapturingHtmlToPdfRenderer();
        var request = new DocumentRenderRequest(
            BirdDocumentType.Badge,
            CreateSnapshot(
                genealogy:
                [
                    new GenealogySnapshotNode(
                        "father",
                        "Pai real",
                        "654321",
                        BirdSex.Male,
                        null,
                        new DocumentPhotoSnapshot("father.png", "image/png", [4, 5, 6])),
                    new GenealogySnapshotNode(
                        "mother",
                        "Mãe real",
                        "654322",
                        BirdSex.Female,
                        null,
                        new DocumentPhotoSnapshot("mother.png", "image/png", [7, 8, 9]))
                ]),
            new BadgeRenderConfiguration(
                BadgeModelId.Classic,
                BadgePrintSize.Medium,
                [DocumentField.Name, DocumentField.GenealogyTree]));

        using var renderer = new PdfDocumentRenderer(htmlRenderer);
        await renderer.RenderAsync(request);

        Assert.Contains("data:image/png;base64,BAUG", htmlRenderer.Html, StringComparison.Ordinal);
        Assert.Contains("data:image/png;base64,BwgJ", htmlRenderer.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderBadgeAsync_BindsBothSidesInsideOneBadgeViewport()
    {
        var htmlRenderer = new CapturingHtmlToPdfRenderer();
        var request = new DocumentRenderRequest(
            BirdDocumentType.Badge,
            CreateSnapshot(),
            new BadgeRenderConfiguration(
                BadgeModelId.Competition,
                BadgePrintSize.Medium,
                [DocumentField.Name, DocumentField.GenealogyTree]));

        using var renderer = new PdfDocumentRenderer(htmlRenderer);
        await renderer.RenderAsync(request);

        Assert.Equal(1, CountOccurrences(htmlRenderer.Html, "class=\"badge-viewport\""));
        Assert.Contains("flex-flow: row nowrap", htmlRenderer.Html, StringComparison.Ordinal);
        Assert.True(
            htmlRenderer.Html.IndexOf("competition--front", StringComparison.Ordinal) <
            htmlRenderer.Html.IndexOf("competition--back", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RenderBadgeAsync_EmbedsSupportedPngPhotoInThePhotographicLayout()
    {
        var request = new DocumentRenderRequest(
            BirdDocumentType.Badge,
            CreateSnapshot(photo: new DocumentPhotoSnapshot(
                "bird.png",
                "image/png",
                Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="))),
            new BadgeRenderConfiguration(
                BadgeModelId.Photographic,
                BadgePrintSize.Large,
                [DocumentField.Name, DocumentField.BirdPhoto]));

        using var renderer = new PdfDocumentRenderer();
        var rendered = await renderer.RenderAsync(request);
        var pdf = System.Text.Encoding.ASCII.GetString(rendered.Content);
        var text = ExtractPdfText(rendered.Content);

        Assert.StartsWith("%PDF-1.4", pdf, StringComparison.Ordinal);
        Assert.Contains("/Subtype /Image", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("Visualizacao da foto indisponivel", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderBadgeAsync_UsesProvidedPhotoInsteadOfThePlaceholder()
    {
        var photo = new DocumentPhotoSnapshot("primary.png", "image/png", [1, 2, 3]);
        var htmlRenderer = new CapturingHtmlToPdfRenderer();
        var request = new DocumentRenderRequest(
            BirdDocumentType.Badge,
            CreateSnapshot(photo: photo),
            new BadgeRenderConfiguration(
                BadgeModelId.Classic,
                BadgePrintSize.Medium,
                [DocumentField.Name, DocumentField.BirdPhoto]));

        using var renderer = new PdfDocumentRenderer(htmlRenderer);
        await renderer.RenderAsync(request);

        Assert.Contains(
            "data:image/png;base64,AQID",
            htmlRenderer.Html,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "assets.bird-placeholder.svg",
            htmlRenderer.Html,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderBadgeAsync_UsesRealDefaultPhotoWhenSnapshotHasNoPhoto()
    {
        var htmlRenderer = new CapturingHtmlToPdfRenderer();
        var request = new DocumentRenderRequest(
            BirdDocumentType.Badge,
            CreateSnapshot(),
            new BadgeRenderConfiguration(
                BadgeModelId.Classic,
                BadgePrintSize.Medium,
                [DocumentField.Name, DocumentField.BirdPhoto]));

        using var renderer = new PdfDocumentRenderer(htmlRenderer);
        await renderer.RenderAsync(request);

        Assert.Contains("data:image/jpeg;base64,", htmlRenderer.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("assets.bird-placeholder.svg", htmlRenderer.Html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(BadgeModelId.Classic)]
    [InlineData(BadgeModelId.Minimalist)]
    [InlineData(BadgeModelId.Competition)]
    [InlineData(BadgeModelId.Photographic)]
    public async Task RenderBadgeAsync_RendersSelectedBreedingFarmFields(
        BadgeModelId model)
    {
        var snapshot = CreateSnapshot(
            breedingFarmDetails: new BreedingFarmDocumentSnapshot(
                "Responsável Azul",
                "contato@azul.example",
                null,
                null,
                new BreedingFarmAddressDocumentSnapshot(
                    "Rua das Aves",
                    "123",
                    "Casa 2",
                    "Centro",
                    "Campinas",
                    "SP",
                    "13000-000")));
        var request = new DocumentRenderRequest(
            BirdDocumentType.Badge,
            snapshot,
            new BadgeRenderConfiguration(
                model,
                BadgePrintSize.Medium,
                [DocumentField.BreedingFarmName, DocumentField.BreedingFarmAddress]));
        using var renderer = new PdfDocumentRenderer();
        var rendered = await renderer.RenderAsync(request);
        var text = ExtractPdfText(rendered.Content);

        Assert.Contains("Criatório Azul", text, StringComparison.Ordinal);
        Assert.Contains("Rua das Aves", text, StringComparison.Ordinal);
        Assert.Contains("Campinas", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssembleAsync_CombinesAllPagesWithoutChangingBadgeDimensions()
    {
        using var renderer = new PdfDocumentRenderer();
        var first = await renderer.RenderAsync(new DocumentRenderRequest(
            BirdDocumentType.Badge,
            CreateSnapshot(),
            new BadgeRenderConfiguration(
                BadgeModelId.Classic,
                BadgePrintSize.Small,
                [DocumentField.Name])));
        var second = await renderer.RenderAsync(new DocumentRenderRequest(
            BirdDocumentType.Badge,
            CreateSnapshot(),
            new BadgeRenderConfiguration(
                BadgeModelId.Classic,
                BadgePrintSize.Small,
                [DocumentField.Name, DocumentField.GenealogyTree])));

        var aggregate = new PdfDocumentAssembler().Assemble([first, second], "badge-batch-test.pdf");
        var pdf = System.Text.Encoding.ASCII.GetString(aggregate.Content);
        var text = ExtractPdfText(aggregate.Content);

        Assert.Equal("application/pdf", aggregate.ContentType);
        Assert.Equal(2, aggregate.PageCount);
        Assert.Equal(first.WidthMillimeters, aggregate.WidthMillimeters);
        Assert.Equal(first.HeightMillimeters, aggregate.HeightMillimeters);
        Assert.StartsWith("%PDF-1.", pdf, StringComparison.Ordinal);
        Assert.Contains("Nome", text, StringComparison.Ordinal);
        Assert.Contains("Árvore Genealógica", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderGenealogyCertificateAsync_UsesInstitutionalModelByDefault()
    {
        var request = new DocumentRenderRequest(
            BirdDocumentType.GenealogyCertificate,
            CreateSnapshot(
                new BreedingFarmDocumentSnapshot(
                    "Responsável Azul",
                    "contato@azul.example",
                    "+55 11 99999-0000",
                    "REG-001")));

        using var renderer = new PdfDocumentRenderer();
        var rendered = await renderer.RenderAsync(request);
        var text = ExtractPdfText(rendered.Content);
        Assert.Equal(1, rendered.PageCount);
        Assert.Equal(297, rendered.WidthMillimeters);
        Assert.Equal(210, rendered.HeightMillimeters);
        Assert.Contains("CERTIFICADO", text, StringComparison.Ordinal);
        Assert.Contains("ÁRVORE GENEALÓGICA", text, StringComparison.Ordinal);
        Assert.Contains("Responsável Azul", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(GenealogyCertificateModelId.ClassicPremium, "Genealógico")]
    [InlineData(GenealogyCertificateModelId.Institutional, "ÁRVORE GENEALÓGICA")]
    [InlineData(GenealogyCertificateModelId.Modern, "CERTIFICADO DE GENEALOGIA")]
    public async Task RenderGenealogyCertificateAsync_ProducesLandscapeA4ForEveryModel(
        GenealogyCertificateModelId model,
        string modelMarker)
    {
        var request = new DocumentRenderRequest(
            BirdDocumentType.GenealogyCertificate,
            CreateSnapshot(),
            certificate: new GenealogyCertificateRenderConfiguration(model));

        using var renderer = new PdfDocumentRenderer();
        var rendered = await renderer.RenderAsync(request);
        var text = ExtractPdfText(rendered.Content);

        Assert.Equal(1, rendered.PageCount);
        Assert.Equal(297, rendered.WidthMillimeters);
        Assert.Equal(210, rendered.HeightMillimeters);
        Assert.Contains(modelMarker, text, StringComparison.Ordinal);
        Assert.Contains("Luna", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderGenealogyCertificateAsync_UsesDistinctCompositionsAndOfficialBrandMark()
    {
        using var renderer = new PdfDocumentRenderer();
        var rendered = new Dictionary<GenealogyCertificateModelId, string>();
        foreach (var model in Enum.GetValues<GenealogyCertificateModelId>())
        {
            var document = await renderer.RenderAsync(new DocumentRenderRequest(
                BirdDocumentType.GenealogyCertificate,
                CreateSnapshot(),
                certificate: new GenealogyCertificateRenderConfiguration(model)));
            rendered[model] = ExtractPdfText(document.Content);
        }

        Assert.Contains("Genealógico", rendered[GenealogyCertificateModelId.ClassicPremium], StringComparison.Ordinal);
        Assert.Contains("ÁRVORE GENEALÓGICA", rendered[GenealogyCertificateModelId.Institutional], StringComparison.Ordinal);
        Assert.Contains("CERTIFICADO DE GENEALOGIA", rendered[GenealogyCertificateModelId.Modern], StringComparison.Ordinal);
        Assert.NotEqual(rendered[GenealogyCertificateModelId.ClassicPremium], rendered[GenealogyCertificateModelId.Institutional]);
        Assert.NotEqual(rendered[GenealogyCertificateModelId.Institutional], rendered[GenealogyCertificateModelId.Modern]);
    }

    [Fact]
    public async Task RenderGenealogyCertificateAsync_EmbedsOptionalPhoto()
    {
        var request = new DocumentRenderRequest(
            BirdDocumentType.GenealogyCertificate,
            CreateSnapshot(photo: new DocumentPhotoSnapshot(
                "bird.png",
                "image/png",
                Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="))),
            certificate: new GenealogyCertificateRenderConfiguration(GenealogyCertificateModelId.Institutional));

        using var renderer = new PdfDocumentRenderer();
        var rendered = await renderer.RenderAsync(request);
        var pdf = System.Text.Encoding.ASCII.GetString(rendered.Content);

        Assert.Contains("/Subtype /Image", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderProvenanceDocumentAsync_ProducesPortraitA4PdfWithDeclarationAndOfficialDisclaimer()
    {
        var request = new DocumentRenderRequest(
            BirdDocumentType.ProvenanceDocument,
            CreateSnapshot(
                new BreedingFarmDocumentSnapshot(
                    "Responsável Azul",
                    "contato@azul.example",
                    null,
                    null),
                new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero)));

        using var renderer = new PdfDocumentRenderer();
        var rendered = await renderer.RenderAsync(request);
        var text = ExtractPdfText(rendered.Content);
        Assert.Equal(1, rendered.PageCount);
        Assert.Equal(210, rendered.WidthMillimeters);
        Assert.Equal(297, rendered.HeightMillimeters);
        Assert.Contains("DECLARAÇÃO DE PROCEDÊNCIA", text, StringComparison.Ordinal);
        Assert.Contains("SISPASS", text, StringComparison.Ordinal);
        Assert.Contains("13/09/2026", text, StringComparison.Ordinal);
        Assert.Contains("DP-123456", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ChromiumRenderer_RejectsUnboundedConcurrencyConfiguration()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ChromiumHtmlToPdfRenderer(new DocumentRenderingOptions
            {
                MaxConcurrentRenders = DocumentRenderingOptions.MaximumConcurrentRenders + 1
            }));

        Assert.Contains("between 1 and", exception.Message, StringComparison.Ordinal);
    }

    private static BirdDocumentSnapshot CreateSnapshot(
        BreedingFarmDocumentSnapshot? breedingFarmDetails = null,
        DateTimeOffset? issuedAtUtc = null,
        DocumentPhotoSnapshot? photo = null,
        IReadOnlyCollection<GenealogySnapshotNode>? genealogy = null) => new(
        Guid.NewGuid(),
        "Luna",
        "123456",
        BirdSex.Female,
        "Canário",
        new DateOnly(2024, 2, 3),
            "Criatório Azul",
            photo,
            genealogy ??
            [
                new GenealogySnapshotNode("father", "Sol", "654321", BirdSex.Male, new DateOnly(2022, 1, 1))
            ],
            breedingFarmDetails: breedingFarmDetails,
            issuedAtUtc: issuedAtUtc);

    private static string ExtractPdfText(byte[] content)
    {
        using var document = PdfDocument.Open(content);
        return string.Join("\n", document.GetPages().Select(page => page.Text));
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }

    private sealed class CapturingHtmlToPdfRenderer : IHtmlToPdfRenderer
    {
        public string Html { get; private set; } = string.Empty;

        public Task<byte[]> RenderAsync(
            string html,
            CancellationToken cancellationToken = default)
        {
            Html = html;
            return Task.FromResult(Array.Empty<byte>());
        }
    }
}
