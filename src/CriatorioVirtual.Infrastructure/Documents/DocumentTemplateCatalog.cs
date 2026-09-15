using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.Documents;

namespace CriatorioVirtual.Infrastructure.Documents;

/// <summary>
/// Loads the supplied HTML/CSS document designs and applies the document snapshot
/// to their <c>data-field</c> contract.
/// </summary>
public static partial class DocumentTemplateCatalog
{
    private const string ResourcePrefix = "CriatorioVirtual.Infrastructure.Documents.Templates.";
    private const string AssetResourcePrefix = "CriatorioVirtual.Infrastructure.Documents.Assets.";
    private const string MissingValue = "Não informado";
    private const string DefaultPhotoResourceName = $"{AssetResourcePrefix}criatorio-virtual-default-bird.jpg";
    private const string DefaultPhotoContentType = "image/jpeg";
    private static readonly Lazy<string> DefaultPhotoDataUri = new(CreateDefaultPhotoDataUri);

    private static readonly Assembly ResourceAssembly = typeof(DocumentTemplateCatalog).Assembly;

    public static string BindGenealogyCertificate(
        BirdDocumentSnapshot snapshot,
        GenealogyCertificateModelId modelId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Enum.IsDefined(modelId))
        {
            throw new ArgumentOutOfRangeException(nameof(modelId));
        }

        var templateName = modelId switch
        {
            GenealogyCertificateModelId.ClassicPremium => "certificado-genealogia-classico-premium",
            GenealogyCertificateModelId.Institutional => "certificado-genealogia-institucional-claro",
            GenealogyCertificateModelId.Modern => "certificado-genealogia-moderno",
            _ => throw new ArgumentOutOfRangeException(nameof(modelId))
        };

        return Bind(templateName, snapshot, isProvenance: false);
    }

    public static string BindProvenanceDocument(BirdDocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Bind("documento-procedencia-institucional", snapshot, isProvenance: true);
    }

    private static string Bind(string templateName, BirdDocumentSnapshot snapshot, bool isProvenance)
    {
        var html = ReadResource($"{ResourcePrefix}{templateName}.html");
        var css = ReadResource($"{ResourcePrefix}{templateName}.css");
        var fields = CreateFields(snapshot, isProvenance);

        html = StylesheetLinkRegex().Replace(
            html,
            $"<style>{css}</style>");
        html = StaticAssetRegex().Replace(
            html,
            match =>
            {
                var assetName = match.Groups["asset"].Value switch
                {
                    "criatorio-virtual-simbolo.png" => "criatorio-virtual-symbol.png",
                    "criatorio-virtual-horizontal.png" => "criatorio-virtual-horizontal.png",
                    "ave-referencia-premium-clean.jpg" => "ave-referencia-premium-clean.jpg",
                    _ => null
                };
                if (assetName is null)
                {
                    return match.Value;
                }

                var contentType = Path.GetExtension(assetName).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                    ? "image/jpeg"
                    : "image/png";
                var content = Convert.ToBase64String(ReadResourceBytes($"{AssetResourcePrefix}{assetName}"));
                return match.Groups["prefix"].Value
                    + $"data:{contentType};base64,{content}"
                    + match.Groups["suffix"].Value;
            });
        html = isProvenance
            ? ApplyProvenanceFieldAttributes(html)
            : ApplyGenealogyFieldAttributes(html);
        html = FieldRegex().Replace(
            html,
            match =>
            {
                if (!string.Equals(
                        match.Groups["tag"].Value,
                        match.Groups["closingTag"].Value,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return match.Value;
                }

                var field = match.Groups["field"].Value;
                var value = fields.TryGetValue(field, out var fieldValue) ? fieldValue : MissingValue;
                return match.Groups["opening"].Value
                    + System.Net.WebUtility.HtmlEncode(value)
                    + match.Groups["closing"].Value;
            });
        html = ImageFieldRegex().Replace(
            html,
            match =>
            {
                var content = GetPhotoDataUri(snapshot.Photo);
                return match.Groups["prefix"].Value
                    + content
                    + match.Groups["suffix"].Value;
            });

        return html;
    }

    private static string GetPhotoDataUri(DocumentPhotoSnapshot? photo)
    {
        if (photo is null || photo.Content.Length == 0)
        {
            return DefaultPhotoDataUri.Value;
        }

        var contentType = photo.ContentType.Trim().ToLowerInvariant();
        return contentType is "image/jpeg" or "image/jpg" or "image/png"
            ? $"data:{contentType};base64,{Convert.ToBase64String(photo.Content)}"
            : DefaultPhotoDataUri.Value;
    }

    private static string CreateDefaultPhotoDataUri() =>
        $"data:{DefaultPhotoContentType};base64,{Convert.ToBase64String(ReadResourceBytes(DefaultPhotoResourceName))}";

    private static string ApplyGenealogyFieldAttributes(string html)
    {
        var positions = new List<string> { "root", "pai", "mae" };
        foreach (var generation in Enumerable.Range(2, 4))
        {
            positions.AddRange(BuildPositions(generation));
        }

        var index = 0;
        return TreeNodeRegex().Replace(
            html,
            match =>
            {
                if (index >= positions.Count)
                {
                    return match.Value;
                }

                var position = positions[index++];
                var fieldPrefix = position == "root" ? "ave" : $"genealogia.{position}";
                var node = NameOpeningRegex().Replace(
                    match.Value,
                    $"<b data-field=\"{fieldPrefix}.nome\">");
                return SmallOpeningRegex().Replace(
                    node,
                    $"<small data-field=\"{fieldPrefix}.anilha\">");
            });
    }

    private static string ApplyProvenanceFieldAttributes(string html)
    {
        var fieldByLabel = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Avô paterno"] = "genealogia.pai.pai.nome",
            ["Avó paterna"] = "genealogia.pai.mae.nome",
            ["Avô materno"] = "genealogia.mae.pai.nome",
            ["Avó materna"] = "genealogia.mae.mae.nome"
        };

        return ProvenanceValueRegex().Replace(
            html,
            match =>
            {
                var label = match.Groups["label"].Value;
                return $"<dt>{label}</dt><dd data-field=\"{fieldByLabel[label]}\">{match.Groups["value"].Value}</dd>";
            });
    }

    private static IEnumerable<string> BuildPositions(int generation)
    {
        var positions = new[] { "pai", "mae" };
        foreach (var _ in Enumerable.Range(2, generation - 1))
        {
            positions = positions
                .SelectMany(position => new[] { $"{position}.pai", $"{position}.mae" })
                .ToArray();
        }

        return positions;
    }

    private static Dictionary<string, string> CreateFields(
        BirdDocumentSnapshot snapshot,
        bool isProvenance)
    {
        var farm = snapshot.BreedingFarmDetails;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["criatorio.nome"] = snapshot.BreedingFarmName,
            ["criatorio.responsavel"] = farm?.ResponsibleName ?? MissingValue,
            ["criatorio.registro"] = farm?.OfficialRegistrationNumber ?? MissingValue,
            ["criatorio.cidade_uf"] = MissingValue,
            ["criatorio.telefone"] = farm?.ContactPhone ?? MissingValue,
            ["criatorio.email"] = farm?.ContactEmail ?? MissingValue,
            ["ave.nome"] = snapshot.Name,
            ["ave.especie"] = snapshot.Species,
            ["ave.anilha"] = snapshot.RingNumber ?? MissingValue,
            ["ave.sexo"] = GetSexLabel(snapshot.Sex),
            ["ave.data_nascimento"] = FormatDate(snapshot.BirthDate),
            ["ave.origem"] = MissingValue,
            ["documento.data_emissao"] = FormatDate(snapshot.IssuedAtUtc),
            ["documento.identificador"] = GetDocumentIdentifier(snapshot, isProvenance)
        };

        foreach (var position in GetKnownGenealogyPositions())
        {
            var node = snapshot.Genealogy.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Position,
                    ToSnapshotPosition(position),
                    StringComparison.OrdinalIgnoreCase));
            fields[$"genealogia.{position}.nome"] = node?.Name ?? MissingValue;
            fields[$"genealogia.{position}.anilha"] = node?.RingNumber ?? MissingValue;
            fields[$"genealogia.{position}.sexo"] = node?.Sex is { } sex ? GetSexLabel(sex) : MissingValue;
            fields[$"genealogia.{position}.data_nascimento"] = FormatDate(node?.BirthDate);
        }

        return fields;
    }

    private static string ToSnapshotPosition(string position) => string.Join(
        '.',
        position.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment switch
            {
                "pai" => "father",
                "mae" => "mother",
                _ => segment
            }));

    private static IEnumerable<string> GetKnownGenealogyPositions()
    {
        yield return "pai";
        yield return "mae";
        foreach (var side in new[] { "pai", "mae" })
        {
            foreach (var grandparent in new[] { "pai", "mae" })
            {
                yield return $"{side}.{grandparent}";
                foreach (var greatGrandparent in new[] { "pai", "mae" })
                {
                    yield return $"{side}.{grandparent}.{greatGrandparent}";
                    foreach (var greatGreatGrandparent in new[] { "pai", "mae" })
                    {
                        yield return $"{side}.{grandparent}.{greatGrandparent}.{greatGreatGrandparent}";
                    }
                }
            }
        }
    }

    private static string GetDocumentIdentifier(BirdDocumentSnapshot snapshot, bool isProvenance)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.InternalDocumentIdentifier))
        {
            return snapshot.InternalDocumentIdentifier;
        }

        var prefix = isProvenance ? "DP" : "GEN";
        var suffix = snapshot.RingNumber ?? snapshot.BirdId.ToString("N")[..8].ToUpperInvariant();
        return $"{prefix}-{suffix}";
    }

    private static string FormatDate(DateOnly? date) =>
        date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? MissingValue;

    private static string FormatDate(DateTimeOffset? date) =>
        date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? MissingValue;

    private static string GetSexLabel(BirdSex sex) => sex switch
    {
        BirdSex.Male => "Macho",
        BirdSex.Female => "Fêmea",
        BirdSex.Unknown => MissingValue,
        _ => MissingValue
    };

    private static string ReadResource(string resourceName)
    {
        using var stream = ResourceAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The document template resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static byte[] ReadResourceBytes(string resourceName)
    {
        using var stream = ResourceAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The document asset resource '{resourceName}' was not found.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    [GeneratedRegex("<link\\b[^>]*rel=[\\\"']stylesheet[\\\"'][^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex StylesheetLinkRegex();

    [GeneratedRegex("(?<prefix>src=[\\\"'])assets/(?<asset>[^\\\"']+)(?<suffix>[\\\"'])", RegexOptions.IgnoreCase)]
    private static partial Regex StaticAssetRegex();

    [GeneratedRegex("(?<opening><(?<tag>[A-Za-z][\\w:-]*)\\b[^>]*?data-field=[\\\"'](?<field>[^\\\"']+)[\\\"'][^>]*>)(?<value>[^<]*)(?<closing></(?<closingTag>[A-Za-z][\\w:-]*)>)", RegexOptions.IgnoreCase)]
    private static partial Regex FieldRegex();

    [GeneratedRegex("<article\\b[^>]*class=[\\\"'][^\\\"']*\\bnode\\b[^\\\"']*[\\\"'][^>]*>\\s*<b\\b[^>]*>.*?</article>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TreeNodeRegex();

    [GeneratedRegex("<b\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex NameOpeningRegex();

    [GeneratedRegex("<small\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex SmallOpeningRegex();

    [GeneratedRegex("<dt>(?<label>Av(?:ô|ó) (?:paterna|paterno|materna|materno))</dt><dd(?:\\s+data-field=[\\\"'][^\\\"']+[\\\"'])?>(?<value>.*?)</dd>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ProvenanceValueRegex();

    [GeneratedRegex("(?<prefix><img\\b[^>]*?\\bsrc=[\\\"'])[^\\\"']+(?<suffix>[\\\"'][^>]*?\\bdata-field-src=[\\\"'][^\\\"']+[\\\"'][^>]*>)", RegexOptions.IgnoreCase)]
    private static partial Regex ImageFieldRegex();
}
