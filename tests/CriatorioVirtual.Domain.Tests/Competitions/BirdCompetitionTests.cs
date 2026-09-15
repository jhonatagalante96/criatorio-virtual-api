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

    [Fact]
    public void UpdateDetails_NormalizesMutableFieldsAndPreservesProvenance()
    {
        var breedingFarmId = Guid.NewGuid();
        var birdId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);
        var competition = new BirdCompetition(
            Guid.NewGuid(),
            createdAt,
            breedingFarmId,
            birdId,
            "Campeonato estadual",
            new DateOnly(2026, 9, 6),
            "Livre",
            2,
            "Macaé",
            "Final estadual",
            new DateOnly(2026, 9, 13));
        var updatedAt = new DateTimeOffset(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

        competition.UpdateDetails(
            "  Copa nacional  ",
            new DateOnly(2026, 9, 7),
            "  Azul  ",
            1,
            "  São Paulo  ",
            "  Grande final  ",
            new DateOnly(2026, 9, 13),
            updatedAt);

        Assert.Equal(breedingFarmId, competition.BreedingFarmId);
        Assert.Equal(birdId, competition.BirdId);
        Assert.Equal("Copa nacional", competition.Name);
        Assert.Equal(new DateOnly(2026, 9, 7), competition.CompetitionDate);
        Assert.Equal("Azul", competition.Category);
        Assert.Equal(1, competition.Placement);
        Assert.Equal("São Paulo", competition.Location);
        Assert.Equal("Grande final", competition.Notes);
        Assert.Equal(createdAt, competition.CreatedAtUtc);
        Assert.Equal(updatedAt, competition.UpdatedAtUtc);
    }

    [Fact]
    public void UpdateDetails_RejectsFutureDateAndNonPositivePlacement()
    {
        var competition = new BirdCompetition(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Campeonato estadual",
            null,
            null,
            null,
            null,
            null,
            new DateOnly(2026, 9, 13));

        Assert.Throws<ArgumentException>(() => competition.UpdateDetails(
            "Campeonato estadual",
            new DateOnly(2026, 9, 14),
            null,
            null,
            null,
            null,
            new DateOnly(2026, 9, 13),
            DateTimeOffset.UtcNow));

        Assert.Throws<ArgumentOutOfRangeException>(() => competition.UpdateDetails(
            "Campeonato estadual",
            null,
            null,
            0,
            null,
            null,
            new DateOnly(2026, 9, 13),
            DateTimeOffset.UtcNow));
    }
}
