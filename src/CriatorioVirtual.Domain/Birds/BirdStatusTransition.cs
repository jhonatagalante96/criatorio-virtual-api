using CriatorioVirtual.Domain.Primitives;

namespace CriatorioVirtual.Domain.Birds;

public sealed class BirdStatusTransition : Entity
{
    private BirdStatusTransition()
        : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
    {
    }

    public BirdStatusTransition(
        Guid id,
        DateTimeOffset createdAtUtc,
        Guid birdId,
        Guid breedingFarmId,
        Guid changedByUserId,
        BirdStatus fromStatus,
        BirdStatus toStatus)
        : base(id, createdAtUtc)
    {
        if (birdId == Guid.Empty)
        {
            throw new ArgumentException("The bird identifier cannot be empty.", nameof(birdId));
        }

        if (breedingFarmId == Guid.Empty)
        {
            throw new ArgumentException("The breeding farm identifier cannot be empty.", nameof(breedingFarmId));
        }

        if (changedByUserId == Guid.Empty)
        {
            throw new ArgumentException("The responsible user identifier cannot be empty.", nameof(changedByUserId));
        }

        if (!Enum.IsDefined(fromStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(fromStatus), "The previous bird status is invalid.");
        }

        if (!Enum.IsDefined(toStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(toStatus), "The new bird status is invalid.");
        }

        if (fromStatus == toStatus)
        {
            throw new ArgumentException("A bird status transition must change the status.", nameof(toStatus));
        }

        BirdId = birdId;
        BreedingFarmId = breedingFarmId;
        ChangedByUserId = changedByUserId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
    }

    public Guid BirdId { get; private set; }

    public Guid BreedingFarmId { get; private set; }

    public Guid ChangedByUserId { get; private set; }

    public BirdStatus FromStatus { get; private set; }

    public BirdStatus ToStatus { get; private set; }
}
