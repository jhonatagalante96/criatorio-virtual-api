using CriatorioVirtual.Domain.Species;
using Xunit;
using SpeciesEntity = CriatorioVirtual.Domain.Species.Species;

namespace CriatorioVirtual.Domain.Tests.Species;

public sealed class SpeciesTests
{
    [Fact]
    public void NormalizeForSearch_IgnoresCaseAndDiacritics()
    {
        var normalized = SpeciesEntity.NormalizeForSearch(" Sabiá-laranjeira ");

        Assert.Equal("SABIA-LARANJEIRA", normalized);
    }

    [Fact]
    public void Constructor_TrimsDisplayNamesAndPrecomputesSearchNames()
    {
        var species = new SpeciesEntity(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            " Turdus rufiventris ",
            " Sabiá-laranjeira ",
            isActive: true);

        Assert.Equal("Turdus rufiventris", species.ScientificName);
        Assert.Equal("Sabiá-laranjeira", species.PopularName);
        Assert.Equal("TURDUS RUFIVENTRIS", species.NormalizedScientificName);
        Assert.Equal("SABIA-LARANJEIRA", species.NormalizedPopularName);
    }

    [Fact]
    public void Constructor_StoresDefaultImageMetadata()
    {
        var species = new SpeciesEntity(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "Turdus rufiventris",
            "Sabiá-laranjeira",
            isActive: true,
            defaultImageFileName: " 0047.jpg ",
            defaultImageContentType: " IMAGE/JPEG ");

        Assert.Equal("0047.jpg", species.DefaultImageFileName);
        Assert.Equal("image/jpeg", species.DefaultImageContentType);
    }

    [Theory]
    [InlineData("0047.jpg", null)]
    [InlineData(null, "image/jpeg")]
    public void Constructor_RejectsIncompleteDefaultImageMetadata(
        string? fileName,
        string? contentType)
    {
        Assert.Throws<ArgumentException>(() => new SpeciesEntity(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "Turdus rufiventris",
            "Sabiá-laranjeira",
            isActive: true,
            defaultImageFileName: fileName,
            defaultImageContentType: contentType));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Constructor_RejectsBlankNames(string? name)
    {
        Assert.Throws<ArgumentException>(() => new SpeciesEntity(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            name!,
            "Sabiá",
            isActive: true));
    }
}
