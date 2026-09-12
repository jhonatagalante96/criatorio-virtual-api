using System.Text;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Storage;

public sealed class PrivateObjectStorageTests
{
    [Fact]
    public async Task StoresObjectsInsideTenantNamespaceAndDoesNotExposeThePhysicalLocation()
    {
        await using var temporary = new TemporaryStorage();
        var storage = CreateStorage(temporary.RootPath);
        var sourceFarmId = Guid.NewGuid();
        var objectKey = "birds/attachment-001";
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("private content"));

        var descriptor = await storage.PutAsync(new PrivateObjectUpload(
            sourceFarmId,
            objectKey,
            "bird.jpg",
            "image/jpeg",
            content));

        Assert.Equal(sourceFarmId, descriptor.BreedingFarmId);
        Assert.Equal(objectKey, descriptor.ObjectKey);
        Assert.Equal("image/jpeg", descriptor.ContentType);
        Assert.Equal("private content".Length, descriptor.Length);
        Assert.DoesNotContain(
            nameof(PrivateStorageOptions.PrivateRootPath),
            typeof(PrivateObjectDescriptor).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain(
            "Url",
            typeof(PrivateObjectDescriptor).GetProperties().Select(property => property.Name));

        var expectedPath = Path.Combine(
            temporary.RootPath,
            sourceFarmId.ToString("N"),
            "birds",
            "attachment-001");
        Assert.True(File.Exists(expectedPath));

        await using var stored = await storage.OpenReadAsync(sourceFarmId, objectKey);
        using var reader = new StreamReader(stored, Encoding.UTF8);
        Assert.Equal("private content", await reader.ReadToEndAsync());

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            storage.OpenReadAsync(Guid.NewGuid(), objectKey));
    }

    [Theory]
    [InlineData("bird.pdf", "image/jpeg")]
    [InlineData("bird.jpg", "application/pdf")]
    [InlineData("bird.png", "image/webp")]
    public async Task RejectsMimeAndExtensionMismatches(string fileName, string contentType)
    {
        await using var temporary = new TemporaryStorage();
        var storage = CreateStorage(temporary.RootPath);
        using var content = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<ArgumentException>(() => storage.PutAsync(new PrivateObjectUpload(
            Guid.NewGuid(),
            "birds/attachment-002",
            fileName,
            contentType,
            content)));
    }

    [Fact]
    public async Task RejectsPathTraversalForObjectKeysAndFileNames()
    {
        await using var temporary = new TemporaryStorage();
        var storage = CreateStorage(temporary.RootPath);
        var farmId = Guid.NewGuid();

        using var keyContent = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<ArgumentException>(() => storage.PutAsync(new PrivateObjectUpload(
            farmId,
            "birds/../outside",
            "bird.jpg",
            "image/jpeg",
            keyContent)));

        using var fileNameContent = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<ArgumentException>(() => storage.PutAsync(new PrivateObjectUpload(
            farmId,
            "birds/attachment-003",
            "../bird.jpg",
            "image/jpeg",
            fileNameContent)));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.OpenReadAsync(farmId, "../../outside"));
    }

    [Fact]
    public async Task DeleteRemovesOnlyTheRequestedTenantObject()
    {
        await using var temporary = new TemporaryStorage();
        var storage = CreateStorage(temporary.RootPath);
        var farmId = Guid.NewGuid();
        var otherFarmId = Guid.NewGuid();
        const string objectKey = "birds/attachment-004";

        using var content = new MemoryStream([4, 5, 6]);
        await storage.PutAsync(new PrivateObjectUpload(
            farmId,
            objectKey,
            "bird.png",
            "image/png",
            content));
        using var otherContent = new MemoryStream([7, 8, 9]);
        await storage.PutAsync(new PrivateObjectUpload(
            otherFarmId,
            objectKey,
            "bird.png",
            "image/png",
            otherContent));

        await storage.DeleteAsync(farmId, objectKey);

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            storage.OpenReadAsync(farmId, objectKey));
        await using var otherStored = await storage.OpenReadAsync(otherFarmId, objectKey);
        Assert.Equal(3, otherStored.Length);
    }

    [Fact]
    public void ProductionRequiresAnAbsolutePrivateStorageRoot()
    {
        var configuration = new ConfigurationBuilder().Build();
        var environment = new TestHostEnvironment("Production");
        using var provider = BuildProvider(configuration, environment);

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<PrivateStorageOptions>>().Value);

        Assert.Contains("Storage:PrivateRootPath", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TestingDefaultsToAPrivateAbsoluteStorageRoot()
    {
        var configuration = new ConfigurationBuilder().Build();
        var environment = new TestHostEnvironment("Testing");
        using var provider = BuildProvider(configuration, environment);

        var options = provider.GetRequiredService<IOptions<PrivateStorageOptions>>().Value;

        Assert.True(Path.IsPathRooted(options.PrivateRootPath));
    }

    private static IPrivateObjectStorage CreateStorage(string rootPath) =>
        new FileSystemPrivateObjectStorage(Options.Create(new PrivateStorageOptions
        {
            PrivateRootPath = rootPath
        }));

    private static ServiceProvider BuildProvider(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var services = new ServiceCollection();
        services.AddSingleton(environment);
        services.AddPrivateStorage(configuration, environment);
        return services.BuildServiceProvider();
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

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "CriatorioVirtual.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
