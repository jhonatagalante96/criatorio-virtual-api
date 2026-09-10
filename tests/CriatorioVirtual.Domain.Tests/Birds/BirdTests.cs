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
}
