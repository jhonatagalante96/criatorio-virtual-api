using CriatorioVirtual.Domain.Primitives;

namespace CriatorioVirtual.Domain.Reproductions;

public sealed class Reproduction : Entity
{
    private Reproduction()
        : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
    {
        Notes = null;
    }

    public Reproduction(
        Guid id,
        DateTimeOffset createdAtUtc,
        Guid breedingFarmId,
        Guid maleBirdId,
        Guid femaleBirdId,
        DateOnly startDate,
        DateOnly? endDate,
        string? notes,
        DateOnly today)
        : base(id, createdAtUtc)
    {
        if (breedingFarmId == Guid.Empty)
        {
            throw new ArgumentException("The breeding farm identifier cannot be empty.", nameof(breedingFarmId));
        }

        if (maleBirdId == Guid.Empty)
        {
            throw new ArgumentException("The male bird identifier cannot be empty.", nameof(maleBirdId));
        }

        if (femaleBirdId == Guid.Empty)
        {
            throw new ArgumentException("The female bird identifier cannot be empty.", nameof(femaleBirdId));
        }

        if (maleBirdId == femaleBirdId)
        {
            throw new ArgumentException("The same bird cannot be both parents.", nameof(femaleBirdId));
        }

        if (startDate > today)
        {
            throw new ArgumentException("The reproduction start date cannot be in the future.", nameof(startDate));
        }

        if (endDate is not null && endDate < startDate)
        {
            throw new ArgumentException("The reproduction end date cannot be before its start date.", nameof(endDate));
        }

        BreedingFarmId = breedingFarmId;
        MaleBirdId = maleBirdId;
        FemaleBirdId = femaleBirdId;
        StartDate = startDate;
        EndDate = endDate;
        Notes = NormalizeNotes(notes);
        Status = ReproductionStatus.Active;
    }

    public Guid BreedingFarmId { get; private set; }

    public Guid MaleBirdId { get; private set; }

    public Guid FemaleBirdId { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    public string? Notes { get; private set; }

    public ReproductionStatus Status { get; private set; }

    private static string? NormalizeNotes(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is not null && normalized.Length > 2000)
        {
            throw new ArgumentException("Reproduction notes cannot exceed 2000 characters.", nameof(value));
        }

        return normalized;
    }
}
