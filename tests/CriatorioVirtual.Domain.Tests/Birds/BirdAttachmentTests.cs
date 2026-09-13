using CriatorioVirtual.Domain.Birds;
using Xunit;

namespace CriatorioVirtual.Domain.Tests.Birds;

public sealed class BirdAttachmentTests
{
    [Fact]
    public void MarkDeleted_HidesAttachmentAndCreatesCleanupPendingState()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var attachment = CreateAttachment(createdAt);
        var deletedAt = createdAt.AddMinutes(1);

        attachment.MarkDeleted(deletedAt);

        Assert.True(attachment.IsDeleted);
        Assert.Equal(deletedAt, attachment.DeletedAtUtc);
        Assert.True(attachment.StorageCleanupPending);
        Assert.Equal(deletedAt, attachment.UpdatedAtUtc);
    }

    [Fact]
    public void MarkDeleted_IsIdempotentAndDoesNotMoveTheDeletionTimestamp()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var attachment = CreateAttachment(createdAt);
        var deletedAt = createdAt.AddMinutes(1);

        attachment.MarkDeleted(deletedAt);
        attachment.MarkDeleted(deletedAt.AddMinutes(1));

        Assert.Equal(deletedAt, attachment.DeletedAtUtc);
        Assert.True(attachment.StorageCleanupPending);
    }

    [Fact]
    public void MarkStorageCleanupCompleted_RequiresDeletionAndClearsPendingState()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var attachment = CreateAttachment(createdAt);
        var cleanedAt = createdAt.AddMinutes(2);

        Assert.Throws<InvalidOperationException>(
            () => attachment.MarkStorageCleanupCompleted(cleanedAt));

        attachment.MarkDeleted(createdAt.AddMinutes(1));
        attachment.MarkStorageCleanupCompleted(cleanedAt);

        Assert.True(attachment.IsDeleted);
        Assert.False(attachment.StorageCleanupPending);
        Assert.Equal(cleanedAt, attachment.UpdatedAtUtc);
    }

    private static BirdAttachment CreateAttachment(DateTimeOffset createdAt) =>
        new(
            Guid.NewGuid(),
            createdAt,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "birds/bird/attachments/object",
            "bird.jpg",
            "image/jpeg",
            12);
}
