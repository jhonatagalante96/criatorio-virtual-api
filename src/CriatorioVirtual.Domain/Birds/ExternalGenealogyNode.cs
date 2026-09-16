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
    }

    public const int NameMaxLength = 200;

    public Guid BreedingFarmId { get; private set; }

    public Guid GenealogyRootId { get; private set; }

    public string Name { get; private set; }

    public BirdSex Sex { get; private set; }

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
}
