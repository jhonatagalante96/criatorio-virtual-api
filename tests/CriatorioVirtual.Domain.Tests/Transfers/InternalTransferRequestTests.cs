using CriatorioVirtual.Domain.Transfers;
using Xunit;

namespace CriatorioVirtual.Domain.Tests.Transfers;

public sealed class InternalTransferRequestTests
{
    [Fact]
    public void Constructor_StartsPendingAndStoresTransferParticipants()
    {
        var sourceFarmId = Guid.NewGuid();
        var destinationFarmId = Guid.NewGuid();
        var birdId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var request = new InternalTransferRequest(
            Guid.NewGuid(),
            createdAt,
            sourceFarmId,
            destinationFarmId,
            birdId,
            userId);

        Assert.Equal(sourceFarmId, request.SourceBreedingFarmId);
        Assert.Equal(destinationFarmId, request.DestinationBreedingFarmId);
        Assert.Equal(birdId, request.BirdId);
        Assert.Equal(userId, request.RequestedByUserId);
        Assert.Equal(InternalTransferRequestStatus.Pending, request.Status);
        Assert.Equal(createdAt, request.CreatedAtUtc);
    }

    [Fact]
    public void Constructor_RejectsTheSameSourceAndDestinationFarm()
    {
        var farmId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => new InternalTransferRequest(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            farmId,
            farmId,
            Guid.NewGuid(),
            Guid.NewGuid()));
    }
}
