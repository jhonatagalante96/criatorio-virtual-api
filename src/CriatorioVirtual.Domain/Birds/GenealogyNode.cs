using CriatorioVirtual.Domain.Primitives;

namespace CriatorioVirtual.Domain.Birds;

/// <summary>Represents the root node created together with a registered bird.</summary>
public sealed class GenealogyNode : Entity
{
    private GenealogyNode()
        : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
    {
    }

    public GenealogyNode(Guid id, DateTimeOffset createdAtUtc, Guid birdId)
        : base(id, createdAtUtc)
    {
        if (birdId == Guid.Empty)
        {
            throw new ArgumentException("The bird identifier cannot be empty.", nameof(birdId));
        }

        BirdId = birdId;
        IsRoot = true;
    }

    public Guid BirdId { get; private set; }

    public bool IsRoot { get; private set; }
}
