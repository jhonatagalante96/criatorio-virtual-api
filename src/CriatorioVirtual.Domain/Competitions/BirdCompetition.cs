using CriatorioVirtual.Domain.Primitives;

namespace CriatorioVirtual.Domain.Competitions;

public sealed class BirdCompetition : Entity
{
    public const int NameMaxLength = 200;
    public const int CategoryMaxLength = 200;
    public const int LocationMaxLength = 200;
    public const int NotesMaxLength = 2000;

    private BirdCompetition()
        : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
    {
        Name = null!;
    }

    public BirdCompetition(
        Guid id,
        DateTimeOffset createdAtUtc,
        Guid breedingFarmId,
        Guid birdId,
        string name,
        DateOnly? competitionDate,
        string? category,
        int? placement,
        string? location,
        string? notes,
        DateOnly today)
        : base(id, createdAtUtc)
    {
        if (breedingFarmId == Guid.Empty)
        {
            throw new ArgumentException("The breeding farm identifier cannot be empty.", nameof(breedingFarmId));
        }

        if (birdId == Guid.Empty)
        {
            throw new ArgumentException("The bird identifier cannot be empty.", nameof(birdId));
        }

        if (competitionDate is not null && competitionDate > today)
        {
            throw new ArgumentException(
                "The competition date cannot be in the future.",
                nameof(competitionDate));
        }

        if (placement is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(placement),
                "The competition placement must be positive.");
        }

        BreedingFarmId = breedingFarmId;
        BirdId = birdId;
        Name = RequireName(name);
        CompetitionDate = competitionDate;
        Category = Normalize(category, CategoryMaxLength, nameof(category));
        Placement = placement;
        Location = Normalize(location, LocationMaxLength, nameof(location));
        Notes = Normalize(notes, NotesMaxLength, nameof(notes));
    }

    public Guid BreedingFarmId { get; private set; }

    public Guid BirdId { get; private set; }

    public string Name { get; private set; } = null!;

    public DateOnly? CompetitionDate { get; private set; }

    public string? Category { get; private set; }

    public int? Placement { get; private set; }

    public string? Location { get; private set; }

    public string? Notes { get; private set; }

    public void UpdateDetails(
        string name,
        DateOnly? competitionDate,
        string? category,
        int? placement,
        string? location,
        string? notes,
        DateOnly today,
        DateTimeOffset updatedAtUtc)
    {
        if (competitionDate is not null && competitionDate > today)
        {
            throw new ArgumentException(
                "The competition date cannot be in the future.",
                nameof(competitionDate));
        }

        if (placement is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(placement),
                "The competition placement must be positive.");
        }

        Name = RequireName(name);
        CompetitionDate = competitionDate;
        Category = Normalize(category, CategoryMaxLength, nameof(category));
        Placement = placement;
        Location = Normalize(location, LocationMaxLength, nameof(location));
        Notes = Normalize(notes, NotesMaxLength, nameof(notes));
        Touch(updatedAtUtc);
    }

    private static string RequireName(string value)
    {
        var normalized = Normalize(value, NameMaxLength, nameof(value));
        return normalized ?? throw new ArgumentException(
            "A competition name is required.",
            nameof(value));
    }

    private static string? Normalize(string? value, int maxLength, string parameterName)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is not null && normalized.Length > maxLength)
        {
            throw new ArgumentException(
                $"The competition value cannot exceed {maxLength} characters.",
                parameterName);
        }

        return normalized;
    }
}
