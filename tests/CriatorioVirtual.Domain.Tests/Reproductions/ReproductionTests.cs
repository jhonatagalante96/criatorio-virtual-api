using CriatorioVirtual.Domain.Reproductions;
using Xunit;

namespace CriatorioVirtual.Domain.Tests.Reproductions;

public sealed class ReproductionTests
{
    [Fact]
    public void Constructor_NormalizesNotesAndStartsActive()
    {
        var reproduction = CreateReproduction(notes: "  Período de primavera  ");

        Assert.Equal("Período de primavera", reproduction.Notes);
        Assert.Equal(ReproductionStatus.Active, reproduction.Status);
        Assert.Equal(new DateOnly(2026, 9, 1), reproduction.StartDate);
        Assert.Null(reproduction.EndDate);
    }

    [Fact]
    public void Constructor_RejectsFutureStartDate()
    {
        Assert.Throws<ArgumentException>(() => new Reproduction(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 9, 8),
            null,
            null,
            new DateOnly(2026, 9, 7)));
    }

    [Fact]
    public void Constructor_RejectsEndDateBeforeStartDate()
    {
        Assert.Throws<ArgumentException>(() => new Reproduction(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 9, 7),
            new DateOnly(2026, 9, 6),
            null,
            new DateOnly(2026, 9, 7)));
    }

    [Fact]
    public void Constructor_RejectsUsingTheSameBirdTwice()
    {
        var birdId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => new Reproduction(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            birdId,
            birdId,
            new DateOnly(2026, 9, 7),
            null,
            null,
            new DateOnly(2026, 9, 7)));
    }

    private static Reproduction CreateReproduction(string? notes = null) =>
        new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 9, 1),
            null,
            notes,
            new DateOnly(2026, 9, 7));
}
