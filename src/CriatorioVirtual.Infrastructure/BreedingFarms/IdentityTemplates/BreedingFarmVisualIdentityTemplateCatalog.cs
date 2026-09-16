using System.Reflection;
using CriatorioVirtual.Application.BreedingFarms;

namespace CriatorioVirtual.Infrastructure.BreedingFarms.IdentityTemplates;

public sealed class BreedingFarmVisualIdentityTemplateCatalog : IVisualIdentityTemplateCatalog
{
    private static readonly IReadOnlyList<VisualIdentityTemplateDefinition> Templates = CreateTemplates();

    public IReadOnlyList<VisualIdentityTemplateDefinition> GetAll() => Templates;

    private static IReadOnlyList<VisualIdentityTemplateDefinition> CreateTemplates() =>
    [
        Create("folhagem-classica", "Folhagem Clássica", ["brand", "forest"], "brand"),
        Create("selo-criador", "Selo do Criador", ["brand", "jade"], "brand"),
        Create("ramo-natural", "Ramo Natural", ["natural", "brand"], "natural"),
        Create("noturno-minimalista", "Noturno Minimalista", ["navy", "forest-night"], "navy")
    ];

    private static VisualIdentityTemplateDefinition Create(
        string id,
        string name,
        IReadOnlyList<string> variants,
        string defaultVariant)
    {
        var assembly = typeof(BreedingFarmVisualIdentityTemplateCatalog).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(candidate =>
            candidate.EndsWith($".IdentityTemplates.Assets.{id}.svg", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The preview asset for template '{id}' is missing.");
        using var reader = new StreamReader(stream);

        const string version = "1.0.0";
        return new VisualIdentityTemplateDefinition(
            id,
            name,
            version,
            $"/api/breeding-farms/visual-identity/templates/{id}/{version}/preview",
            Array.AsReadOnly(variants.ToArray()),
            defaultVariant,
            reader.ReadToEnd(),
            IsActive: true);
    }
}
