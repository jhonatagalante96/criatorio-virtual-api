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

    [Fact]
    public void UpdateDetails_ChangesActiveReproductionAndNormalizesNotes()
    {
        var reproduction = CreateReproduction(notes: "Original");
        var maleBirdId = Guid.NewGuid();
        var femaleBirdId = Guid.NewGuid();

        reproduction.UpdateDetails(
            maleBirdId,
            femaleBirdId,
            new DateOnly(2026, 9, 2),
            new DateOnly(2026, 9, 6),
            "  Atualizada  ",
            new DateOnly(2026, 9, 7),
            DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Equal(maleBirdId, reproduction.MaleBirdId);
        Assert.Equal(femaleBirdId, reproduction.FemaleBirdId);
        Assert.Equal(new DateOnly(2026, 9, 2), reproduction.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 6), reproduction.EndDate);
        Assert.Equal("Atualizada", reproduction.Notes);
        Assert.Equal(ReproductionStatus.Active, reproduction.Status);
    }

    [Fact]
    public void Finish_StoresEndDateAndRejectsASecondTerminalTransition()
    {
        var reproduction = CreateReproduction();

        reproduction.Finish(
            new DateOnly(2026, 9, 6),
            new DateOnly(2026, 9, 7),
            DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Equal(ReproductionStatus.Finished, reproduction.Status);
        Assert.Equal(new DateOnly(2026, 9, 6), reproduction.EndDate);
        Assert.Throws<InvalidOperationException>(() => reproduction.Cancel(DateTimeOffset.UtcNow.AddMinutes(2)));
    }

    [Fact]
    public void Cancel_PreservesPeriodAndRejectsReopening()
    {
        var reproduction = CreateReproduction();

        reproduction.Cancel(DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Equal(ReproductionStatus.Cancelled, reproduction.Status);
        Assert.Null(reproduction.EndDate);
        Assert.Throws<InvalidOperationException>(() => reproduction.Finish(
            new DateOnly(2026, 9, 6),
            new DateOnly(2026, 9, 7),
            DateTimeOffset.UtcNow.AddMinutes(2)));
    }

    [Fact]
    public void UpdateDetails_AllowsOnlyNotesAfterTerminalTransition()
    {
        var reproduction = CreateReproduction(notes: "Original");
        reproduction.Finish(
            new DateOnly(2026, 9, 6),
            new DateOnly(2026, 9, 7),
            DateTimeOffset.UtcNow.AddMinutes(1));

        reproduction.UpdateDetails(
            reproduction.MaleBirdId,
            reproduction.FemaleBirdId,
            reproduction.StartDate,
            reproduction.EndDate,
            "  Correção  ",
            new DateOnly(2026, 9, 7),
            DateTimeOffset.UtcNow.AddMinutes(2));

        Assert.Equal("Correção", reproduction.Notes);
        Assert.Throws<InvalidOperationException>(() => reproduction.UpdateDetails(
            Guid.NewGuid(),
            reproduction.FemaleBirdId,
            reproduction.StartDate,
            reproduction.EndDate,
            "Outra alteração",
            new DateOnly(2026, 9, 7),
            DateTimeOffset.UtcNow.AddMinutes(3)));
    }

    [Fact]
    public void Finish_RejectsFutureAndBeforeStartDates()
    {
        var reproduction = CreateReproduction();

        Assert.Throws<ArgumentException>(() => reproduction.Finish(
            new DateOnly(2026, 8, 31),
            new DateOnly(2026, 9, 7),
            DateTimeOffset.UtcNow.AddMinutes(1)));
        Assert.Throws<ArgumentException>(() => reproduction.Finish(
            new DateOnly(2026, 9, 8),
            new DateOnly(2026, 9, 7),
            DateTimeOffset.UtcNow.AddMinutes(1)));
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
