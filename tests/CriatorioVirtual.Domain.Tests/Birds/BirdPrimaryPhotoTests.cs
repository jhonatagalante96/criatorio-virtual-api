using CriatorioVirtual.Domain.Birds;
using Xunit;

namespace CriatorioVirtual.Domain.Tests.Birds;

public sealed class BirdPrimaryPhotoTests
{
    [Fact]
    public void SetPrimaryPhoto_StoresAndClearsTheReference()
    {
        var bird = CreateBird();
        var attachmentId = Guid.NewGuid();
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(1);

        bird.SetPrimaryPhoto(attachmentId, updatedAt);

        Assert.Equal(attachmentId, bird.PrimaryPhotoId);
        Assert.Equal(updatedAt, bird.UpdatedAtUtc);

        bird.SetPrimaryPhoto(null, updatedAt.AddMinutes(1));

        Assert.Null(bird.PrimaryPhotoId);
        Assert.Equal(updatedAt.AddMinutes(1), bird.UpdatedAtUtc);
    }

    [Fact]
    public void SetPrimaryPhoto_RejectsAnEmptyAttachmentIdentifier()
    {
        var bird = CreateBird();

        Assert.Throws<ArgumentException>(() => bird.SetPrimaryPhoto(Guid.Empty, DateTimeOffset.UtcNow));
        Assert.Null(bird.PrimaryPhotoId);
    }

    private static Bird CreateBird() =>
        new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            "Aurora",
            Guid.NewGuid(),
            BirdSex.Female,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateOnly.FromDateTime(DateTime.UtcNow));
}
