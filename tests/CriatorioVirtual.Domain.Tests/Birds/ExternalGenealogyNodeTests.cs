using CriatorioVirtual.Domain.Birds;
using Xunit;

namespace CriatorioVirtual.Domain.Tests.Birds;

public sealed class ExternalGenealogyNodeTests
{
    [Fact]
    public void Constructor_NormalizesNameAndKeepsTreeScope()
    {
        var farmId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var node = new ExternalGenealogyNode(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            farmId,
            rootId,
            "  Pai externo  ",
            BirdSex.Male);

        Assert.Equal("Pai externo", node.Name);
        Assert.Equal(BirdSex.Male, node.Sex);
        Assert.Equal(farmId, node.BreedingFarmId);
        Assert.Equal(rootId, node.GenealogyRootId);
    }

    [Theory]
    [InlineData(BirdSex.Unknown)]
    public void Constructor_RejectsUnvalidatedSex(BirdSex sex)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExternalGenealogyNode(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Ancestral",
            sex));
    }

    [Fact]
    public void ParentLink_RejectsSelfAndMultipleSources()
    {
        var nodeId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => new ExternalGenealogyParentLink(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            nodeId,
            ExternalGenealogyParentLink.FatherPosition,
            null,
            nodeId,
            null,
            null,
            null,
            null,
            null,
            null));

        Assert.Throws<ArgumentException>(() => new ExternalGenealogyParentLink(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            ExternalGenealogyParentLink.FatherPosition,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Bird",
            BirdSex.Male,
            null,
            null,
            BirdStatus.Active));
    }

    [Fact]
    public void ParentLink_ValidatesExternalParentTreeAndPositionSex()
    {
        var farmId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var parent = new ExternalGenealogyNode(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            farmId,
            rootId,
            "Pai",
            BirdSex.Male);
        var link = new ExternalGenealogyParentLink(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            farmId,
            rootId,
            Guid.NewGuid(),
            null,
            ExternalGenealogyParentLink.FatherPosition,
            null,
            parent.Id,
            null,
            null,
            null,
            null,
            null,
            null);

        link.ValidateExternalParent(parent);

        var foreignParent = new ExternalGenealogyNode(
            parent.Id,
            DateTimeOffset.UtcNow,
            farmId,
            Guid.NewGuid(),
            "Pai estrangeiro",
            BirdSex.Male);
        Assert.Throws<InvalidOperationException>(() => link.ValidateExternalParent(foreignParent));
    }

    [Fact]
    public void ParentLink_AllowsTheSameExternalParentInDifferentChildBranches()
    {
        var farmId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var parent = new ExternalGenealogyNode(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            farmId,
            rootId,
            "Pai compartilhado",
            BirdSex.Male);

        var first = CreateLink(Guid.NewGuid());
        var second = CreateLink(Guid.NewGuid());

        Assert.Equal(parent.Id, first.ParentExternalNodeId);
        Assert.Equal(parent.Id, second.ParentExternalNodeId);

        ExternalGenealogyParentLink CreateLink(Guid childId) => new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            farmId,
            rootId,
            childId,
            null,
            ExternalGenealogyParentLink.FatherPosition,
            null,
            parent.Id,
            null,
            null,
            null,
            null,
            null,
            null);
    }
}
