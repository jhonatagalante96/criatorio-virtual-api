using System.Text.Json;
using CriatorioVirtual.Application.Messaging;

namespace CriatorioVirtual.Application.BreedingFarms;

public sealed record BreedingFarmVisualIdentityTemplateOption(
    string Key,
    string Type,
    bool Required,
    string Default,
    IReadOnlyList<string> Values);

public sealed record BreedingFarmVisualIdentityTemplateCatalogItem(
    string Id,
    string Name,
    string Version,
    string PreviewUrl,
    string AspectRatio,
    IReadOnlyList<BreedingFarmVisualIdentityTemplateOption> Options);

public sealed record VisualIdentityTemplateDefinition(
    string Id,
    string Name,
    string Version,
    string PreviewUrl,
    string PreviewHtml,
    IReadOnlyDictionary<string, string> DefaultConfiguration,
    IReadOnlyList<BreedingFarmVisualIdentityTemplateOption> Options,
    bool IsActive);

public interface IVisualIdentityTemplateCatalog
{
    IReadOnlyList<VisualIdentityTemplateDefinition> GetAll();
}

public interface IVisualIdentityTemplateImageRenderer
{
    Task<byte[]> RenderPngAsync(
        VisualIdentityTemplateDefinition template,
        IReadOnlyDictionary<string, string> configuration,
        CancellationToken cancellationToken = default);
}

public sealed record GetBreedingFarmVisualIdentityTemplatesQuery
    : IQuery<IReadOnlyList<BreedingFarmVisualIdentityTemplateCatalogItem>>;

public sealed record GetBreedingFarmVisualIdentityTemplatePreviewQuery(string TemplateId, string Version)
    : IQuery<GetBreedingFarmVisualIdentityTemplatePreviewResult>;

public enum GetBreedingFarmVisualIdentityTemplatePreviewStatus
{
    Available,
    NotFound,
    Inactive,
    VersionUnavailable,
    RenderingUnavailable
}

public sealed record GetBreedingFarmVisualIdentityTemplatePreviewResult(
    GetBreedingFarmVisualIdentityTemplatePreviewStatus Status,
    byte[]? PreviewPng);

public sealed record PreviewBreedingFarmVisualIdentityTemplateQuery(
    Guid UserId,
    string TemplateId,
    string Version,
    JsonElement Configuration)
    : IQuery<PreviewBreedingFarmVisualIdentityTemplateResult>;

public enum PreviewBreedingFarmVisualIdentityTemplateStatus
{
    PreviewReady,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    TemplateNotFound,
    TemplateInactive,
    VersionUnavailable,
    InvalidConfiguration,
    RenderingUnavailable
}

public sealed record PreviewBreedingFarmVisualIdentityTemplateResult(
    PreviewBreedingFarmVisualIdentityTemplateStatus Status,
    byte[]? PngContent);

public sealed record ApplyBreedingFarmVisualIdentityTemplateCommand(
    Guid UserId,
    string TemplateId,
    string Version,
    JsonElement Configuration)
    : ICommand<ApplyBreedingFarmVisualIdentityTemplateResult>;

public enum ApplyBreedingFarmVisualIdentityTemplateStatus
{
    Applied,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    TemplateNotFound,
    TemplateInactive,
    VersionUnavailable,
    InvalidConfiguration,
    RenderingUnavailable,
    StorageUnavailable
}

public sealed record ApplyBreedingFarmVisualIdentityTemplateResult(
    ApplyBreedingFarmVisualIdentityTemplateStatus Status,
    Guid? BreedingFarmId,
    BreedingFarmVisualIdentityMetadata? Identity,
    BreedingFarmVisualIdentityCleanup? PreviousAsset);

public static class BreedingFarmVisualIdentityTemplateConfiguration
{
    private const int MaximumTextLength = 120;

    public static bool TryGetEffectiveConfiguration(
        VisualIdentityTemplateDefinition template,
        JsonElement configuration,
        string breedingFarmName,
        out IReadOnlyDictionary<string, string> effectiveConfiguration,
        out string serializedConfiguration)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(breedingFarmName);
        effectiveConfiguration = new Dictionary<string, string>();
        serializedConfiguration = string.Empty;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (configuration.ValueKind is JsonValueKind.Undefined)
        {
            // Optional model fields use the declared defaults.
        }
        else if (configuration.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in configuration.EnumerateObject())
            {
                var option = template.Options.SingleOrDefault(candidate =>
                    string.Equals(candidate.Key, property.Name, StringComparison.Ordinal));
                if (option is null ||
                    !seen.Add(property.Name) ||
                    property.Value.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                var value = property.Value.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumTextLength)
                {
                    return false;
                }

                values.Add(option.Key, value);
            }
        }
        else
        {
            return false;
        }

        var effective = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in template.DefaultConfiguration)
        {
            effective.Add(pair.Key, pair.Value);
        }

        foreach (var pair in values)
        {
            effective[pair.Key] = pair.Value;
        }

        effective["name"] = breedingFarmName.Trim();
        effectiveConfiguration = effective;
        serializedConfiguration = JsonSerializer.Serialize(effective);
        return true;
    }
}
