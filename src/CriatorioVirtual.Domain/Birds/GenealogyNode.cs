using CriatorioVirtual.Domain.Primitives;

namespace CriatorioVirtual.Domain.Birds;

/// <summary>Represents the root node created together with a registered bird.</summary>
public sealed class GenealogyNode : Entity
{
    private GenealogyNode()
        : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
    {
        Position = null!;
    }

    public GenealogyNode(Guid id, DateTimeOffset createdAtUtc, Guid birdId)
        : base(id, createdAtUtc)
    {
        if (birdId == Guid.Empty)
        {
            throw new ArgumentException("The bird identifier cannot be empty.", nameof(birdId));
        }

        BirdId = birdId;
        GenealogyRootId = id;
        LinkedBirdId = birdId;
        Position = RootPosition;
        IsRoot = true;
    }

    public GenealogyNode(
        Guid id,
        DateTimeOffset createdAtUtc,
        Guid breedingFarmId,
        Guid birdId)
        : this(id, createdAtUtc, birdId)
    {
        if (breedingFarmId == Guid.Empty)
        {
            throw new ArgumentException("The breeding farm identifier cannot be empty.", nameof(breedingFarmId));
        }

        BreedingFarmId = breedingFarmId;
    }

    public GenealogyNode(
        Guid id,
        DateTimeOffset createdAtUtc,
        Guid breedingFarmId,
        Guid genealogyRootId,
        string position,
        Guid linkedBirdId,
        string snapshotName,
        BirdSex snapshotSex,
        DateOnly? snapshotBirthDate,
        string? snapshotRingNumber,
        BirdStatus snapshotStatus)
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

        if (linkedBirdId == Guid.Empty)
        {
            throw new ArgumentException("The linked bird identifier cannot be empty.", nameof(linkedBirdId));
        }

        if (!Enum.IsDefined(snapshotSex))
        {
            throw new ArgumentOutOfRangeException(nameof(snapshotSex), "The snapshot bird sex is invalid.");
        }

        if (!Enum.IsDefined(snapshotStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(snapshotStatus), "The snapshot bird status is invalid.");
        }

        BreedingFarmId = breedingFarmId;
        BirdId = linkedBirdId;
        GenealogyRootId = genealogyRootId;
        Position = RequirePosition(position);
        LinkedBirdId = linkedBirdId;
        SnapshotName = RequireSnapshotName(snapshotName);
        SnapshotSex = snapshotSex;
        SnapshotBirthDate = snapshotBirthDate;
        SnapshotRingNumber = NormalizeSnapshotRingNumber(snapshotRingNumber);
        SnapshotStatus = snapshotStatus;
        IsRoot = false;
    }

    public const string RootPosition = "root";

    public Guid? BreedingFarmId { get; private set; }

    public Guid BirdId { get; private set; }

    public Guid GenealogyRootId { get; private set; }

    public string Position { get; private set; }

    public Guid? LinkedBirdId { get; private set; }

    public string? SnapshotName { get; private set; }

    public BirdSex? SnapshotSex { get; private set; }

    public DateOnly? SnapshotBirthDate { get; private set; }

    public string? SnapshotRingNumber { get; private set; }

    public BirdStatus? SnapshotStatus { get; private set; }

    public bool IsRoot { get; private set; }

    private static string RequirePosition(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 100)
        {
            throw new ArgumentException("A genealogy node position is required and cannot exceed 100 characters.", nameof(value));
        }

        return normalized;
    }

    private static string RequireSnapshotName(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("A linked bird snapshot name is required.", nameof(value));
        }

        if (normalized.Length > 100)
        {
            throw new ArgumentException("A linked bird snapshot name cannot exceed 100 characters.", nameof(value));
        }

        return normalized;
    }

    private static string? NormalizeSnapshotRingNumber(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is not null &&
            (normalized.Length != 6 || normalized.Any(character => !char.IsAsciiDigit(character))))
        {
            throw new ArgumentException("A linked bird snapshot ring number must contain exactly six digits.", nameof(value));
        }

        return normalized;
    }
}
