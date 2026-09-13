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
