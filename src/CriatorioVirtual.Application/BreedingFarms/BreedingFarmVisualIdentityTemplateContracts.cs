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
    IReadOnlyList<string> Variants,
    string DefaultVariant,
    string PreviewSvg,
    bool IsActive);

public interface IVisualIdentityTemplateCatalog
{
    IReadOnlyList<VisualIdentityTemplateDefinition> GetAll();
}

public interface IVisualIdentityTemplateImageRenderer
{
    Task<byte[]> RenderPngAsync(
        VisualIdentityTemplateDefinition template,
        string breedingFarmName,
        string variant,
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
    VersionUnavailable
}

public sealed record GetBreedingFarmVisualIdentityTemplatePreviewResult(
    GetBreedingFarmVisualIdentityTemplatePreviewStatus Status,
    string? PreviewSvg);

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
    public static bool TryGetEffectiveVariant(
        VisualIdentityTemplateDefinition template,
        JsonElement configuration,
        out string variant,
        out string serializedConfiguration)
    {
        ArgumentNullException.ThrowIfNull(template);
        variant = template.DefaultVariant;
        serializedConfiguration = string.Empty;

        var hasVariant = false;
        if (configuration.ValueKind is JsonValueKind.Undefined)
        {
            // The catalog default is the effective value when clients omit the optional object.
        }
        else if (configuration.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in configuration.EnumerateObject())
            {
                if (!string.Equals(property.Name, "variant", StringComparison.Ordinal) ||
                    property.Value.ValueKind != JsonValueKind.String ||
                    hasVariant)
                {
                    return false;
                }

                variant = property.Value.GetString() ?? string.Empty;
                hasVariant = true;
            }
        }
        else
        {
            return false;
        }

        if ((!hasVariant && string.IsNullOrWhiteSpace(template.DefaultVariant)) ||
            !template.Variants.Contains(variant, StringComparer.Ordinal))
        {
            return false;
        }

        serializedConfiguration = JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["variant"] = variant
        });
        return true;
    }
}
