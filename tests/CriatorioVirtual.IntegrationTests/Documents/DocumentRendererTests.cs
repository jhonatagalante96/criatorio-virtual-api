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

        var rendered = await new PdfDocumentRenderer().RenderAsync(request);
        var pdf = System.Text.Encoding.ASCII.GetString(rendered.Content);
        var text = ExtractPdfText(rendered.Content);

        Assert.Equal("application/pdf", rendered.ContentType);
        Assert.Equal("%PDF-1.4", pdf[..8]);
        Assert.Equal(1, rendered.PageCount);
        Assert.True(rendered.WidthMillimeters > rendered.HeightMillimeters);
        Assert.Contains("/MediaBox [0 0", pdf, StringComparison.Ordinal);
        Assert.Contains("Nome", text, StringComparison.Ordinal);
        Assert.Contains("Número da anilha", text, StringComparison.Ordinal);
        Assert.Contains("123456", text, StringComparison.Ordinal);
        Assert.Contains("Espécie", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderBadgeAsync_AddsClassicStructuredReverseWhenGenealogyIsSelected()
    {
        var request = new DocumentRenderRequest(
            BirdDocumentType.Badge,
            CreateSnapshot(),
            new BadgeRenderConfiguration(
                BadgeModelId.Photographic,
                BadgePrintSize.Large,
                [DocumentField.Name, DocumentField.GenealogyTree]));

        var rendered = await new PdfDocumentRenderer().RenderAsync(request);
        var text = ExtractPdfText(rendered.Content);

        Assert.Equal(2, rendered.PageCount);
        Assert.Contains("Árvore Genealógica", text, StringComparison.Ordinal);
        Assert.Contains("Pai", text, StringComparison.Ordinal);
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

        var rendered = await new PdfDocumentRenderer().RenderAsync(request);
        var pdf = System.Text.Encoding.ASCII.GetString(rendered.Content);
        var text = ExtractPdfText(rendered.Content);

        Assert.StartsWith("%PDF-1.4", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("Visualizacao da foto indisponivel", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssembleAsync_CombinesAllPagesWithoutChangingBadgeDimensions()
    {
        var renderer = new PdfDocumentRenderer();
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
        Assert.Equal(3, aggregate.PageCount);
        Assert.Equal(first.WidthMillimeters, aggregate.WidthMillimeters);
        Assert.Equal(first.HeightMillimeters, aggregate.HeightMillimeters);
        Assert.StartsWith("%PDF-1.4", pdf, StringComparison.Ordinal);
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

        var rendered = await new PdfDocumentRenderer().RenderAsync(request);
        var pdf = System.Text.Encoding.ASCII.GetString(rendered.Content);

        Assert.Equal(1, rendered.PageCount);
        Assert.Equal(297, rendered.WidthMillimeters);
        Assert.Equal(210, rendered.HeightMillimeters);
        Assert.Contains(ToHex("CERTIFICADO DE GENEALOGIA"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("DOCUMENTO INTERNO DO CRIATORIO VIRTUAL"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("ARVORE GENEALOGICA"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("INSTITUCIONAL CLARO"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("Nao informado"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("GERADO PELO CRIATORIO VIRTUAL"), pdf, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(GenealogyCertificateModelId.ClassicPremium, "CLASSICO PREMIUM")]
    [InlineData(GenealogyCertificateModelId.Institutional, "INSTITUCIONAL CLARO")]
    [InlineData(GenealogyCertificateModelId.Modern, "MODERNO")]
    public async Task RenderGenealogyCertificateAsync_ProducesLandscapeA4ForEveryModel(
        GenealogyCertificateModelId model,
        string modelLabel)
    {
        var request = new DocumentRenderRequest(
            BirdDocumentType.GenealogyCertificate,
            CreateSnapshot(),
            certificate: new GenealogyCertificateRenderConfiguration(model));

        var rendered = await new PdfDocumentRenderer().RenderAsync(request);
        var pdf = System.Text.Encoding.ASCII.GetString(rendered.Content);

        Assert.Equal(1, rendered.PageCount);
        Assert.Equal(297, rendered.WidthMillimeters);
        Assert.Equal(210, rendered.HeightMillimeters);
        Assert.Contains(ToHex(modelLabel), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("TRISAVOS"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("Nao informado"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("AVE PRINCIPAL"), pdf, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderGenealogyCertificateAsync_UsesDistinctCompositionsAndOfficialBrandMark()
    {
        var renderer = new PdfDocumentRenderer();
        var rendered = new Dictionary<GenealogyCertificateModelId, string>();
        foreach (var model in Enum.GetValues<GenealogyCertificateModelId>())
        {
            var document = await renderer.RenderAsync(new DocumentRenderRequest(
                BirdDocumentType.GenealogyCertificate,
                CreateSnapshot(),
                certificate: new GenealogyCertificateRenderConfiguration(model)));
            rendered[model] = System.Text.Encoding.ASCII.GetString(document.Content);
        }

        Assert.Contains(ToHex("CLASSICO PREMIUM"), rendered[GenealogyCertificateModelId.ClassicPremium], StringComparison.Ordinal);
        Assert.Contains(ToHex("INSTITUCIONAL CLARO"), rendered[GenealogyCertificateModelId.Institutional], StringComparison.Ordinal);
        Assert.Contains(ToHex("LINHAGEM EM FOCO"), rendered[GenealogyCertificateModelId.Modern], StringComparison.Ordinal);
        Assert.NotEqual(rendered[GenealogyCertificateModelId.ClassicPremium], rendered[GenealogyCertificateModelId.Institutional]);
        Assert.NotEqual(rendered[GenealogyCertificateModelId.Institutional], rendered[GenealogyCertificateModelId.Modern]);
        foreach (var pdf in rendered.Values)
        {
            Assert.Contains(ToHex("CRIATORIO"), pdf, StringComparison.Ordinal);
            Assert.Contains(ToHex("VIRTUAL"), pdf, StringComparison.Ordinal);
            Assert.Contains(ToHex("GESTAO COM PAIXAO"), pdf, StringComparison.Ordinal);
            Assert.Contains("/BaseFont /Times-Bold", pdf, StringComparison.Ordinal);
        }
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

        var rendered = await new PdfDocumentRenderer().RenderAsync(request);
        var pdf = System.Text.Encoding.ASCII.GetString(rendered.Content);

        Assert.Contains(" BI /W ", pdf, StringComparison.Ordinal);
        Assert.Contains("/FlateDecode", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain(ToHex("FOTO OPCIONAL"), pdf, StringComparison.Ordinal);
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

        var rendered = await new PdfDocumentRenderer().RenderAsync(request);
        var pdf = System.Text.Encoding.ASCII.GetString(rendered.Content);

        Assert.Equal(1, rendered.PageCount);
        Assert.Equal(210, rendered.WidthMillimeters);
        Assert.Equal(297, rendered.HeightMillimeters);
        Assert.Contains(ToHex("DOCUMENTO DE PROCEDENCIA"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("IDENTIFICACAO DA AVE"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("ASCENDENCIA"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("DECLARACAO DE PROCEDENCIA"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("Declaramos, para fins de registro interno"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("Observacao: este documento nao substitui registros, declaracoes ou procedimentos oficiais do SISPASS/IBAMA."), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("13/09/2026"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("DP-123456"), pdf, StringComparison.Ordinal);
        Assert.Contains(" BI /W ", pdf, StringComparison.Ordinal);
        Assert.Contains("/FlateDecode", pdf, StringComparison.Ordinal);
    }

    private static BirdDocumentSnapshot CreateSnapshot(
        BreedingFarmDocumentSnapshot? breedingFarmDetails = null,
        DateTimeOffset? issuedAtUtc = null,
        DocumentPhotoSnapshot? photo = null) => new(
        Guid.NewGuid(),
        "Luna",
        "123456",
        BirdSex.Female,
        "Canário",
        new DateOnly(2024, 2, 3),
            "Criatório Azul",
            photo,
            genealogy:
            [
                new GenealogySnapshotNode("father", "Sol", "654321", BirdSex.Male, new DateOnly(2022, 1, 1))
            ],
            breedingFarmDetails: breedingFarmDetails,
            issuedAtUtc: issuedAtUtc);

    private static string ToHex(string value) =>
        Convert.ToHexString(System.Text.Encoding.ASCII.GetBytes(value));

    private static string ExtractPdfText(byte[] content)
    {
        using var document = PdfDocument.Open(content);
        return string.Join("\n", document.GetPages().Select(page => page.Text));
    }
}
