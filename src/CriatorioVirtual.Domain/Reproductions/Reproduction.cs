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

    public void UpdateDetails(
        Guid maleBirdId,
        Guid femaleBirdId,
        DateOnly startDate,
        DateOnly? endDate,
        string? notes,
        DateOnly today,
        DateTimeOffset updatedAtUtc)
    {
        if (Status is not ReproductionStatus.Active)
        {
            if (maleBirdId != MaleBirdId ||
                femaleBirdId != FemaleBirdId ||
                startDate != StartDate ||
                endDate != EndDate)
            {
                throw new InvalidOperationException(
                    "Only notes can be changed after a reproduction reaches a terminal status.");
            }

            Notes = NormalizeNotes(notes);
            Touch(updatedAtUtc);
            return;
        }

        ValidatePair(maleBirdId, femaleBirdId);
        ValidatePeriod(startDate, endDate, today);

        MaleBirdId = maleBirdId;
        FemaleBirdId = femaleBirdId;
        StartDate = startDate;
        EndDate = endDate;
        Notes = NormalizeNotes(notes);
        Touch(updatedAtUtc);
    }

    public void Finish(DateOnly endDate, DateOnly today, DateTimeOffset updatedAtUtc)
    {
        EnsureActive();
        ValidatePeriod(StartDate, endDate, today);

        EndDate = endDate;
        Status = ReproductionStatus.Finished;
        Touch(updatedAtUtc);
    }

    public void Cancel(DateTimeOffset updatedAtUtc)
    {
        EnsureActive();

        Status = ReproductionStatus.Cancelled;
        Touch(updatedAtUtc);
    }

    private void EnsureActive()
    {
        if (Status is not ReproductionStatus.Active)
        {
            throw new InvalidOperationException(
                "A reproduction can only transition from the active status.");
        }
    }

    private static void ValidatePair(Guid maleBirdId, Guid femaleBirdId)
    {
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
    }

    private static void ValidatePeriod(DateOnly startDate, DateOnly? endDate, DateOnly today)
    {
        if (startDate > today)
        {
            throw new ArgumentException("The reproduction start date cannot be in the future.", nameof(startDate));
        }

        if (endDate is not null && endDate < startDate)
        {
            throw new ArgumentException("The reproduction end date cannot be before its start date.", nameof(endDate));
        }

        if (endDate is not null && endDate > today)
        {
            throw new ArgumentException("The reproduction end date cannot be in the future.", nameof(endDate));
        }
    }

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
