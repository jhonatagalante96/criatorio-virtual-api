using CriatorioVirtual.Domain.Competitions;
using Xunit;

namespace CriatorioVirtual.Domain.Tests.Competitions;

public sealed class BirdCompetitionTests
{
    [Fact]
    public void Constructor_NormalizesTextAndPreservesCivilDate()
    {
        var competition = new BirdCompetition(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "  Campeonato estadual  ",
            new DateOnly(2026, 9, 7),
            "  Livre / Azul  ",
            2,
            "  Macaé  ",
            "  Final  ",
            new DateOnly(2026, 9, 13));

        Assert.Equal("Campeonato estadual", competition.Name);
        Assert.Equal(new DateOnly(2026, 9, 7), competition.CompetitionDate);
        Assert.Equal("Livre / Azul", competition.Category);
        Assert.Equal(2, competition.Placement);
        Assert.Equal("Macaé", competition.Location);
        Assert.Equal("Final", competition.Notes);
    }

    [Fact]
    public void Constructor_RejectsBlankName()
    {
        Assert.Throws<ArgumentException>(() => new BirdCompetition(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "  ",
            null,
            null,
            null,
            null,
            null,
            new DateOnly(2026, 9, 13)));
    }

    [Fact]
    public void Constructor_RejectsFutureDate()
    {
        Assert.Throws<ArgumentException>(() => new BirdCompetition(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Campeonato estadual",
            new DateOnly(2026, 9, 14),
            null,
            null,
            null,
            null,
            new DateOnly(2026, 9, 13)));
    }

    [Fact]
    public void Constructor_RejectsNonPositivePlacement()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BirdCompetition(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Campeonato estadual",
            null,
            null,
            0,
            null,
            null,
            new DateOnly(2026, 9, 13)));
    }
}
