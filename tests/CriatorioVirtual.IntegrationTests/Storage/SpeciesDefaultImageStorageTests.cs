using CriatorioVirtual.Infrastructure.Storage;
using Microsoft.Extensions.Options;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Storage;

public sealed class SpeciesDefaultImageStorageTests
{
    [Fact]
    public async Task ProvisionerCopiesAllCatalogImagesAndPreservesExistingFiles()
    {
        await using var temporary = new TemporaryStorage();
        var provisioner = new SpeciesDefaultImageProvisioner(
            Options.Create(new SpeciesDefaultImageStorageOptions
            {
                SpeciesDefaultImagesRootPath = temporary.RootPath
            }));

        await provisioner.StartAsync(CancellationToken.None);

        var files = Directory.GetFiles(temporary.RootPath, "*.jpg");
        Assert.Equal(60, files.Length);
        Assert.All(files, file => Assert.True(new FileInfo(file).Length > 0));

        var firstImagePath = Path.Combine(temporary.RootPath, "0001.jpg");
        var firstImage = await File.ReadAllBytesAsync(firstImagePath);
        await File.WriteAllBytesAsync(firstImagePath, [1, 2, 3]);

        await provisioner.StartAsync(CancellationToken.None);

        var preservedImage = await File.ReadAllBytesAsync(firstImagePath);
        Assert.Equal([1, 2, 3], preservedImage);
        Assert.False(firstImage.SequenceEqual(preservedImage));
    }

    private sealed class TemporaryStorage : IAsyncDisposable
    {
        public TemporaryStorage() =>
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "CriatorioVirtualTests",
                Guid.NewGuid().ToString("N"));

        public string RootPath { get; }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
