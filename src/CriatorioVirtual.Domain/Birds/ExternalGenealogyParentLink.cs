using CriatorioVirtual.Domain.Primitives;

namespace CriatorioVirtual.Domain.Birds;

/// <summary>
/// Stores one parent position for either a bird root or an external genealogy node.
/// Bird parents are immutable snapshots; external parents refer to another node in the same tree.
/// </summary>
public sealed class ExternalGenealogyParentLink : Entity
{
    private ExternalGenealogyParentLink()
        : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
    {
        Position = null!;
    }

    public ExternalGenealogyParentLink(
        Guid id,
        DateTimeOffset createdAtUtc,
        Guid breedingFarmId,
        Guid genealogyRootId,
        Guid? childBirdId,
        Guid? childExternalNodeId,
        string position,
        Guid? parentBirdId,
        Guid? parentExternalNodeId,
        Guid? parentSourceBreedingFarmId,
        string? parentSnapshotName,
        BirdSex? parentSnapshotSex,
        DateOnly? parentSnapshotBirthDate,
        string? parentSnapshotRingNumber,
        BirdStatus? parentSnapshotStatus)
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

        if ((childBirdId is null) == (childExternalNodeId is null))
        {
            throw new ArgumentException("A genealogy link must have exactly one child source.");
        }

        if ((parentBirdId is null) == (parentExternalNodeId is null))
        {
            throw new ArgumentException("A genealogy link must have exactly one parent source.");
        }

        if (childExternalNodeId is not null && childExternalNodeId == parentExternalNodeId)
        {
            throw new ArgumentException("An external genealogy node cannot be its own parent.");
        }

        BreedingFarmId = breedingFarmId;
        GenealogyRootId = genealogyRootId;
        ChildBirdId = childBirdId;
        ChildExternalNodeId = childExternalNodeId;
        Position = NormalizePosition(position);
        ParentBirdId = parentBirdId;
        ParentExternalNodeId = parentExternalNodeId;
        ParentSourceBreedingFarmId = parentSourceBreedingFarmId;

        if (parentBirdId is not null)
        {
            ParentSnapshotName = RequireSnapshotName(parentSnapshotName);
            ParentSnapshotSex = RequireSnapshotSex(parentSnapshotSex, Position);
            ParentSnapshotBirthDate = parentSnapshotBirthDate;
            ParentSnapshotRingNumber = NormalizeSnapshotRingNumber(parentSnapshotRingNumber);
            ParentSnapshotStatus = RequireSnapshotStatus(parentSnapshotStatus);
        }
        else if (parentSnapshotName is not null ||
                 parentSnapshotSex is not null ||
                 parentSnapshotBirthDate is not null ||
                 parentSnapshotRingNumber is not null ||
                 parentSnapshotStatus is not null)
        {
            throw new ArgumentException("An external parent link cannot contain a bird snapshot.");
        }
    }

    public const string FatherPosition = "father";
    public const string MotherPosition = "mother";

    public Guid BreedingFarmId { get; private set; }

    public Guid GenealogyRootId { get; private set; }

    public Guid? ChildBirdId { get; private set; }

    public Guid? ChildExternalNodeId { get; private set; }

    public string Position { get; private set; }

    public Guid? ParentBirdId { get; private set; }

    public Guid? ParentExternalNodeId { get; private set; }

    /// <summary>Preserves the source tenant of a bird snapshot without granting access to it.</summary>
    public Guid? ParentSourceBreedingFarmId { get; private set; }

    public string? ParentSnapshotName { get; private set; }

    public BirdSex? ParentSnapshotSex { get; private set; }

    public DateOnly? ParentSnapshotBirthDate { get; private set; }

    public string? ParentSnapshotRingNumber { get; private set; }

    public BirdStatus? ParentSnapshotStatus { get; private set; }

    public void ValidateExternalParent(ExternalGenealogyNode parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        if (ParentExternalNodeId != parent.Id)
        {
            throw new ArgumentException("The supplied external parent does not match this link.", nameof(parent));
        }

        if (parent.GenealogyRootId != GenealogyRootId || parent.BreedingFarmId != BreedingFarmId)
        {
            throw new InvalidOperationException("An external genealogy parent must belong to the same tree.");
        }

        var expectedSex = Position == FatherPosition ? BirdSex.Male : BirdSex.Female;
        if (parent.Sex != expectedSex)
        {
            throw new InvalidOperationException("The external parent sex is incompatible with its position.");
        }
    }

    private static string NormalizePosition(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is not (FatherPosition or MotherPosition))
        {
            throw new ArgumentException("A genealogy parent position must be father or mother.", nameof(value));
        }

        return normalized;
    }

    private static string RequireSnapshotName(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("A bird parent snapshot name is required.", nameof(value));
        }

        if (normalized.Length > 200)
        {
            throw new ArgumentException("A bird parent snapshot name cannot exceed 200 characters.", nameof(value));
        }

        return normalized;
    }

    private static BirdSex RequireSnapshotSex(BirdSex? value, string position)
    {
        var expectedSex = position == FatherPosition ? BirdSex.Male : BirdSex.Female;
        if (value != expectedSex)
        {
            throw new ArgumentException("A bird parent snapshot sex is incompatible with its position.", nameof(value));
        }

        return value.Value;
    }

    private static string? NormalizeSnapshotRingNumber(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is not null &&
            (normalized.Length != 6 || normalized.Any(character => !char.IsAsciiDigit(character))))
        {
            throw new ArgumentException("A bird parent snapshot ring number must contain exactly six digits.", nameof(value));
        }

        return normalized;
    }

    private static BirdStatus RequireSnapshotStatus(BirdStatus? value)
    {
        if (value is null || !Enum.IsDefined(value.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "A bird parent snapshot status is required.");
        }

        return value.Value;
    }
}
