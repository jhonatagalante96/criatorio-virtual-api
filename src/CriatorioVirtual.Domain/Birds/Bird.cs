using CriatorioVirtual.Domain.Primitives;

namespace CriatorioVirtual.Domain.Birds;

public sealed class Bird : Entity
{
    private Bird()
        : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
    {
        Name = null!;
        Notes = null;
    }

    public Bird(
        Guid id,
        DateTimeOffset createdAtUtc,
        Guid breedingFarmId,
        string name,
        Guid speciesId,
        BirdSex sex,
        DateOnly? birthDate,
        string? ringNumber,
        Guid? fatherBirdId,
        string? externalFatherName,
        Guid? motherBirdId,
        string? externalMotherName,
        string? notes,
        DateOnly today)
        : this(
            id,
            createdAtUtc,
            breedingFarmId,
            name,
            speciesId,
            sex,
            birthDate,
            ringNumber,
            fatherBirdId,
            externalFatherName,
            motherBirdId,
            externalMotherName,
            notes,
            today,
            null)
    {
    }

    public Bird(
        Guid id,
        DateTimeOffset createdAtUtc,
        Guid breedingFarmId,
        string name,
        Guid speciesId,
        BirdSex sex,
        DateOnly? birthDate,
        string? ringNumber,
        Guid? fatherBirdId,
        string? externalFatherName,
        Guid? motherBirdId,
        string? externalMotherName,
        string? notes,
        DateOnly today,
        DateOnly? deathDate)
        : base(id, createdAtUtc)
    {
        if (breedingFarmId == Guid.Empty)
        {
            throw new ArgumentException("The breeding farm identifier cannot be empty.", nameof(breedingFarmId));
        }

        if (speciesId == Guid.Empty)
        {
            throw new ArgumentException("The species identifier cannot be empty.", nameof(speciesId));
        }

        if (!Enum.IsDefined(sex))
        {
            throw new ArgumentOutOfRangeException(nameof(sex), "The bird sex is invalid.");
        }

        BreedingFarmId = breedingFarmId;
        Name = RequireName(name, nameof(name));
        SpeciesId = speciesId;
        Sex = sex;
        BirthDate = ValidateBirthDate(birthDate, today);
        DeathDate = ValidateDeathDate(deathDate, birthDate);
        RingNumber = NormalizeRingNumber(ringNumber, nameof(ringNumber));
        ValidateParentSources(fatherBirdId, externalFatherName, motherBirdId, externalMotherName);
        FatherBirdId = fatherBirdId;
        ExternalFatherName = NormalizeParentName(externalFatherName, nameof(externalFatherName));
        MotherBirdId = motherBirdId;
        ExternalMotherName = NormalizeParentName(externalMotherName, nameof(externalMotherName));
        Notes = NormalizeNotes(notes);
        Status = BirdStatus.Active;
    }

    public Guid BreedingFarmId { get; private set; }

    public string Name { get; private set; } = null!;

    public Guid SpeciesId { get; private set; }

    public BirdSex Sex { get; private set; }

    public DateOnly? BirthDate { get; private set; }

    public DateOnly? DeathDate { get; private set; }

    public string? RingNumber { get; private set; }

    public Guid? FatherBirdId { get; private set; }

    public string? ExternalFatherName { get; private set; }

    public Guid? MotherBirdId { get; private set; }

    public string? ExternalMotherName { get; private set; }

    public string? Notes { get; private set; }

    public BirdStatus Status { get; private set; }

    public bool IdentificationPending => RingNumber is null;

    public void UpdateDetails(
        string name,
        Guid speciesId,
        BirdSex sex,
        DateOnly? birthDate,
        string? ringNumber,
        string? notes,
        DateOnly today,
        DateTimeOffset updatedAtUtc)
    {
        if (speciesId == Guid.Empty)
        {
            throw new ArgumentException("The species identifier cannot be empty.", nameof(speciesId));
        }

        if (!Enum.IsDefined(sex))
        {
            throw new ArgumentOutOfRangeException(nameof(sex), "The bird sex is invalid.");
        }

        var normalizedName = RequireName(name, nameof(name));
        var normalizedBirthDate = ValidateBirthDate(birthDate, today);
        ValidateDeathDate(DeathDate, normalizedBirthDate);
        var normalizedRingNumber = NormalizeRingNumber(ringNumber, nameof(ringNumber));
        var normalizedNotes = NormalizeNotes(notes);

        Name = normalizedName;
        SpeciesId = speciesId;
        Sex = sex;
        BirthDate = normalizedBirthDate;
        RingNumber = normalizedRingNumber;
        Notes = normalizedNotes;
        Touch(updatedAtUtc);
    }

    public int? CalculateAgeInYears(DateOnly today)
    {
        if (BirthDate is null)
        {
            return null;
        }

        if (today < BirthDate.Value)
        {
            throw new ArgumentException("The age cannot be calculated before the birth date.", nameof(today));
        }

        var age = today.Year - BirthDate.Value.Year;
        if (BirthDate.Value.AddYears(age) > today)
        {
            age--;
        }

        return age;
    }

    private static string RequireName(string value, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("A non-empty bird name is required.", parameterName);
        }

        if (normalized.Length > 100)
        {
            throw new ArgumentException("A bird name cannot exceed 100 characters.", parameterName);
        }

        return normalized;
    }

    private static DateOnly? ValidateBirthDate(DateOnly? birthDate, DateOnly today)
    {
        if (birthDate is not null && birthDate > today)
        {
            throw new ArgumentException("The birth date cannot be in the future.", nameof(birthDate));
        }

        return birthDate;
    }

    private static DateOnly? ValidateDeathDate(DateOnly? deathDate, DateOnly? birthDate)
    {
        if (deathDate is not null && birthDate is not null && deathDate < birthDate)
        {
            throw new ArgumentException("The death date cannot be before the birth date.", nameof(deathDate));
        }

        return deathDate;
    }

    private static string? NormalizeRingNumber(string? value, string parameterName)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is null)
        {
            return null;
        }

        if (normalized.Length != 6 || normalized.Any(character => !char.IsAsciiDigit(character)))
        {
            throw new ArgumentException("A ring number must contain exactly six digits.", parameterName);
        }

        return normalized;
    }

    private static string? NormalizeParentName(string? value, string parameterName)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is not null && normalized.Length > 200)
        {
            throw new ArgumentException("An external parent name cannot exceed 200 characters.", parameterName);
        }

        return normalized;
    }

    private static string? NormalizeNotes(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is not null && normalized.Length > 2000)
        {
            throw new ArgumentException("Bird notes cannot exceed 2000 characters.", nameof(value));
        }

        return normalized;
    }

    private static void ValidateParentSources(
        Guid? fatherBirdId,
        string? externalFatherName,
        Guid? motherBirdId,
        string? externalMotherName)
    {
        if (fatherBirdId == Guid.Empty || motherBirdId == Guid.Empty)
        {
            throw new ArgumentException("A parent bird identifier cannot be empty.");
        }

        if (fatherBirdId is not null && externalFatherName is not null && !string.IsNullOrWhiteSpace(externalFatherName) ||
            motherBirdId is not null && externalMotherName is not null && !string.IsNullOrWhiteSpace(externalMotherName))
        {
            throw new ArgumentException("A parent must be linked to a bird or represented by an external name, not both.");
        }

        if (fatherBirdId is not null && fatherBirdId == motherBirdId)
        {
            throw new ArgumentException("The same bird cannot be both parents.");
        }
    }
}
