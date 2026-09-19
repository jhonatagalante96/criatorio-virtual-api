using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.Primitives;

namespace CriatorioVirtual.Domain.Reproductions;

public sealed class Reproduction : Entity
{
    private Reproduction()
        : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
    {
        MaleBirdName = null!;
        FemaleBirdName = null!;
        Notes = null;
    }

    public Reproduction(
        Guid id,
        DateTimeOffset createdAtUtc,
        Guid breedingFarmId,
        Guid maleBirdId,
        string maleBirdName,
        BirdSex maleBirdSex,
        DateOnly? maleBirdBirthDate,
        string? maleBirdRingNumber,
        BirdStatus maleBirdStatus,
        Guid femaleBirdId,
        string femaleBirdName,
        BirdSex femaleBirdSex,
        DateOnly? femaleBirdBirthDate,
        string? femaleBirdRingNumber,
        BirdStatus femaleBirdStatus,
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

        ValidatePair(maleBirdId, femaleBirdId);
        ValidatePeriod(startDate, endDate, today);

        BreedingFarmId = breedingFarmId;
        MaleBirdId = maleBirdId;
        MaleBirdName = RequireSnapshotName(maleBirdName, nameof(maleBirdName));
        MaleBirdSex = RequireMaleSex(maleBirdSex);
        MaleBirdBirthDate = maleBirdBirthDate;
        MaleBirdRingNumber = NormalizeSnapshotRingNumber(maleBirdRingNumber, nameof(maleBirdRingNumber));
        MaleBirdStatus = RequireSnapshotStatus(maleBirdStatus, nameof(maleBirdStatus));

        FemaleBirdId = femaleBirdId;
        FemaleBirdName = RequireSnapshotName(femaleBirdName, nameof(femaleBirdName));
        FemaleBirdSex = RequireFemaleSex(femaleBirdSex);
        FemaleBirdBirthDate = femaleBirdBirthDate;
        FemaleBirdRingNumber = NormalizeSnapshotRingNumber(femaleBirdRingNumber, nameof(femaleBirdRingNumber));
        FemaleBirdStatus = RequireSnapshotStatus(femaleBirdStatus, nameof(femaleBirdStatus));

        StartDate = startDate;
        EndDate = endDate;
        Notes = NormalizeNotes(notes);
        Status = ReproductionStatus.Active;
    }

    public Reproduction(
        Guid id,
        DateTimeOffset createdAtUtc,
        Guid breedingFarmId,
        ReproductionParticipantSnapshot maleSnapshot,
        ReproductionParticipantSnapshot femaleSnapshot,
        DateOnly startDate,
        DateOnly? endDate,
        string? notes,
        DateOnly today)
        : this(
            id,
            createdAtUtc,
            breedingFarmId,
            (maleSnapshot ?? throw new ArgumentNullException(nameof(maleSnapshot))).BirdId,
            maleSnapshot.Name,
            maleSnapshot.Sex,
            maleSnapshot.BirthDate,
            maleSnapshot.RingNumber,
            maleSnapshot.Status,
            (femaleSnapshot ?? throw new ArgumentNullException(nameof(femaleSnapshot))).BirdId,
            femaleSnapshot.Name,
            femaleSnapshot.Sex,
            femaleSnapshot.BirthDate,
            femaleSnapshot.RingNumber,
            femaleSnapshot.Status,
            startDate,
            endDate,
            notes,
            today)
    {
    }

    public Guid BreedingFarmId { get; private set; }

    public Guid MaleBirdId { get; private set; }

    public string MaleBirdName { get; private set; }

    public BirdSex MaleBirdSex { get; private set; }

    public DateOnly? MaleBirdBirthDate { get; private set; }

    public string? MaleBirdRingNumber { get; private set; }

    public BirdStatus MaleBirdStatus { get; private set; }

    public Guid FemaleBirdId { get; private set; }

    public string FemaleBirdName { get; private set; }

    public BirdSex FemaleBirdSex { get; private set; }

    public DateOnly? FemaleBirdBirthDate { get; private set; }

    public string? FemaleBirdRingNumber { get; private set; }

    public BirdStatus FemaleBirdStatus { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    public string? Notes { get; private set; }

    public ReproductionStatus Status { get; private set; }

    public ReproductionParticipantSnapshot MaleSnapshot =>
        new(MaleBirdId, MaleBirdName, MaleBirdSex, MaleBirdBirthDate, MaleBirdRingNumber, MaleBirdStatus);

    public ReproductionParticipantSnapshot FemaleSnapshot =>
        new(FemaleBirdId, FemaleBirdName, FemaleBirdSex, FemaleBirdBirthDate, FemaleBirdRingNumber, FemaleBirdStatus);

    public void UpdateDetails(
        Guid maleBirdId,
        Guid femaleBirdId,
        DateOnly startDate,
        DateOnly? endDate,
        string? notes,
        DateOnly today,
        DateTimeOffset updatedAtUtc,
        ReproductionParticipantSnapshot? newMaleSnapshot = null,
        ReproductionParticipantSnapshot? newFemaleSnapshot = null)
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

        if (maleBirdId != MaleBirdId)
        {
            if (newMaleSnapshot is null || newMaleSnapshot.BirdId != maleBirdId)
            {
                throw new ArgumentException("A snapshot matching the new male bird is required.", nameof(newMaleSnapshot));
            }

            SetMaleSnapshot(newMaleSnapshot);
        }

        if (femaleBirdId != FemaleBirdId)
        {
            if (newFemaleSnapshot is null || newFemaleSnapshot.BirdId != femaleBirdId)
            {
                throw new ArgumentException("A snapshot matching the new female bird is required.", nameof(newFemaleSnapshot));
            }

            SetFemaleSnapshot(newFemaleSnapshot);
        }

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

    private void SetMaleSnapshot(ReproductionParticipantSnapshot snapshot)
    {
        MaleBirdId = snapshot.BirdId;
        MaleBirdName = RequireSnapshotName(snapshot.Name, nameof(snapshot.Name));
        MaleBirdSex = RequireMaleSex(snapshot.Sex);
        MaleBirdBirthDate = snapshot.BirthDate;
        MaleBirdRingNumber = NormalizeSnapshotRingNumber(snapshot.RingNumber, nameof(snapshot.RingNumber));
        MaleBirdStatus = RequireSnapshotStatus(snapshot.Status, nameof(snapshot.Status));
    }

    private void SetFemaleSnapshot(ReproductionParticipantSnapshot snapshot)
    {
        FemaleBirdId = snapshot.BirdId;
        FemaleBirdName = RequireSnapshotName(snapshot.Name, nameof(snapshot.Name));
        FemaleBirdSex = RequireFemaleSex(snapshot.Sex);
        FemaleBirdBirthDate = snapshot.BirthDate;
        FemaleBirdRingNumber = NormalizeSnapshotRingNumber(snapshot.RingNumber, nameof(snapshot.RingNumber));
        FemaleBirdStatus = RequireSnapshotStatus(snapshot.Status, nameof(snapshot.Status));
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

    private static string RequireSnapshotName(string value, string paramName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("A participant snapshot name is required.", paramName);
        }

        if (normalized.Length > 100)
        {
            throw new ArgumentException("A participant snapshot name cannot exceed 100 characters.", paramName);
        }

        return normalized;
    }

    private static BirdSex RequireMaleSex(BirdSex sex)
    {
        if (sex != BirdSex.Male)
        {
            throw new ArgumentException("The male participant snapshot must have Male sex.", nameof(sex));
        }

        return sex;
    }

    private static BirdSex RequireFemaleSex(BirdSex sex)
    {
        if (sex != BirdSex.Female)
        {
            throw new ArgumentException("The female participant snapshot must have Female sex.", nameof(sex));
        }

        return sex;
    }

    private static string? NormalizeSnapshotRingNumber(string? value, string paramName)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is not null &&
            (normalized.Length != 6 || normalized.Any(character => !char.IsAsciiDigit(character))))
        {
            throw new ArgumentException("A participant snapshot ring number must contain exactly six digits.", paramName);
        }

        return normalized;
    }

    private static BirdStatus RequireSnapshotStatus(BirdStatus status, string paramName)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(paramName, "The participant snapshot status is invalid.");
        }

        return status;
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
