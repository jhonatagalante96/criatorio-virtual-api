using CriatorioVirtual.Domain.Primitives;

namespace CriatorioVirtual.Domain.Birds;

/// <summary>
/// Represents an external ancestor that belongs to one genealogy tree but is not a
/// registered bird in the breeding farm's inventory.
/// </summary>
public sealed class ExternalGenealogyNode : Entity
{
    private ExternalGenealogyNode()
        : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
    {
        Name = null!;
    }

    public ExternalGenealogyNode(
        Guid id,
        DateTimeOffset createdAtUtc,
        Guid breedingFarmId,
        Guid genealogyRootId,
        string name,
        BirdSex sex)
        : this(
            id,
            createdAtUtc,
            breedingFarmId,
            genealogyRootId,
            name,
            sex,
            false,
            null,
            null,
            null,
            null,
            false)
    {
    }

    private ExternalGenealogyNode(
        Guid id,
        DateTimeOffset createdAtUtc,
        Guid breedingFarmId,
        Guid genealogyRootId,
        string name,
        BirdSex sex,
        bool isBirdSnapshot,
        Guid? snapshotSourceBirdId,
        DateOnly? snapshotBirthDate,
        string? snapshotRingNumber,
        BirdStatus? snapshotStatus,
        bool canNavigateToSourceBird)
        : base(id, createdAtUtc)
    {
        if (breedingFarmId == Guid.Empty)
        {
            throw new ArgumentException("The breeding farm identifier cannot be empty.", nameof(breedingFarmId));
        }

        if (genealogyRootId == Guid.Empty)
        {
            throw new ArgumentException("The genealogy root identifier cannot be empty.", nameof(genealogyRootId));
        }

        BreedingFarmId = breedingFarmId;
        GenealogyRootId = genealogyRootId;
        Name = RequireName(name);
        Sex = RequireSex(sex);
        IsBirdSnapshot = isBirdSnapshot;
        SnapshotSourceBirdId = snapshotSourceBirdId;
        SnapshotBirthDate = snapshotBirthDate;
        SnapshotRingNumber = NormalizeSnapshotRingNumber(snapshotRingNumber);
        SnapshotStatus = snapshotStatus is null ? null : RequireSnapshotStatus(snapshotStatus.Value);
        CanNavigateToSourceBird = canNavigateToSourceBird;

        if (isBirdSnapshot)
        {
            if (snapshotSourceBirdId == Guid.Empty || snapshotStatus is null ||
                (canNavigateToSourceBird && snapshotSourceBirdId is null))
            {
                throw new ArgumentException("A bird genealogy snapshot requires valid snapshot data.");
            }
        }
        else if (snapshotSourceBirdId is not null || snapshotBirthDate is not null ||
                 snapshotRingNumber is not null || snapshotStatus is not null || canNavigateToSourceBird)
        {
            throw new ArgumentException("An external ancestor cannot contain bird snapshot data.");
        }
    }

    public static ExternalGenealogyNode CreateBirdSnapshot(
        Guid id,
        DateTimeOffset createdAtUtc,
        Guid breedingFarmId,
        Guid genealogyRootId,
        string name,
        BirdSex sex,
        Guid? sourceBirdId,
        DateOnly? birthDate,
        string? ringNumber,
        BirdStatus status,
        bool canNavigateToSourceBird) =>
        new(
            id,
            createdAtUtc,
            breedingFarmId,
            genealogyRootId,
            name,
            sex,
            true,
            sourceBirdId,
            birthDate,
            ringNumber,
            status,
            canNavigateToSourceBird);

    public const int NameMaxLength = 200;

    public Guid BreedingFarmId { get; private set; }

    public Guid GenealogyRootId { get; private set; }

    public string Name { get; private set; }

    public BirdSex Sex { get; private set; }

    public bool IsBirdSnapshot { get; private set; }

    public Guid? SnapshotSourceBirdId { get; private set; }

    public DateOnly? SnapshotBirthDate { get; private set; }

    public string? SnapshotRingNumber { get; private set; }

    public BirdStatus? SnapshotStatus { get; private set; }

    public bool CanNavigateToSourceBird { get; private set; }

    /// <summary>Moves the tree-scoped node with its genealogy root during an internal transfer.</summary>
    public void MoveToBreedingFarm(Guid breedingFarmId, DateTimeOffset updatedAtUtc)
    {
        if (breedingFarmId == Guid.Empty)
        {
            throw new ArgumentException("The breeding farm identifier cannot be empty.", nameof(breedingFarmId));
        }

        BreedingFarmId = breedingFarmId;
        Touch(updatedAtUtc);
    }

    private static string RequireName(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("An external genealogy node name is required.", nameof(value));
        }

        if (normalized.Length > NameMaxLength)
        {
            throw new ArgumentException(
                $"An external genealogy node name cannot exceed {NameMaxLength} characters.",
                nameof(value));
        }

        return normalized;
    }

    private static BirdSex RequireSex(BirdSex value)
    {
        if (value is not (BirdSex.Male or BirdSex.Female))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "An external genealogy node must have a validated male or female sex.");
        }

        return value;
    }

    private static string? NormalizeSnapshotRingNumber(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is not null &&
            (normalized.Length != 6 || normalized.Any(character => !char.IsAsciiDigit(character))))
        {
            throw new ArgumentException("A bird snapshot ring number must contain exactly six digits.", nameof(value));
        }

        return normalized;
    }

    private static BirdStatus RequireSnapshotStatus(BirdStatus value)
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "A bird snapshot status is invalid.");
        }

        return value;
    }
}
