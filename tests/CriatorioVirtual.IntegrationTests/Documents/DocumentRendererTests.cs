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

    private static BirdDocumentSnapshot CreateSnapshot(
        BreedingFarmDocumentSnapshot? breedingFarmDetails = null) => new(
        Guid.NewGuid(),
        "Luna",
        "123456",
        BirdSex.Female,
        "Canário",
        new DateOnly(2024, 2, 3),
            "Criatório Azul",
            genealogy:
            [
                new GenealogySnapshotNode("father", "Sol", "654321", BirdSex.Male, new DateOnly(2022, 1, 1))
            ],
            breedingFarmDetails: breedingFarmDetails);
}
