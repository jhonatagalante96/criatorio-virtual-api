using System.Text.Json;
using System.Text.RegularExpressions;
using CriatorioVirtual.Application.BreedingFarms;

namespace CriatorioVirtual.Infrastructure.BreedingFarms.CoverTemplates;

internal sealed record BreedingFarmCoverEffectiveConfiguration(
    IReadOnlyDictionary<string, object?> Values,
    string Serialized);

internal static partial class BreedingFarmCoverTemplateConfiguration
{
    private const int MaximumNameLength = 80;
    private const int MaximumTaglineLength = 120;
    private const int MaximumBadgeTextLength = 32;

    public static bool TryCreate(
        BreedingFarmCoverTemplateDefinition template,
        JsonElement configuration,
        string breedingFarmName,
        out BreedingFarmCoverEffectiveConfiguration effective)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(breedingFarmName);
        effective = new(new Dictionary<string, object?>(), string.Empty);

        var values = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in template.Defaults)
        {
            var publicKey = key.Equals("logoUrl", StringComparison.Ordinal) ? "logoAssetId" : key;
            values[publicKey] = ConvertJsonValue(value);
        }

        var supported = template.SupportedOptions.ToHashSet(StringComparer.Ordinal);
        if (configuration.ValueKind != JsonValueKind.Undefined)
        {
            if (configuration.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in configuration.EnumerateObject())
            {
                if (!supported.Contains(property.Name) || !seen.Add(property.Name) ||
                    !TryConvertOption(property.Name, property.Value, out var converted))
                {
                    return false;
                }

                values[property.Name] = converted;
            }
        }

        var name = breedingFarmName.Trim();
        values["name"] = name.Length <= MaximumNameLength ? name : name[..MaximumNameLength];
        values.TryAdd("tagline", string.Empty);
        values.TryAdd("showLogo", false);
        values.TryAdd("logoAssetId", null);
        values.TryAdd("accentColor", "#48643A");
        if (supported.Contains("showBadge"))
        {
            values.TryAdd("showBadge", false);
            values.TryAdd("badgeText", string.Empty);
        }

        if (!values.TryGetValue("tagline", out var tagline) || tagline is not string taglineText ||
            taglineText.Length > MaximumTaglineLength ||
            !values.TryGetValue("showLogo", out var showLogo) || showLogo is not bool ||
            !values.TryGetValue("logoAssetId", out var logoAssetId) ||
            (logoAssetId is not null && !string.Equals(logoAssetId as string, "current", StringComparison.Ordinal)) ||
            !values.TryGetValue("accentColor", out var accentColor) || accentColor is not string color ||
            !AccentColorRegex().IsMatch(color))
        {
            return false;
        }

        if (supported.Contains("showBadge") &&
            (!values.TryGetValue("showBadge", out var showBadge) || showBadge is not bool ||
             !values.TryGetValue("badgeText", out var badgeText) || badgeText is not string badgeTextValue ||
             badgeTextValue.Length > MaximumBadgeTextLength))
        {
            return false;
        }

        var effectiveValues = new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(values);
        effective = new(effectiveValues, JsonSerializer.Serialize(values));
        return true;
    }

    private static bool TryConvertOption(string key, JsonElement value, out object? converted)
    {
        converted = null;
        if (key is "showLogo" or "showBadge")
        {
            if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
            {
                return false;
            }

            converted = value.GetBoolean();
            return true;
        }

        if (key == "logoAssetId" && value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = value.GetString()?.Trim();
        if (text is null)
        {
            return false;
        }

        switch (key)
        {
            case "name":
                if (text.Length is < 1 or > MaximumNameLength)
                {
                    return false;
                }

                break;
            case "tagline":
                if (text.Length > MaximumTaglineLength)
                {
                    return false;
                }

                break;
            case "logoAssetId":
                if (!string.Equals(text, "current", StringComparison.Ordinal))
                {
                    return false;
                }

                break;
            case "accentColor":
                if (!AccentColorRegex().IsMatch(text))
                {
                    return false;
                }

                break;
            case "badgeText":
                if (text.Length > MaximumBadgeTextLength)
                {
                    return false;
                }

                break;
            default:
                return false;
        }

        converted = text;
        return true;
    }

    private static object? ConvertJsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.Null => null,
        _ => throw new InvalidOperationException("The cover template defaults contain an unsupported value.")
    };

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex AccentColorRegex();
}
