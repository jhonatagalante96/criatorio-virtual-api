using CriatorioVirtual.Domain.Birds;
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
        Assert.Equal("Macho", reproduction.MaleBirdName);
        Assert.Equal(BirdSex.Male, reproduction.MaleBirdSex);
        Assert.Equal("123456", reproduction.MaleBirdRingNumber);
        Assert.Equal("Fêmea", reproduction.FemaleBirdName);
        Assert.Equal(BirdSex.Female, reproduction.FemaleBirdSex);
        Assert.Equal("654321", reproduction.FemaleBirdRingNumber);
    }

    [Fact]
    public void Constructor_RejectsFutureStartDate()
    {
        Assert.Throws<ArgumentException>(() => new Reproduction(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Macho",
            BirdSex.Male,
            null,
            "123456",
            BirdStatus.Active,
            Guid.NewGuid(),
            "Fêmea",
            BirdSex.Female,
            null,
            "654321",
            BirdStatus.Active,
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
            "Macho",
            BirdSex.Male,
            null,
            "123456",
            BirdStatus.Active,
            Guid.NewGuid(),
            "Fêmea",
            BirdSex.Female,
            null,
            "654321",
            BirdStatus.Active,
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
            "Macho",
            BirdSex.Male,
            null,
            "123456",
            BirdStatus.Active,
            birdId,
            "Fêmea",
            BirdSex.Female,
            null,
            "654321",
            BirdStatus.Active,
            new DateOnly(2026, 9, 7),
            null,
            null,
            new DateOnly(2026, 9, 7)));
    }

    [Fact]
    public void Constructor_ValidatesSnapshotProperties()
    {
        Assert.Throws<ArgumentException>(() => new Reproduction(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "   ",
            BirdSex.Male,
            null,
            "123456",
            BirdStatus.Active,
            Guid.NewGuid(),
            "Fêmea",
            BirdSex.Female,
            null,
            "654321",
            BirdStatus.Active,
            new DateOnly(2026, 9, 1),
            null,
            null,
            new DateOnly(2026, 9, 7)));

        Assert.Throws<ArgumentException>(() => new Reproduction(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Macho",
            BirdSex.Female,
            null,
            "123456",
            BirdStatus.Active,
            Guid.NewGuid(),
            "Fêmea",
            BirdSex.Female,
            null,
            "654321",
            BirdStatus.Active,
            new DateOnly(2026, 9, 1),
            null,
            null,
            new DateOnly(2026, 9, 7)));

        Assert.Throws<ArgumentException>(() => new Reproduction(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Macho",
            BirdSex.Male,
            null,
            "12345",
            BirdStatus.Active,
            Guid.NewGuid(),
            "Fêmea",
            BirdSex.Female,
            null,
            "654321",
            BirdStatus.Active,
            new DateOnly(2026, 9, 1),
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
        var newMaleSnapshot = new ReproductionParticipantSnapshot(
            maleBirdId,
            "Novo Macho",
            BirdSex.Male,
            null,
            "999001",
            BirdStatus.Active);
        var newFemaleSnapshot = new ReproductionParticipantSnapshot(
            femaleBirdId,
            "Nova Fêmea",
            BirdSex.Female,
            null,
            "999002",
            BirdStatus.Active);

        reproduction.UpdateDetails(
            maleBirdId,
            femaleBirdId,
            new DateOnly(2026, 9, 2),
            new DateOnly(2026, 9, 6),
            "  Atualizada  ",
            new DateOnly(2026, 9, 7),
            DateTimeOffset.UtcNow.AddMinutes(1),
            newMaleSnapshot,
            newFemaleSnapshot);

        Assert.Equal(maleBirdId, reproduction.MaleBirdId);
        Assert.Equal("Novo Macho", reproduction.MaleBirdName);
        Assert.Equal("999001", reproduction.MaleBirdRingNumber);
        Assert.Equal(femaleBirdId, reproduction.FemaleBirdId);
        Assert.Equal("Nova Fêmea", reproduction.FemaleBirdName);
        Assert.Equal("999002", reproduction.FemaleBirdRingNumber);
        Assert.Equal(new DateOnly(2026, 9, 2), reproduction.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 6), reproduction.EndDate);
        Assert.Equal("Atualizada", reproduction.Notes);
        Assert.Equal(ReproductionStatus.Active, reproduction.Status);
    }

    [Fact]
    public void UpdateDetails_PreservesHistoricalSnapshotWhenBirdIdsAreNotChanged()
    {
        var reproduction = CreateReproduction(notes: "Original");
        var originalMaleName = reproduction.MaleBirdName;
        var originalFemaleName = reproduction.FemaleBirdName;

        reproduction.UpdateDetails(
            reproduction.MaleBirdId,
            reproduction.FemaleBirdId,
            new DateOnly(2026, 9, 2),
            null,
            "Nova anotação apenas",
            new DateOnly(2026, 9, 7),
            DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Equal(originalMaleName, reproduction.MaleBirdName);
        Assert.Equal(originalFemaleName, reproduction.FemaleBirdName);
        Assert.Equal("Nova anotação apenas", reproduction.Notes);
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
            "Macho",
            BirdSex.Male,
            new DateOnly(2025, 1, 1),
            "123456",
            BirdStatus.Active,
            Guid.NewGuid(),
            "Fêmea",
            BirdSex.Female,
            new DateOnly(2025, 2, 1),
            "654321",
            BirdStatus.Active,
            new DateOnly(2026, 9, 1),
            null,
            notes,
            new DateOnly(2026, 9, 7));
}
