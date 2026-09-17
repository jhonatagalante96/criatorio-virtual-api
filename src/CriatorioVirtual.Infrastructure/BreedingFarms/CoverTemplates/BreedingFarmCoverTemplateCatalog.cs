using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CriatorioVirtual.Application.BreedingFarms;

namespace CriatorioVirtual.Infrastructure.BreedingFarms.CoverTemplates;

public sealed record BreedingFarmCoverTemplateDefinition(
    string Id,
    string Name,
    string Version,
    string PreviewUrl,
    string EntryHtml,
    byte[] BackgroundPng,
    byte[] PreviewImage,
    IReadOnlyDictionary<string, JsonElement> Defaults,
    IReadOnlyList<string> SupportedOptions,
    bool IsActive);

public interface IBreedingFarmCoverTemplateCatalog
{
    IReadOnlyList<BreedingFarmCoverTemplateDefinition> GetAll();

    BreedingFarmCoverTemplateDefinition? Find(string templateId);
}

public sealed class BreedingFarmCoverTemplateCatalog : IBreedingFarmCoverTemplateCatalog
{
    private const string ArchiveResourceSuffix = ".CoverTemplates.Assets.criatorio-cover-templates-v0.zip";
    private static readonly IReadOnlyList<BreedingFarmCoverTemplateDefinition> Templates = CreateTemplates();

    public IReadOnlyList<BreedingFarmCoverTemplateDefinition> GetAll() => Templates;

    public BreedingFarmCoverTemplateDefinition? Find(string templateId) => Templates.SingleOrDefault(candidate =>
        string.Equals(candidate.Id, templateId, StringComparison.Ordinal));

    private static IReadOnlyList<BreedingFarmCoverTemplateDefinition> CreateTemplates()
    {
        var assembly = typeof(BreedingFarmCoverTemplateCatalog).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(candidate =>
            candidate.EndsWith(ArchiveResourceSuffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The breeding-farm cover template archive is missing.");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var manifest = ReadManifest(archive);
        var baseStyles = ReadText(archive, "shared/base.css");
        var runtime = ReadText(archive, "shared/runtime.js");
        return manifest.Templates.Select(item => Create(archive, item, baseStyles, runtime)).ToArray();
    }

    private static BreedingFarmCoverTemplateDefinition Create(
        ZipArchive archive,
        CoverTemplateManifestItem item,
        string baseStyles,
        string runtime)
    {
        var entryHtml = ReadText(archive, item.Entry);
        var defaultsMatch = Regex.Match(entryHtml, "data-defaults='([^']*)'", RegexOptions.CultureInvariant);
        if (!defaultsMatch.Success)
        {
            throw new InvalidOperationException($"The defaults for cover template '{item.Id}' are missing.");
        }

        var supportedOptions = item.Options
            .Select(option => option.Equals("logoUrl", StringComparison.Ordinal) ? "logoAssetId" : option)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        using var defaultsDocument = JsonDocument.Parse(defaultsMatch.Groups[1].Value);
        var defaults = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in defaultsDocument.RootElement.EnumerateObject())
        {
            var key = property.Name.Equals("logoUrl", StringComparison.Ordinal) ? "logoAssetId" : property.Name;
            if (supportedOptions.Contains(key, StringComparer.Ordinal))
            {
                defaults[key] = property.Value.Clone();
            }
        }
        var background = ReadBytes(archive, $"assets/backgrounds/{item.Id}.png");
        return new BreedingFarmCoverTemplateDefinition(
            item.Id,
            item.Name,
            item.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"/api/breeding-farm-cover-templates/{Uri.EscapeDataString(item.Id)}/{item.Version}/preview",
            InlineResources(entryHtml, baseStyles, runtime, item.Id, background),
            background,
            ReadBytes(archive, item.Preview),
            defaults,
            supportedOptions,
            IsActive: true);
    }

    private static CoverTemplateManifest ReadManifest(ZipArchive archive)
    {
        var json = ReadText(archive, "manifest.json");
        return JsonSerializer.Deserialize<CoverTemplateManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("The cover template manifest is invalid.");
    }

    private static string InlineResources(
        string html,
        string baseStyles,
        string runtime,
        string templateId,
        byte[] background)
    {
        html = Regex.Replace(
            html,
            "<link\\s+rel=['\"]stylesheet['\"]\\s+href=['\"]../../shared/base\\.css['\"]\\s*/?>",
            $"<style>{baseStyles}</style>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        html = Regex.Replace(
            html,
            "<script\\s+src=['\"]../../shared/runtime\\.js['\"]\\s*></script>",
            $"<script>{runtime}</script>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var backgroundUri = $"data:image/png;base64,{Convert.ToBase64String(background)}";
        var relativeBackground = $"../../assets/backgrounds/{templateId}.png";
        html = html.Replace(relativeBackground, backgroundUri, StringComparison.Ordinal);
        html = html.Replace("<body>",
            $"<body><img alt='' aria-hidden='true' style='display:none' src='{backgroundUri}'>",
            StringComparison.OrdinalIgnoreCase);
        return html;
    }

    private static string ReadText(ZipArchive archive, string relativePath)
    {
        var entry = archive.GetEntry($"criatorio-cover-templates-v0/{relativePath}")
            ?? throw new InvalidOperationException($"Cover template asset '{relativePath}' is missing.");
        using var content = entry.Open();
        using var reader = new StreamReader(content, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] ReadBytes(ZipArchive archive, string relativePath)
    {
        var entry = archive.GetEntry($"criatorio-cover-templates-v0/{relativePath}")
            ?? throw new InvalidOperationException($"Cover template asset '{relativePath}' is missing.");
        using var content = entry.Open();
        using var result = new MemoryStream();
        content.CopyTo(result);
        return result.ToArray();
    }

    private sealed class CoverTemplateManifest
    {
        public List<CoverTemplateManifestItem> Templates { get; init; } = [];
    }

    private sealed class CoverTemplateManifestItem
    {
        public string Id { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public int Version { get; init; }

        public string Entry { get; init; } = string.Empty;

        public string Preview { get; init; } = string.Empty;

        public List<string> Options { get; init; } = [];
    }
}
