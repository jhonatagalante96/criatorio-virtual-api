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
        Assert.Contains("4E616D65", pdf, StringComparison.Ordinal);
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
        Assert.Contains("47656E65616C6F6779", pdf, StringComparison.Ordinal);
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
        Assert.DoesNotContain("Photo preview unavailable", pdf, StringComparison.Ordinal);
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
        Assert.Contains("4E616D65", pdf, StringComparison.Ordinal);
        Assert.Contains("47656E65616C6F6779", pdf, StringComparison.Ordinal);
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
        Assert.Contains("47656E65616C6F6779206365727469666963617465", pdf, StringComparison.Ordinal);
        Assert.Contains("496E7465726E616C20646F63756D656E74", pdf, StringComparison.Ordinal);
        Assert.Contains("666174686572", pdf, StringComparison.Ordinal);
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
        Assert.Contains("50726F76656E616E636520646F63756D656E74", pdf, StringComparison.Ordinal);
        Assert.Contains("496E7465726E616C20646F63756D656E74202D20646F6573206E6F74207265706C616365", pdf, StringComparison.Ordinal);
        Assert.Contains("50726F76656E616E6365206973206261736564206F6E6C79206F6E2072656769737465726564207265636F7264733B20697420646F6573206E6F742065737461626C697368206175746F6D61746963206C6567616C2076616C6964697479", pdf, StringComparison.Ordinal);
        Assert.Contains("4973737565643A2031332F30392F323032362031323A303020555443", pdf, StringComparison.Ordinal);
        Assert.Contains("4D616E75616C207369676E6174757265", pdf, StringComparison.Ordinal);
        Assert.Contains("5265676973746572656420706172656E747320616E6420616E636573746F7273", pdf, StringComparison.Ordinal);
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
}
