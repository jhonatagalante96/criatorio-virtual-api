using System.IO.Compression;
using System.Reflection;
using System.Text;
using CriatorioVirtual.Application.BreedingFarms;

namespace CriatorioVirtual.Infrastructure.BreedingFarms.IdentityTemplates;

public sealed class BreedingFarmVisualIdentityTemplateCatalog : IVisualIdentityTemplateCatalog
{
    private const string ArchiveResourceSuffix = ".IdentityTemplates.Assets.criatorio-v0-138-identico.zip";
    private static readonly IReadOnlyList<VisualIdentityTemplateDefinition> Templates = CreateTemplates();

    public IReadOnlyList<VisualIdentityTemplateDefinition> GetAll() => Templates;

    private static IReadOnlyList<VisualIdentityTemplateDefinition> CreateTemplates()
    {
        var assembly = typeof(BreedingFarmVisualIdentityTemplateCatalog).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(candidate =>
            candidate.EndsWith(ArchiveResourceSuffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The visual identity template archive is missing.");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        return
        [
            Create(
                archive,
                "premium",
                "Premium",
                "premium.html",
                "RENASCENDO"),
            Create(
                archive,
                "natural",
                "Natural",
                "natural.html",
                "MORAIS"),
            Create(
                archive,
                "imperial",
                "Imperial",
                "imperial.html",
                "IMPERIAL"),
            Create(
                archive,
                "classico",
                "Clássico",
                "classico.html",
                "RENASCER")
        ];
    }

    private static VisualIdentityTemplateDefinition Create(
        ZipArchive archive,
        string id,
        string name,
        string fileName,
        string defaultName)
    {
        var entryName = $"criatorio-v0-138-identico/{fileName}";
        var entry = archive.GetEntry(entryName)
            ?? throw new InvalidOperationException($"The HTML asset for template '{id}' is missing.");
        using var content = entry.Open();
        using var reader = new StreamReader(content, Encoding.UTF8);
        var defaults = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = defaultName
        };
        const string version = "1.3.0";
        return new VisualIdentityTemplateDefinition(
            id,
            name,
            version,
            $"/api/breeding-farms/visual-identity/templates/{id}/{version}/preview",
            reader.ReadToEnd(),
            defaults,
            Array.Empty<BreedingFarmVisualIdentityTemplateOption>(),
            IsActive: true);
    }
}
