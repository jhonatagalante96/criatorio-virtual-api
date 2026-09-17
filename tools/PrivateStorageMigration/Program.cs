using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) ||
        args.Contains("-h", StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine("Migrate private objects from Storage:LegacyPrivateRootPath to the configured Storage:S3 bucket.");
        Console.WriteLine("Configure Storage:Provider=S3, the Storage:S3 credentials, Storage:LegacyPrivateRootPath, and ConnectionStrings:CriatorioVirtual.");
        return 0;
    }

    try
    {
        var builder = Host.CreateApplicationBuilder(args);
        var sourceRoot = builder.Configuration[$"{PrivateStorageOptions.SectionName}:LegacyPrivateRootPath"];
        if (string.IsNullOrWhiteSpace(sourceRoot) || !Path.IsPathRooted(sourceRoot))
        {
            throw new InvalidOperationException("Storage:LegacyPrivateRootPath must point to the mounted legacy Volume.");
        }

        var connectionString = builder.Configuration.GetConnectionString("CriatorioVirtual");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:CriatorioVirtual is required to preserve stored MIME metadata.");
        }

        builder.Services.AddPrivateStorage(builder.Configuration, builder.Environment);
        using var host = builder.Build();
        await host.StartAsync();

        var metadata = await LoadContentTypesAsync(connectionString, CancellationToken.None);
        var source = new FileSystemPrivateStorageMigrationSource(sourceRoot, metadata);
        var target = host.Services.GetRequiredService<IPrivateStorageMigrationTarget>();
        var summary = await new PrivateObjectStorageMigrator(source, target).MigrateAsync();
        Console.WriteLine($"Migration verified: {summary.Copied} copied, {summary.AlreadyPresent} already matched, {summary.Discovered} discovered.");
        await host.StopAsync();
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Migration stopped: {exception.GetType().Name}: {exception.Message}");
        return 1;
    }
}

static async Task<IReadOnlyDictionary<(Guid BreedingFarmId, string ObjectKey), string>> LoadContentTypesAsync(
    string connectionString,
    CancellationToken cancellationToken)
{
    var options = new DbContextOptionsBuilder<CriatorioVirtualDbContext>()
        .UseNpgsql(connectionString)
        .Options;
    await using var database = new CriatorioVirtualDbContext(options);
    var result = new Dictionary<(Guid BreedingFarmId, string ObjectKey), string>();

    var attachments = await database.BirdAttachments
        .AsNoTracking()
        .Select(attachment => new { attachment.BreedingFarmId, attachment.ObjectKey, attachment.ContentType })
        .ToListAsync(cancellationToken);
    foreach (var item in attachments)
    {
        AddMetadata(result, item.BreedingFarmId, item.ObjectKey, item.ContentType);
    }

    var documents = await database.BirdDocuments
        .AsNoTracking()
        .Select(document => new { document.CreatedByBreedingFarmId, document.ObjectKey, document.ContentType })
        .ToListAsync(cancellationToken);
    foreach (var item in documents)
    {
        AddMetadata(result, item.CreatedByBreedingFarmId, item.ObjectKey, item.ContentType);
    }

    var identities = await database.BreedingFarms
        .AsNoTracking()
        .Where(farm => farm.VisualIdentityReference != null && farm.VisualIdentityContentType != null)
        .Select(farm => new { farm.Id, ObjectKey = farm.VisualIdentityReference!, ContentType = farm.VisualIdentityContentType! })
        .ToListAsync(cancellationToken);
    foreach (var item in identities)
    {
        AddMetadata(result, item.Id, item.ObjectKey, item.ContentType);
    }

    return result;
}

static void AddMetadata(
    IDictionary<(Guid BreedingFarmId, string ObjectKey), string> metadata,
    Guid breedingFarmId,
    string objectKey,
    string contentType)
{
    var key = (breedingFarmId, objectKey);
    if (metadata.TryGetValue(key, out var previous) &&
        !string.Equals(previous, contentType, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException($"Conflicting MIME metadata exists for private object '{objectKey}'.");
    }

    metadata[key] = contentType;
}
