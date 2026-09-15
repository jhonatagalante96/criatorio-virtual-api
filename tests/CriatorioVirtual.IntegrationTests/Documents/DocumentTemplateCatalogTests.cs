using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.Documents;
using CriatorioVirtual.Infrastructure.Documents;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Documents;

public sealed class DocumentTemplateCatalogTests
{
    private const string EmptyPhotoDataUri =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    [Fact]
    public void BindGenealogyCertificate_UsesEmbeddedHtmlCssAndSnapshotValues()
    {
        var snapshot = CreateSnapshot(
            genealogy:
            [
                new GenealogySnapshotNode("father", "Pai real", "654321", BirdSex.Male, null),
                new GenealogySnapshotNode("father.father", "Avo real", null, BirdSex.Male, null)
            ]);

        var html = DocumentTemplateCatalog.BindGenealogyCertificate(
            snapshot,
            GenealogyCertificateModelId.Institutional);

        Assert.Contains("<style>", html, StringComparison.Ordinal);
        Assert.Contains("Pai real", html, StringComparison.Ordinal);
        Assert.Contains("Avo real", html, StringComparison.Ordinal);
        Assert.Contains("data:image/jpeg;base64,", html, StringComparison.Ordinal);
        Assert.DoesNotContain(EmptyPhotoDataUri, html, StringComparison.Ordinal);
        Assert.Contains("N&#227;o informado", html, StringComparison.Ordinal);
        Assert.DoesNotContain("RIO NEGRO", html, StringComparison.Ordinal);
        Assert.DoesNotContain("PANTANÃO", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BindProvenanceDocument_UsesRealDefaultPhotoWhenSnapshotHasNoPhoto()
    {
        var html = DocumentTemplateCatalog.BindProvenanceDocument(CreateSnapshot());

        Assert.Contains("data:image/jpeg;base64,", html, StringComparison.Ordinal);
        Assert.DoesNotContain(EmptyPhotoDataUri, html, StringComparison.Ordinal);
    }

    [Fact]
    public void BindProvenanceDocument_UsesDynamicAncestryAndPhotoDataUri()
    {
        var snapshot = CreateSnapshot(
            photo: new DocumentPhotoSnapshot("bird.png", "image/png", [1, 2, 3]),
            genealogy:
            [
                new GenealogySnapshotNode("father", "Pai real", null, BirdSex.Male, null),
                new GenealogySnapshotNode("father.father", "Avo paterno real", null, BirdSex.Male, null)
            ]);

        var html = DocumentTemplateCatalog.BindProvenanceDocument(snapshot);

        Assert.Contains("data:image/png;base64,AQID", html, StringComparison.Ordinal);
        Assert.Contains("Avo paterno real", html, StringComparison.Ordinal);
        Assert.Contains("N&#227;o informado", html, StringComparison.Ordinal);
        Assert.DoesNotContain("PANTANÃO", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BindGenealogyCertificate_UsesTheSamePhotoAlignmentInPremiumAndModernModels()
    {
        var snapshot = CreateSnapshot();

        var premium = DocumentTemplateCatalog.BindGenealogyCertificate(
            snapshot,
            GenealogyCertificateModelId.ClassicPremium);
        var modern = DocumentTemplateCatalog.BindGenealogyCertificate(
            snapshot,
            GenealogyCertificateModelId.Modern);

        Assert.Contains("object-position:right center", premium, StringComparison.Ordinal);
        Assert.Contains("object-position:right center", modern, StringComparison.Ordinal);
        Assert.Contains(".portrait>img{display:block", premium, StringComparison.Ordinal);
        Assert.Contains(".visual>.bird{display:block", modern, StringComparison.Ordinal);
    }

    private static BirdDocumentSnapshot CreateSnapshot(
        DocumentPhotoSnapshot? photo = null,
        IReadOnlyCollection<GenealogySnapshotNode>? genealogy = null) => new(
        Guid.NewGuid(),
        "Ave real",
        "123456",
        BirdSex.Female,
        "Canário",
        new DateOnly(2024, 2, 3),
        "Criatório Azul",
        photo,
        genealogy);
}
