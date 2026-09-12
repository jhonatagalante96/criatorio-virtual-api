using CriatorioVirtual.Domain.Primitives;

namespace CriatorioVirtual.Domain.Transfers;

public sealed class InternalTransferRequest : Entity
{
    private InternalTransferRequest()
        : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
    {
    }

    public InternalTransferRequest(
        Guid id,
        DateTimeOffset createdAtUtc,
        Guid sourceBreedingFarmId,
        Guid destinationBreedingFarmId,
        Guid birdId,
        Guid requestedByUserId)
        : base(id, createdAtUtc)
    {
        if (sourceBreedingFarmId == Guid.Empty)
        {
            throw new ArgumentException("The source breeding farm identifier cannot be empty.", nameof(sourceBreedingFarmId));
        }

        if (destinationBreedingFarmId == Guid.Empty)
        {
            throw new ArgumentException("The destination breeding farm identifier cannot be empty.", nameof(destinationBreedingFarmId));
        }

        if (sourceBreedingFarmId == destinationBreedingFarmId)
        {
            throw new ArgumentException("The source and destination breeding farms must be different.", nameof(destinationBreedingFarmId));
        }

        if (birdId == Guid.Empty)
        {
            throw new ArgumentException("The bird identifier cannot be empty.", nameof(birdId));
        }

        if (requestedByUserId == Guid.Empty)
        {
            throw new ArgumentException("The requesting user identifier cannot be empty.", nameof(requestedByUserId));
        }

        SourceBreedingFarmId = sourceBreedingFarmId;
        DestinationBreedingFarmId = destinationBreedingFarmId;
        BirdId = birdId;
        RequestedByUserId = requestedByUserId;
        Status = InternalTransferRequestStatus.Pending;
    }

    public Guid SourceBreedingFarmId { get; private set; }

    public Guid DestinationBreedingFarmId { get; private set; }

    public Guid BirdId { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    public InternalTransferRequestStatus Status { get; private set; }
}
