using CriatorioVirtual.Domain.Primitives;
using Xunit;

namespace CriatorioVirtual.Domain.Tests.Primitives;

public sealed class EntityTests
{
    [Fact]
    public void Constructor_RejectsEmptyIdentifier()
    {
        var timestamp = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentException>(() => new TestEntity(Guid.Empty, timestamp));
    }

    [Fact]
    public void Constructor_RejectsNonUtcTimestamp()
    {
        var timestamp = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.FromHours(-3));

        Assert.Throws<ArgumentException>(() => new TestEntity(Guid.NewGuid(), timestamp));
    }

    [Fact]
    public void Touch_UpdatesTimestampInUtc()
    {
        var createdAt = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
        var updatedAt = createdAt.AddMinutes(1);
        var entity = new TestEntity(Guid.NewGuid(), createdAt);

        entity.Update(updatedAt);

        Assert.Equal(updatedAt, entity.UpdatedAtUtc);
        Assert.Equal(TimeSpan.Zero, entity.UpdatedAtUtc.Offset);
    }

    private sealed class TestEntity(Guid id, DateTimeOffset createdAtUtc) : Entity(id, createdAtUtc)
    {
        public void Update(DateTimeOffset updatedAtUtc) => Touch(updatedAtUtc);
    }
}
