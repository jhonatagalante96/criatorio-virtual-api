using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Infrastructure.Documents;

namespace CriatorioVirtual.Infrastructure.BreedingFarms.IdentityTemplates;

public sealed class BreedingFarmVisualIdentityTemplateImageRenderer(IHtmlToPngRenderer htmlRenderer)
    : IVisualIdentityTemplateImageRenderer
{
    private static readonly XNamespace SvgNamespace = "http://www.w3.org/2000/svg";

    public async Task<byte[]> RenderPngAsync(
        VisualIdentityTemplateDefinition template,
        string breedingFarmName,
        string variant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(breedingFarmName);
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);
        if (!template.Variants.Contains(variant, StringComparer.Ordinal))
        {
            throw new ArgumentException("The selected variant is not declared by this identity template.", nameof(variant));
        }

        var document = XDocument.Parse(template.PreviewSvg, LoadOptions.None);
        var root = document.Root ?? throw new InvalidOperationException("The identity template SVG has no root element.");
        var name = root.Descendants(SvgNamespace + "text")
            .SingleOrDefault(element => (string?)element.Attribute("id") == "breeder-name")
            ?? throw new InvalidOperationException("The identity template SVG has no breeder-name text element.");
        name.SetAttributeValue("data-max-width", template.Id == "selo-criador" ? "600" : template.Id == "ramo-natural" ? "380" : "820");
        name.SetAttributeValue("data-min-font-size", template.Id switch
        {
            "selo-criador" => "48",
            "ramo-natural" => "54",
            "noturno-minimalista" => "52",
            _ => "56"
        });
        name.SetAttributeValue("data-max-font-size", template.Id switch
        {
            "selo-criador" => "82",
            "ramo-natural" => "96",
            "noturno-minimalista" => "92",
            _ => "104"
        });
        name.SetAttributeValue("data-single-y", template.Id switch
        {
            "selo-criador" => "738",
            "ramo-natural" => "336",
            "noturno-minimalista" => "726",
            _ => "771"
        });
        name.SetAttributeValue("data-first-y", template.Id switch
        {
            "selo-criador" => "690",
            "ramo-natural" => "336",
            "noturno-minimalista" => "682",
            _ => "724"
        });
        name.SetAttributeValue("data-second-y", template.Id switch
        {
            "selo-criador" => "780",
            "ramo-natural" => "432",
            "noturno-minimalista" => "776",
            _ => "818"
        });

        ApplyVariant(root, template.Id, variant);
        var title = root.Element(SvgNamespace + "title");
        if (title is not null)
        {
            title.Value = $"Identidade visual de {breedingFarmName.Trim()}";
        }

        var description = root.Element(SvgNamespace + "desc");
        if (description is not null)
        {
            description.Value = $"Identidade visual do criatório gerada pelo modelo {template.Name}.";
        }

        var html = CreateHtml(document.ToString(SaveOptions.DisableFormatting), breedingFarmName);
        return await htmlRenderer.RenderPngAsync(html, 1024, 1024, cancellationToken);
    }

    private static void ApplyVariant(XElement root, string templateId, string variant)
    {
        var palette = (templateId, variant) switch
        {
            ("folhagem-classica", "forest") => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["#47BD59"] = "#8CBF70", ["#0D5872"] = "#315846", ["#239B4D"] = "#527D50", ["#063A56"] = "#173F32"
            },
            ("selo-criador", "jade") => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["#063A56"] = "#185C43", ["#47BD59"] = "#79B88C"
            },
            ("ramo-natural", "brand") => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["#48725B"] = "#0D5872", ["#86A96B"] = "#47BD59", ["#5D8D61"] = "#239B4D", ["#9BBE73"] = "#6AAB60"
            },
            ("noturno-minimalista", "forest-night") => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["#062F45"] = "#082E28", ["#1C5268"] = "#316647", ["#083D56"] = "#103D32", ["#47BD59"] = "#83B65B"
            },
            _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        foreach (var attribute in root.DescendantsAndSelf().Attributes().Where(attribute =>
                     attribute.Name.LocalName is "fill" or "stroke"))
        {
            if (palette.TryGetValue(attribute.Value, out var color))
            {
                attribute.Value = color;
            }
        }
    }

    private static string CreateHtml(string svg, string name)
    {
        var encodedName = Convert.ToBase64String(Encoding.UTF8.GetBytes(name));
        var html = """
            <!doctype html>
            <html lang="pt-BR">
              <head>
                <meta charset="utf-8">
                <style>
                  * { box-sizing: border-box; }
                  html, body { width: 1024px; height: 1024px; margin: 0; overflow: hidden; }
                  body { font-family: Arial, Helvetica, sans-serif; }
                  svg { display: block; width: 1024px; height: 1024px; }
                </style>
              </head>
              <body>
                <div id="canvas">
                  __SVG_CONTENT__
                </div>
                <script>
                  (() => {
                    const encodedName = '__ENCODED_NAME__';
                    const name = new TextDecoder().decode(Uint8Array.from(atob(encodedName), value => value.charCodeAt(0)))
                      .normalize('NFC').replace(/\s+/gu, ' ').trim();
                    const svg = document.querySelector('svg');
                    const text = svg.querySelector('#breeder-name');
                    const measureCanvas = document.createElement('canvas');
                    const context = measureCanvas.getContext('2d');
                    const maxWidth = Number(text.dataset.maxWidth);
                    const minSize = Number(text.dataset.minFontSize);
                    const maxSize = Number(text.dataset.maxFontSize);
                    const family = text.getAttribute('font-family') || 'Arial, Helvetica, sans-serif';
                    const weight = text.getAttribute('font-weight') || '700';
                    const measure = (value, size) => {
                      context.font = `${weight} ${size}px ${family}`;
                      return context.measureText(value).width;
                    };
                    const truncate = (value, size) => {
                      let result = value;
                      while (result.length > 0 && measure(`${result}…`, size) > maxWidth) {
                        result = Array.from(result).slice(0, -1).join('');
                      }
                      return result ? `${result}…` : '…';
                    };
                    const words = name.split(' ');
                    let fontSize = maxSize;
                    while (fontSize > minSize && measure(name, fontSize) > maxWidth) fontSize -= 1;
                    let lines = [name];
                    if (measure(name, fontSize) > maxWidth) {
                      fontSize = minSize;
                      let first = '';
                      while (words.length > 0) {
                        const candidate = first ? `${first} ${words[0]}` : words[0];
                        if (first && measure(candidate, fontSize) > maxWidth) break;
                        first = candidate;
                        words.shift();
                        if (measure(first, fontSize) > maxWidth) {
                          first = truncate(first, fontSize);
                          break;
                        }
                      }
                      const second = words.join(' ');
                      lines = second ? [first, truncate(second, fontSize)] : [first];
                    }

                    const x = Number(text.getAttribute('x'));
                    const anchor = text.getAttribute('text-anchor') || 'start';
                    const firstY = lines.length === 1 ? Number(text.dataset.singleY) : Number(text.dataset.firstY);
                    text.setAttribute('font-size', String(fontSize));
                    text.replaceChildren();
                    lines.forEach((line, index) => {
                      const span = document.createElementNS('http://www.w3.org/2000/svg', 'tspan');
                      span.setAttribute('x', String(x));
                      span.setAttribute('y', String(lines.length === 1 ? firstY : Number(index === 0 ? text.dataset.firstY : text.dataset.secondY)));
                      span.setAttribute('text-anchor', anchor);
                      span.textContent = line;
                      text.appendChild(span);
                    });

                    const monogram = svg.querySelector('#template-monogram');
                    if (monogram) {
                      const significantWords = name.split(' ').filter(word => !['de', 'do', 'da', 'dos', 'das', 'e'].includes(word.toLocaleLowerCase('pt-BR')));
                      const initials = significantWords.slice(0, 2).map(word => Array.from(word)[0] || '');
                      monogram.textContent = (initials.join('') || Array.from(name).slice(0, 2).join('')).toLocaleUpperCase('pt-BR');
                    }
                  })();
                </script>
              </body>
            </html>
            """;
        return html
            .Replace("__SVG_CONTENT__", svg, StringComparison.Ordinal)
            .Replace("__ENCODED_NAME__", encodedName, StringComparison.Ordinal);
    }
}
