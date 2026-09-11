using CriatorioVirtual.Domain.Birds;
using Xunit;

namespace CriatorioVirtual.Domain.Tests.Birds;

public sealed class BirdTests
{
    [Fact]
    public void Constructor_NormalizesOptionalValuesAndStartsActive()
    {
        var bird = new Bird(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            "  Aurora  ",
            Guid.NewGuid(),
            BirdSex.Female,
            new DateOnly(2020, 9, 7),
            " 123456 ",
            null,
            "  Pai externo ",
            null,
            null,
            "  Observação ",
            new DateOnly(2026, 9, 7));

        Assert.Equal("Aurora", bird.Name);
        Assert.Equal("123456", bird.RingNumber);
        Assert.Equal("Pai externo", bird.ExternalFatherName);
        Assert.Equal("Observação", bird.Notes);
        Assert.Equal(BirdStatus.Active, bird.Status);
        Assert.False(bird.IdentificationPending);
        Assert.Equal(6, bird.CalculateAgeInYears(new DateOnly(2026, 9, 7)));
    }

    [Fact]
    public void Constructor_AllowsMissingRingAndMarksIdentificationPending()
    {
        var bird = CreateBird(ringNumber: null);

        Assert.Null(bird.RingNumber);
        Assert.True(bird.IdentificationPending);
    }

    [Fact]
    public void Eligibility_ReportsMissingRingAsPendingAndIneligible()
    {
        var eligibility = BirdEligibility.Evaluate(null, BirdStatus.Active);

        Assert.False(eligibility.IsEligible);
        Assert.Equal(
            [BirdEligibilityIssueCode.MissingRingNumber],
            eligibility.Issues);
    }

    [Fact]
    public void Eligibility_AllowsActiveBirdWithRing()
    {
        var eligibility = BirdEligibility.Evaluate("123456", BirdStatus.Active);

        Assert.True(eligibility.IsEligible);
        Assert.Empty(eligibility.Issues);
    }

    [Fact]
    public void Eligibility_ReportsInactiveStatusWithoutCreatingIdentificationPending()
    {
        var eligibility = BirdEligibility.Evaluate("123456", BirdStatus.Archived);

        Assert.False(eligibility.IsEligible);
        Assert.Equal(
            [BirdEligibilityIssueCode.InactiveStatus],
            eligibility.Issues);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("12345A")]
    public void Constructor_RejectsInvalidRingNumber(string ringNumber)
    {
        Assert.Throws<ArgumentException>(() => CreateBird(ringNumber));
    }

    [Fact]
    public void Constructor_RejectsBirthDateInTheFuture()
    {
        Assert.Throws<ArgumentException>(() => new Bird(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            "Aurora",
            Guid.NewGuid(),
            BirdSex.Unknown,
            new DateOnly(2026, 9, 8),
            null,
            null,
            null,
            null,
            null,
            null,
            new DateOnly(2026, 9, 7)));
    }

    [Fact]
    public void Constructor_RejectsNameLongerThanOneHundredCharacters()
    {
        var longName = new string('A', 101);

        Assert.Throws<ArgumentException>(() => new Bird(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            longName,
            Guid.NewGuid(),
            BirdSex.Unknown,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            new DateOnly(2026, 9, 7)));
    }

    [Fact]
    public void Constructor_RejectsDeathDateBeforeBirthDate()
    {
        Assert.Throws<ArgumentException>(() => new Bird(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            "Aurora",
            Guid.NewGuid(),
            BirdSex.Unknown,
            new DateOnly(2020, 9, 7),
            null,
            null,
            null,
            null,
            null,
            null,
            new DateOnly(2026, 9, 7),
            new DateOnly(2020, 9, 6)));
    }

    [Fact]
    public void Constructor_RejectsBothSourcesForOneParent()
    {
        Assert.Throws<ArgumentException>(() => new Bird(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            "Aurora",
            Guid.NewGuid(),
            BirdSex.Unknown,
            null,
            null,
            Guid.NewGuid(),
            "Pai externo",
            null,
            null,
            null,
            new DateOnly(2026, 9, 7)));
    }

    [Fact]
    public void UpdateDetails_NormalizesValuesAndUpdatesTimestamp()
    {
        var bird = CreateBird("123456");
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(1);

        bird.UpdateDetails(
            "  Beatriz  ",
            Guid.NewGuid(),
            BirdSex.Female,
            new DateOnly(2021, 2, 3),
            " 654321 ",
            "  Atualizada  ",
            new DateOnly(2026, 9, 7),
            updatedAt);

        Assert.Equal("Beatriz", bird.Name);
        Assert.Equal(BirdSex.Female, bird.Sex);
        Assert.Equal(new DateOnly(2021, 2, 3), bird.BirthDate);
        Assert.Equal("654321", bird.RingNumber);
        Assert.Equal("Atualizada", bird.Notes);
        Assert.False(bird.IdentificationPending);
        Assert.Equal(updatedAt, bird.UpdatedAtUtc);
    }

    [Fact]
    public void UpdateDetails_AllowsClearingRingAndMarksIdentificationPending()
    {
        var bird = CreateBird("123456");

        bird.UpdateDetails(
            bird.Name,
            bird.SpeciesId,
            bird.Sex,
            bird.BirthDate,
            null,
            bird.Notes,
            new DateOnly(2026, 9, 7),
            DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Null(bird.RingNumber);
        Assert.True(bird.IdentificationPending);
    }

    [Fact]
    public void UpdateDetails_RejectsBirthDateAfterExistingDeathDate()
    {
        var bird = new Bird(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            "Aurora",
            Guid.NewGuid(),
            BirdSex.Female,
            new DateOnly(2020, 9, 7),
            null,
            null,
            null,
            null,
            null,
            null,
            new DateOnly(2026, 9, 7),
            new DateOnly(2022, 9, 7));

        Assert.Throws<ArgumentException>(() => bird.UpdateDetails(
            bird.Name,
            bird.SpeciesId,
            bird.Sex,
            new DateOnly(2023, 1, 1),
            bird.RingNumber,
            bird.Notes,
            new DateOnly(2026, 9, 7),
            DateTimeOffset.UtcNow.AddMinutes(1)));
    }

    [Fact]
    public void ChangeStatus_ArchivesActiveBirdAndUpdatesTimestamp()
    {
        var bird = CreateBird("123456");
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(1);

        bird.ChangeStatus(
            BirdStatus.Archived,
            null,
            null,
            new DateOnly(2026, 9, 7),
            updatedAt);

        Assert.Equal(BirdStatus.Archived, bird.Status);
        Assert.Null(bird.DeathDate);
        Assert.Equal(updatedAt, bird.UpdatedAtUtc);
    }

    [Fact]
    public void ChangeStatus_RequiresAndStoresValidDeathData()
    {
        var bird = new Bird(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            "Aurora",
            Guid.NewGuid(),
            BirdSex.Female,
            new DateOnly(2020, 9, 7),
            null,
            null,
            null,
            null,
            null,
            "Original",
            new DateOnly(2026, 9, 7));
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(1);

        bird.ChangeStatus(
            BirdStatus.Deceased,
            new DateOnly(2026, 9, 6),
            "  Observada antes do falecimento  ",
            new DateOnly(2026, 9, 7),
            updatedAt);

        Assert.Equal(BirdStatus.Deceased, bird.Status);
        Assert.Equal(new DateOnly(2026, 9, 6), bird.DeathDate);
        Assert.Equal("Observada antes do falecimento", bird.Notes);
        Assert.Equal(updatedAt, bird.UpdatedAtUtc);
    }

    [Fact]
    public void ChangeStatus_RejectsMissingOrFutureDeathDate()
    {
        var bird = CreateBird(null);

        Assert.Throws<ArgumentException>(() => bird.ChangeStatus(
            BirdStatus.Deceased,
            null,
            null,
            new DateOnly(2026, 9, 7),
            DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => bird.ChangeStatus(
            BirdStatus.Deceased,
            new DateOnly(2026, 9, 8),
            null,
            new DateOnly(2026, 9, 7),
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ChangeStatus_RejectsNonActiveBirdsAndManualTransferredStatus()
    {
        var bird = CreateBird(null);
        bird.ChangeStatus(
            BirdStatus.Escaped,
            null,
            null,
            new DateOnly(2026, 9, 7),
            DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => bird.ChangeStatus(
            BirdStatus.Archived,
            null,
            null,
            new DateOnly(2026, 9, 7),
            DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => bird.ChangeStatus(
            BirdStatus.Transferred,
            null,
            null,
            new DateOnly(2026, 9, 7),
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void UpdateParents_ReplacesLinkedAndExternalSources()
    {
        var bird = CreateBird("123456");
        var fatherId = Guid.NewGuid();
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(1);

        bird.UpdateParents(
            fatherId,
            null,
            null,
            "Mãe não cadastrada",
            updatedAt);

        Assert.Equal(fatherId, bird.FatherBirdId);
        Assert.Null(bird.ExternalFatherName);
        Assert.Null(bird.MotherBirdId);
        Assert.Equal("Mãe não cadastrada", bird.ExternalMotherName);
        Assert.Equal(updatedAt, bird.UpdatedAtUtc);
    }

    [Fact]
    public void UpdateParents_RejectsTheSameBirdAsBothParents()
    {
        var bird = CreateBird("123456");
        var parentId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => bird.UpdateParents(
            parentId,
            null,
            parentId,
            null,
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void UpdateParents_NormalizesExternalParentSexes()
    {
        var bird = CreateBird("123456");

        bird.UpdateParents(
            null,
            "  Pai externo  ",
            BirdSex.Male,
            null,
            "  Mãe externa  ",
            BirdSex.Female,
            DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Equal("Pai externo", bird.ExternalFatherName);
        Assert.Equal(BirdSex.Male, bird.ExternalFatherSex);
        Assert.Equal("Mãe externa", bird.ExternalMotherName);
        Assert.Equal(BirdSex.Female, bird.ExternalMotherSex);
    }

    [Theory]
    [InlineData(BirdSex.Female, null)]
    [InlineData(null, BirdSex.Male)]
    public void UpdateParents_RejectsExternalSexForTheWrongPosition(
        BirdSex? externalFatherSex,
        BirdSex? externalMotherSex)
    {
        var bird = CreateBird("123456");

        Assert.Throws<ArgumentException>(() => bird.UpdateParents(
            null,
            "Pai externo",
            externalFatherSex,
            null,
            "Mãe externa",
            externalMotherSex,
            DateTimeOffset.UtcNow.AddMinutes(1)));
    }

    private static Bird CreateBird(string? ringNumber) =>
        new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            "Aurora",
            Guid.NewGuid(),
            BirdSex.Unknown,
            null,
            ringNumber,
            null,
            null,
            null,
            null,
            null,
            new DateOnly(2026, 9, 7));
}

public sealed class GenealogyNodeTests
{
    [Fact]
    public void Constructor_CreatesRootLinkedToBird()
    {
        var birdId = Guid.NewGuid();
        var node = new GenealogyNode(Guid.NewGuid(), DateTimeOffset.UtcNow, birdId);

        Assert.Equal(birdId, node.BirdId);
        Assert.True(node.IsRoot);
    }

    [Fact]
    public void Constructor_RejectsEmptyBirdIdentifier()
    {
        Assert.Throws<ArgumentException>(() => new GenealogyNode(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.Empty));
    }

    [Fact]
    public void LinkedConstructor_CopiesSnapshotWithoutChangingLinkedBirdIdentifier()
    {
        var farmId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var linkedBirdId = Guid.NewGuid();
        var node = new GenealogyNode(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            farmId,
            rootId,
            "father",
            linkedBirdId,
            "Pai Azul",
            BirdSex.Male,
            new DateOnly(2018, 6, 1),
            "930001",
            BirdStatus.Active);

        Assert.Equal(farmId, node.BreedingFarmId);
        Assert.Equal(rootId, node.GenealogyRootId);
        Assert.Equal("father", node.Position);
        Assert.Equal(linkedBirdId, node.LinkedBirdId);
        Assert.Equal("Pai Azul", node.SnapshotName);
        Assert.Equal(BirdSex.Male, node.SnapshotSex);
        Assert.Equal("930001", node.SnapshotRingNumber);
        Assert.False(node.IsRoot);
    }
}
