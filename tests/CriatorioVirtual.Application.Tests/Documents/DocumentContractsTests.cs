using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.Documents;
using Xunit;

namespace CriatorioVirtual.Application.Tests.Documents;

public sealed class DocumentContractsTests
{
    [Fact]
    public void BadgeConfiguration_RejectsDuplicateOrUnknownFields()
    {
        Assert.Throws<ArgumentException>(() => new BadgeRenderConfiguration(
            BadgeModelId.Classic,
            BadgePrintSize.Small,
            [DocumentField.Name, DocumentField.Name]));

        Assert.Throws<ArgumentException>(() => new BadgeRenderConfiguration(
            BadgeModelId.Classic,
            BadgePrintSize.Small,
            [(DocumentField)99]));
    }

    [Fact]
    public void RenderRequest_RequiresBadgeConfigurationOnlyForBadge()
    {
        var snapshot = CreateSnapshot();

        Assert.Throws<ArgumentException>(() => new DocumentRenderRequest(BirdDocumentType.Badge, snapshot));

        var request = new DocumentRenderRequest(BirdDocumentType.GenealogyCertificate, snapshot);
        Assert.Equal(BirdDocumentType.GenealogyCertificate, request.Type);
        Assert.Null(request.Badge);
        Assert.NotNull(request.Certificate);
        Assert.Equal(GenealogyCertificateModelId.Institutional, request.Certificate!.ModelId);

        var classicRequest = new DocumentRenderRequest(
            BirdDocumentType.GenealogyCertificate,
            snapshot,
            certificate: new GenealogyCertificateRenderConfiguration(GenealogyCertificateModelId.ClassicPremium));
        Assert.Equal(GenealogyCertificateModelId.ClassicPremium, classicRequest.Certificate!.ModelId);

        Assert.Throws<ArgumentOutOfRangeException>(() => new GenealogyCertificateRenderConfiguration((GenealogyCertificateModelId)99));

        Assert.Throws<ArgumentException>(() => new DocumentRenderRequest(
            BirdDocumentType.ProvenanceDocument,
            snapshot,
            new BadgeRenderConfiguration(
                BadgeModelId.Classic,
                BadgePrintSize.Small,
                [DocumentField.Name])));
    }

    [Fact]
    public void PhotoFocus_RequiresNormalizedCoordinatesAndZoom()
    {
        var focus = new DocumentPhotoFocus(72, 38, 1.35);

        Assert.Equal(72, focus.X);
        Assert.Equal(38, focus.Y);
        Assert.Equal(1.35, focus.Zoom);
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentPhotoFocus(-1, 50, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentPhotoFocus(50, 101, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentPhotoFocus(50, 50, 0.99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentPhotoFocus(double.NaN, 50, 1));
    }

    [Fact]
    public void Snapshot_RejectsInvalidTenantIndependentBirdData()
    {
        Assert.Throws<ArgumentException>(() => new BirdDocumentSnapshot(
            Guid.Empty,
            "Luna",
            null,
            BirdSex.Female,
            "Canário",
            null,
            "Criatório"));

        Assert.Throws<ArgumentException>(() => new BirdDocumentSnapshot(
            Guid.NewGuid(),
            "Luna",
            "123",
            BirdSex.Female,
            "Canário",
            null,
            "Criatório"));
    }

    private static BirdDocumentSnapshot CreateSnapshot() => new(
        Guid.NewGuid(),
        "Luna",
        "123456",
        BirdSex.Female,
        "Canário",
        new DateOnly(2024, 2, 3),
        "Criatório Azul");
}
