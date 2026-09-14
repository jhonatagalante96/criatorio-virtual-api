using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Domain.Birds;

namespace CriatorioVirtual.Infrastructure.Documents;

internal static class BadgeTemplateCatalog
{
    private static readonly Assembly ResourceAssembly = typeof(BadgeTemplateCatalog).Assembly;
    private static readonly ConcurrentDictionary<string, string> ResourceCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex PlaceholderPattern = new(@"\{\{([^}]+)\}\}", RegexOptions.Compiled);
    private static readonly Regex BackSectionPattern = new(
        @"\s*<section class=""badge-card [^""]+--back"">.*?</section>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex BadgeSectionPattern = new(
        @"<section class=""badge-card [^""]+"">.*?</section>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly IReadOnlyDictionary<BadgeModelId, string> TemplateNames =
        new Dictionary<BadgeModelId, string>
        {
            [BadgeModelId.Classic] = "classico",
            [BadgeModelId.Minimalist] = "minimalista",
            [BadgeModelId.Competition] = "competicao",
            [BadgeModelId.Photographic] = "fotografico"
        };
    private static readonly IReadOnlyDictionary<DocumentField, string> FieldClasses =
        new Dictionary<DocumentField, string>
        {
            [DocumentField.Name] = "name",
            [DocumentField.RingNumber] = "ring-number",
            [DocumentField.Sex] = "sex",
            [DocumentField.Species] = "species",
            [DocumentField.BirthDate] = "birth-date"
        };
    internal static string Bind(
        BirdDocumentSnapshot snapshot,
        BadgeRenderConfiguration configuration,
        double widthMillimeters,
        double heightMillimeters)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(configuration);

        var templateName = TemplateNames[configuration.ModelId];
        var baseHeightMillimeters = configuration.ModelId is BadgeModelId.Competition or BadgeModelId.Photographic
            ? 59d
            : 54d;
        return BuildHtml(snapshot, configuration, templateName, widthMillimeters, heightMillimeters, baseHeightMillimeters);
    }

    private static string BuildHtml(
        BirdDocumentSnapshot snapshot,
        BadgeRenderConfiguration configuration,
        string templateName,
        double widthMillimeters,
        double heightMillimeters,
        double baseHeightMillimeters)
    {
        var baseWidthMillimeters = 86d;
        var scaleX = widthMillimeters / baseWidthMillimeters;
        var scaleY = heightMillimeters / baseHeightMillimeters;
        var widthCss = widthMillimeters.ToString("0.###", CultureInfo.InvariantCulture);
        var heightCss = heightMillimeters.ToString("0.###", CultureInfo.InvariantCulture);
        var scaleXCss = scaleX.ToString("0.######", CultureInfo.InvariantCulture);
        var scaleYCss = scaleY.ToString("0.######", CultureInfo.InvariantCulture);
        var html = ReadResource($"templates.{templateName}.html");
        var sharedCss = ReadResource("templates.shared.css");
        var modelCss = ReadResource($"templates.{templateName}.css");
        var printOverrides =
            $"@page {{ size: {widthCss}mm {heightCss}mm; margin: 0; }}" +
            $"html, body {{ width: {widthCss}mm; height: {heightCss}mm; }}" +
            $".badge-viewport {{ width: {widthCss}mm; height: {heightCss}mm; overflow: hidden; break-after: page; page-break-after: always; }}" +
            $".badge-viewport:last-child {{ break-after: auto; page-break-after: auto; }}" +
            $".badge-card {{ transform: scale({scaleXCss}, {scaleYCss}); transform-origin: top left; break-after: auto; page-break-after: auto; }}" +
            $".badge-sheet {{ width: {widthCss}mm; height: {heightCss}mm; padding: 0; display: block; }}";
        var selectedFields = configuration.SelectedFields.ToHashSet();
        var selectedClass = selectedFields.Contains(DocumentField.BirdPhoto) ? string.Empty : ".badge-photo { visibility: hidden !important; }";
        var hiddenFields = new StringBuilder();
        foreach (var fieldClass in FieldClasses.Values.Where(fieldClass =>
                     !selectedFields.Any(field => FieldClasses.TryGetValue(field, out var selectedClassName) && selectedClassName == fieldClass)))
        {
            hiddenFields.Append($".field--{fieldClass} {{ display: none !important; }}");
        }

        html = html
            .Replace("<link rel=\"stylesheet\" href=\"shared.css\">", string.Empty, StringComparison.Ordinal)
            .Replace($"<link rel=\"stylesheet\" href=\"{templateName}.css\">", string.Empty, StringComparison.Ordinal)
            .Replace("</head>", $"<style>{sharedCss}{modelCss}{printOverrides}{selectedClass}{hiddenFields}</style></head>", StringComparison.Ordinal);
        foreach (var (label, fieldClass) in new Dictionary<string, string>
                 {
                     ["Nome"] = "name",
                     ["Número da anilha"] = "ring-number",
                     ["Sexo"] = "sex",
                     ["Espécie"] = "species",
                     ["Data de nascimento"] = "birth-date"
                 })
        {
            html = html.Replace(
                $"<div><span class=\"field-label\">{label}</span>",
                $"<div class=\"field--{fieldClass}\"><span class=\"field-label\">{label}</span>",
                StringComparison.Ordinal);
        }
        if (!selectedFields.Contains(DocumentField.GenealogyTree))
        {
            html = BackSectionPattern.Replace(html, string.Empty);
        }

        html = BadgeSectionPattern.Replace(html, match => $"<div class=\"badge-viewport\">{match.Value}</div>");

        return PlaceholderPattern.Replace(html, match =>
        {
            var path = match.Groups[1].Value.Trim();
            return WebUtility.HtmlEncode(GetValue(path, snapshot));
        });
    }

    private static string GetValue(string path, BirdDocumentSnapshot snapshot)
    {
        var placeholder = GetPlaceholder(path, snapshot);
        if (placeholder is not null)
        {
            return placeholder;
        }

        return path switch
        {
            "bird.name" => snapshot.Name,
            "bird.ringNumber" => snapshot.RingNumber ?? "Nao informado",
            "bird.sex" => snapshot.Sex switch
            {
                BirdSex.Male => "Macho",
                BirdSex.Female => "Femea",
                _ => "Nao informado"
            },
            "bird.species" => snapshot.Species,
            "bird.birthDate" => snapshot.BirthDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Nao informado",
            "bird.photoUrl" => ToDataUri(snapshot.Photo?.ContentType, snapshot.Photo?.Content) ?? GetResourceDataUri("assets.bird-placeholder.svg", "image/svg+xml"),
            "logo.fullLight" => GetResourceDataUri("assets.official.logo-full-light.png", "image/png"),
            "logo.fullDark" => GetResourceDataUri("assets.official.logo-full-green.png", "image/png"),
            "logo.fullGold" => GetResourceDataUri("assets.official.logo-full-gold.png", "image/png"),
            "logo.markDark" => GetResourceDataUri("assets.official.logo-mark-green.png", "image/png"),
            "assets.competicao.diamond" => GetResourceDataUri("assets.extracted.competicao-diamond-transparent.png", "image/png"),
            "assets.competicao.crown" => GetResourceDataUri("assets.extracted.competicao-crown-transparent.png", "image/png"),
            "assets.competicao.laurel" => GetResourceDataUri("assets.extracted.competicao-laurel-transparent.png", "image/png"),
            "assets.competicao.seal" => GetResourceDataUri("assets.extracted.competicao-selo-hd.png", "image/png"),
            _ when path.EndsWith(".photoUrl", StringComparison.Ordinal) => GetResourceDataUri("assets.bird-placeholder.svg", "image/svg+xml"),
            _ => ""
        };
    }

    private static string? GetPlaceholder(string path, BirdDocumentSnapshot snapshot)
    {
        if (path == "bird.ringNumber")
        {
            return null;
        }

        var ancestor = snapshot.Genealogy.FirstOrDefault(node => path switch
        {
            "bird.father.name" or "bird.father.ringNumber" or "bird.father.photoUrl" => node.Position == "father",
            "bird.mother.name" or "bird.mother.ringNumber" or "bird.mother.photoUrl" => node.Position == "mother",
            "bird.father.father.ringNumber" or "bird.father.father.photoUrl" => node.Position == "father.father",
            "bird.father.mother.ringNumber" or "bird.father.mother.photoUrl" => node.Position == "father.mother",
            "bird.mother.father.ringNumber" or "bird.mother.father.photoUrl" => node.Position == "mother.father",
            "bird.mother.mother.ringNumber" or "bird.mother.mother.photoUrl" => node.Position == "mother.mother",
            _ => false
        });
        if (ancestor is null && !path.Contains("photoUrl", StringComparison.Ordinal))
        {
            return path switch
            {
                "bird.father.name" or "bird.mother.name" => "Nao informado",
                _ when path.EndsWith("ringNumber", StringComparison.Ordinal) => "Cadastro pendente",
                _ => null
            };
        }

        if (path.EndsWith(".name", StringComparison.Ordinal))
        {
            return ancestor?.Name ?? "Nao informado";
        }

        if (path.EndsWith(".ringNumber", StringComparison.Ordinal))
        {
            return ancestor?.RingNumber ?? "Cadastro pendente";
        }

        if (path.EndsWith(".photoUrl", StringComparison.Ordinal))
        {
            return GetResourceDataUri("assets.bird-placeholder.svg", "image/svg+xml");
        }

        return null;
    }

    private static string? ToDataUri(string? contentType, byte[]? content) =>
        content is { Length: > 0 } && !string.IsNullOrWhiteSpace(contentType)
            ? $"data:{contentType};base64,{Convert.ToBase64String(content)}"
            : null;

    private static string ReadResource(string suffix)
    {
        return ResourceCache.GetOrAdd(suffix, static resourceSuffix =>
        {
            var resourceName = ResourceAssembly.GetManifestResourceNames()
                .Single(name => name.EndsWith(resourceSuffix.Replace('/', '.'), StringComparison.OrdinalIgnoreCase));
            using var stream = ResourceAssembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded badge template resource '{resourceSuffix}' was not found.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        });
    }

    private static string GetResourceDataUri(string suffix, string contentType)
    {
        var cacheKey = $"{suffix}|{contentType}";
        return ResourceCache.GetOrAdd(cacheKey, static key =>
        {
            var separator = key.IndexOf('|');
            var resourceSuffix = key[..separator];
            var mimeType = key[(separator + 1)..];
            var resourceName = ResourceAssembly.GetManifestResourceNames()
                .Single(name => name.EndsWith(resourceSuffix.Replace('/', '.'), StringComparison.OrdinalIgnoreCase));
            using var stream = ResourceAssembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded badge asset resource '{resourceSuffix}' was not found.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return $"data:{mimeType};base64,{Convert.ToBase64String(memory.ToArray())}";
        });
    }
}
