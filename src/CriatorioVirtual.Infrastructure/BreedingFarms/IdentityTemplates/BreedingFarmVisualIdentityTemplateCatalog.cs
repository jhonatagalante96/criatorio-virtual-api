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
                "RENASCENDO",
                ("subtitle", "MODELO PREMIUM")),
            Create(
                archive,
                "natural",
                "Natural",
                "natural.html",
                "MORAIS",
                ("tagline", "PÁSSAROS DE QUALIDADE"),
                ("subtitle", "TRADIÇÃO E RESPEITO")),
            Create(
                archive,
                "imperial",
                "Imperial",
                "imperial.html",
                "IMPERIAL",
                ("subtitle", "TRADIÇÃO E EXCELÊNCIA")),
            Create(
                archive,
                "classico",
                "Clássico",
                "classico.html",
                "RENASCER",
                ("subtitle", "MODELO CLÁSSICO"))
        ];
    }

    private static VisualIdentityTemplateDefinition Create(
        ZipArchive archive,
        string id,
        string name,
        string fileName,
        string defaultName,
        params (string Key, string Default)[] options)
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
        var declaredOptions = new List<BreedingFarmVisualIdentityTemplateOption>(options.Length);
        foreach (var (key, defaultValue) in options)
        {
            defaults.Add(key, defaultValue);
            declaredOptions.Add(new BreedingFarmVisualIdentityTemplateOption(
                key,
                "text",
                Required: false,
                defaultValue,
                []));
        }

        const string version = "1.1.0";
        return new VisualIdentityTemplateDefinition(
            id,
            name,
            version,
            $"/api/breeding-farms/visual-identity/templates/{id}/{version}/preview",
            reader.ReadToEnd(),
            defaults,
            Array.AsReadOnly(declaredOptions.ToArray()),
            IsActive: true);
    }
}
