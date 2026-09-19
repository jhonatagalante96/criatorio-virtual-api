using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.Transfers;
using Xunit;

namespace CriatorioVirtual.Domain.Tests.Transfers;

public sealed class InternalTransferRequestTests
{
    [Fact]
    public void Constructor_StartsPendingAndStoresTransferParticipantsAndSnapshot()
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
            userId,
            "Canário Campeão",
            BirdSex.Male,
            "123456",
            BirdStatus.Active);

        Assert.Equal(sourceFarmId, request.SourceBreedingFarmId);
        Assert.Equal(destinationFarmId, request.DestinationBreedingFarmId);
        Assert.Equal(birdId, request.BirdId);
        Assert.Equal(userId, request.RequestedByUserId);
        Assert.Equal("Canário Campeão", request.BirdSnapshotName);
        Assert.Equal(BirdSex.Male, request.BirdSnapshotSex);
        Assert.Equal("123456", request.BirdSnapshotRingNumber);
        Assert.Equal(BirdStatus.Active, request.BirdSnapshotStatus);
        Assert.Equal(InternalTransferRequestStatus.Pending, request.Status);
        Assert.Equal(createdAt, request.CreatedAtUtc);
    }

    [Fact]
    public void Constructor_AcceptsNullRingNumber()
    {
        var request = CreateValidRequest(ringNumber: null);

        Assert.Null(request.BirdSnapshotRingNumber);
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
            Guid.NewGuid(),
            "Ave Teste",
            BirdSex.Female,
            "654321",
            BirdStatus.Active));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsEmptySnapshotName(string? invalidName)
    {
        Assert.Throws<ArgumentException>(() => CreateValidRequest(name: invalidName!));
    }

    [Fact]
    public void Constructor_RejectsSnapshotNameExceedingMaxLength()
    {
        var longName = new string('A', 101);

        Assert.Throws<ArgumentException>(() => CreateValidRequest(name: longName));
    }

    [Fact]
    public void Constructor_RejectsInvalidSnapshotSex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateValidRequest(sex: (BirdSex)99));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12A456")]
    public void Constructor_RejectsInvalidSnapshotRingNumber(string invalidRing)
    {
        Assert.Throws<ArgumentException>(() => CreateValidRequest(ringNumber: invalidRing));
    }

    [Fact]
    public void Constructor_RejectsInvalidSnapshotStatus()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateValidRequest(status: (BirdStatus)99));
    }

    [Fact]
    public void Accept_ChangesPendingRequestToAccepted()
    {
        var request = CreateValidRequest();
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(1);

        request.Accept(updatedAt);

        Assert.Equal(InternalTransferRequestStatus.Accepted, request.Status);
        Assert.Equal(updatedAt, request.UpdatedAtUtc);
        Assert.Throws<InvalidOperationException>(() => request.Accept(updatedAt.AddMinutes(1)));
    }

    [Fact]
    public void Reject_ChangesPendingRequestToRejectedAndRejectsTerminalReplay()
    {
        var request = CreateValidRequest();
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(1);

        request.Reject(updatedAt);

        Assert.Equal(InternalTransferRequestStatus.Rejected, request.Status);
        Assert.Equal(updatedAt, request.UpdatedAtUtc);
        Assert.Throws<InvalidOperationException>(() => request.Reject(updatedAt.AddMinutes(1)));
    }

    [Fact]
    public void Cancel_ChangesPendingRequestToCancelledAndRejectsTerminalReplay()
    {
        var request = CreateValidRequest();
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(1);

        request.Cancel(updatedAt);

        Assert.Equal(InternalTransferRequestStatus.Cancelled, request.Status);
        Assert.Equal(updatedAt, request.UpdatedAtUtc);
        Assert.Throws<InvalidOperationException>(() => request.Cancel(updatedAt.AddMinutes(1)));
    }

    private static InternalTransferRequest CreateValidRequest(
        string name = "Ave Teste",
        BirdSex sex = BirdSex.Male,
        string? ringNumber = "123456",
        BirdStatus status = BirdStatus.Active) =>
        new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            name,
            sex,
            ringNumber,
            status);
}
