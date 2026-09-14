using CriatorioVirtual.Infrastructure.Species;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CriatorioVirtual.Infrastructure.Storage;

public sealed class SpeciesDefaultImageStorageOptions
{
    public const string SectionName = PrivateStorageOptions.SectionName;

    public string SpeciesDefaultImagesRootPath { get; set; } = string.Empty;
}

public sealed class SpeciesDefaultImageStorageOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<SpeciesDefaultImageStorageOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        SpeciesDefaultImageStorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SpeciesDefaultImagesRootPath))
        {
            return IsLocalEnvironment(environment)
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(
                    "Storage:SpeciesDefaultImagesRootPath is required outside Development and Testing environments.");
        }

        try
        {
            if (!Path.IsPathRooted(options.SpeciesDefaultImagesRootPath) ||
                string.IsNullOrWhiteSpace(Path.GetFullPath(options.SpeciesDefaultImagesRootPath)))
            {
                return ValidateOptionsResult.Fail(
                    "Storage:SpeciesDefaultImagesRootPath must be an absolute path.");
            }
        }
        catch (ArgumentException)
        {
            return ValidateOptionsResult.Fail(
                "Storage:SpeciesDefaultImagesRootPath must be a valid absolute path.");
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsLocalEnvironment(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing");
}

public static class SpeciesDefaultImageCatalog
{
    private static readonly IReadOnlyDictionary<string, string> ContentTypes =
        SpeciesCatalogSeed.All
            .Where(species => species.DefaultImageFileName is not null && species.DefaultImageContentType is not null)
            .ToDictionary(
                species => species.DefaultImageFileName!,
                species => species.DefaultImageContentType!,
                StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<string> FileNames => ContentTypes.Keys.ToArray();

    public static bool TryGetContentType(string fileName, out string contentType) =>
        ContentTypes.TryGetValue(fileName, out contentType!);
}

public sealed class SpeciesDefaultImageProvisioner(
    IOptions<SpeciesDefaultImageStorageOptions> options) : IHostedService
{
    private const string ResourcePrefix = "CriatorioVirtual.Infrastructure.Species.Assets.";
    private static readonly SemaphoreSlim ProvisioningGate = new(1, 1);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await ProvisioningGate.WaitAsync(cancellationToken);
        try
        {
            var rootPath = Path.GetFullPath(options.Value.SpeciesDefaultImagesRootPath);
            Directory.CreateDirectory(rootPath);

            foreach (var fileName in SpeciesDefaultImageCatalog.FileNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destinationPath = Path.Combine(rootPath, fileName);
                if (File.Exists(destinationPath))
                {
                    continue;
                }

                await CopyEmbeddedImageAsync(destinationPath, fileName, cancellationToken);
            }
        }
        finally
        {
            ProvisioningGate.Release();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task CopyEmbeddedImageAsync(
        string destinationPath,
        string fileName,
        CancellationToken cancellationToken)
    {
        await using var resource = typeof(SpeciesDefaultImageProvisioner).Assembly
            .GetManifestResourceStream(ResourcePrefix + fileName)
            ?? throw new InvalidOperationException(
                $"The embedded species default image '{fileName}' was not found.");
        var destinationWasCreated = false;
        try
        {
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            destinationWasCreated = true;
            await resource.CopyToAsync(destination, 64 * 1024, cancellationToken);
            await destination.FlushAsync(cancellationToken);
        }
        catch (IOException) when (!destinationWasCreated && File.Exists(destinationPath))
        {
            // Another application instance provisioned this immutable file first.
        }
        catch
        {
            if (destinationWasCreated)
            {
                TryDelete(destinationPath);
            }

            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
