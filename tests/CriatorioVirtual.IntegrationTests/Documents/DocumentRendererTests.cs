using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.Documents;
using CriatorioVirtual.Infrastructure.Documents;
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

        Assert.Equal("application/pdf", rendered.ContentType);
        Assert.Equal("%PDF-1.4", pdf[..8]);
        Assert.Equal(1, rendered.PageCount);
        Assert.True(rendered.WidthMillimeters > rendered.HeightMillimeters);
        Assert.Contains("/MediaBox [0 0", pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("Nome"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("Numero da anilha"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("Especie"), pdf, StringComparison.Ordinal);
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
        var pdf = System.Text.Encoding.ASCII.GetString(rendered.Content);

        Assert.Equal(2, rendered.PageCount);
        Assert.Contains(ToHex("Arvore genealogica"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("Pai"), pdf, StringComparison.Ordinal);
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

        Assert.Contains(" BI /W ", pdf, StringComparison.Ordinal);
        Assert.Contains("/FlateDecode", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain(ToHex("Visualizacao da foto indisponivel"), pdf, StringComparison.Ordinal);
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

        Assert.Equal("application/pdf", aggregate.ContentType);
        Assert.Equal(3, aggregate.PageCount);
        Assert.Equal(first.WidthMillimeters, aggregate.WidthMillimeters);
        Assert.Equal(first.HeightMillimeters, aggregate.HeightMillimeters);
        Assert.StartsWith("%PDF-1.4", pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("Nome"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("Arvore genealogica"), pdf, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderGenealogyCertificateAsync_ProducesLandscapeA4PdfFromSnapshot()
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
        Assert.Contains(ToHex("Certificado genealogico"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("Documento interno - nao substitui o registro oficial"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("Pai"), pdf, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderProvenanceDocumentAsync_ProducesLandscapeA4PdfWithIssueDateAndSignature()
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
        Assert.Equal(297, rendered.WidthMillimeters);
        Assert.Equal(210, rendered.HeightMillimeters);
        Assert.Contains(ToHex("Documento de procedencia"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("Documento interno - nao substitui o registro do SISPASS ou IBAMA"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("A procedencia considera apenas registros cadastrados; nao estabelece validade legal automatica"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("Emitido: 13/09/2026 12:00 UTC"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("Assinatura manual"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("Pais e ancestrais registrados"), pdf, StringComparison.Ordinal);
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
}
