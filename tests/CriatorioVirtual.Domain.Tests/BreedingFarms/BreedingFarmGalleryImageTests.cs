using CriatorioVirtual.Domain.BreedingFarms;
using Xunit;

namespace CriatorioVirtual.Domain.Tests.BreedingFarms;

public sealed class BreedingFarmGalleryImageTests
{
    [Fact]
    public void CaptionIsTrimmedAndBlankCaptionsBecomeNull()
    {
        var image = CreateImage();

        image.UpdateCaption("  Ramo do viveiro  ", DateTimeOffset.UtcNow);
        Assert.Equal("Ramo do viveiro", image.Caption);

        image.UpdateCaption("  ", DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Null(image.Caption);
    }

    [Fact]
    public void CaptionCannotExceedContractLimit()
    {
        Assert.Throws<ArgumentException>(() =>
            BreedingFarmGalleryImage.NormalizeCaption(new string('a', BreedingFarmGalleryImage.CaptionMaxLength + 1)));
    }

    [Fact]
    public void DeletionIsIdempotentAndCleanupCanBeRetried()
    {
        var image = CreateImage();
        var deletedAt = DateTimeOffset.UtcNow.AddMinutes(1);

        image.MarkDeleted(deletedAt);
        image.MarkDeleted(deletedAt.AddMinutes(1));

        Assert.True(image.IsDeleted);
        Assert.True(image.StorageCleanupPending);
        Assert.Equal(deletedAt, image.DeletedAtUtc);
        Assert.Throws<InvalidOperationException>(() => image.UpdateCaption("not allowed", deletedAt.AddMinutes(2)));

        image.MarkStorageCleanupCompleted(deletedAt.AddMinutes(3));
        Assert.False(image.StorageCleanupPending);
    }

    private static BreedingFarmGalleryImage CreateImage() => new(
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        Guid.NewGuid(),
        "gallery/image-key",
        "garden.png",
        "image/png",
        128,
        100,
        100,
        null);
}
