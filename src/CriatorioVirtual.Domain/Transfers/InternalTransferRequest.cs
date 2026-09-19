using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.Primitives;

namespace CriatorioVirtual.Domain.Transfers;

public sealed class InternalTransferRequest : Entity
{
    private InternalTransferRequest()
        : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
    {
        BirdSnapshotName = null!;
    }

    public InternalTransferRequest(
        Guid id,
        DateTimeOffset createdAtUtc,
        Guid sourceBreedingFarmId,
        Guid destinationBreedingFarmId,
        Guid birdId,
        Guid requestedByUserId,
        string birdSnapshotName,
        BirdSex birdSnapshotSex,
        string? birdSnapshotRingNumber,
        BirdStatus birdSnapshotStatus)
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
        BirdSnapshotName = RequireSnapshotName(birdSnapshotName);
        BirdSnapshotSex = RequireSnapshotSex(birdSnapshotSex);
        BirdSnapshotRingNumber = NormalizeSnapshotRingNumber(birdSnapshotRingNumber);
        BirdSnapshotStatus = RequireSnapshotStatus(birdSnapshotStatus);
        Status = InternalTransferRequestStatus.Pending;
    }

    public Guid SourceBreedingFarmId { get; private set; }

    public Guid DestinationBreedingFarmId { get; private set; }

    public Guid BirdId { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    public string BirdSnapshotName { get; private set; } = null!;

    public BirdSex BirdSnapshotSex { get; private set; }

    public string? BirdSnapshotRingNumber { get; private set; }

    public BirdStatus BirdSnapshotStatus { get; private set; }

    public InternalTransferRequestStatus Status { get; private set; }

    public void Accept(DateTimeOffset updatedAtUtc)
    {
        Complete(InternalTransferRequestStatus.Accepted, updatedAtUtc);
    }

    public void Reject(DateTimeOffset updatedAtUtc)
    {
        Complete(InternalTransferRequestStatus.Rejected, updatedAtUtc);
    }

    public void Cancel(DateTimeOffset updatedAtUtc)
    {
        Complete(InternalTransferRequestStatus.Cancelled, updatedAtUtc);
    }

    private void Complete(InternalTransferRequestStatus terminalStatus, DateTimeOffset updatedAtUtc)
    {
        if (Status != InternalTransferRequestStatus.Pending)
        {
            throw new InvalidOperationException("Only pending internal transfers can change to a terminal state.");
        }

        Status = terminalStatus;
        Touch(updatedAtUtc);
    }

    private static string RequireSnapshotName(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("A bird snapshot name is required.", nameof(value));
        }

        if (normalized.Length > 100)
        {
            throw new ArgumentException("A bird snapshot name cannot exceed 100 characters.", nameof(value));
        }

        return normalized;
    }

    private static BirdSex RequireSnapshotSex(BirdSex sex)
    {
        if (!Enum.IsDefined(sex))
        {
            throw new ArgumentOutOfRangeException(nameof(sex), "The bird snapshot sex is invalid.");
        }

        return sex;
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

    private static BirdStatus RequireSnapshotStatus(BirdStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), "The bird snapshot status is invalid.");
        }

        return status;
    }
}
