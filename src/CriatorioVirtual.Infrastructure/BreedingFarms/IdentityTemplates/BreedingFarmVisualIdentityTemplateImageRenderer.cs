using System.Text.Json;
using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Infrastructure.Documents;

namespace CriatorioVirtual.Infrastructure.BreedingFarms.IdentityTemplates;

public sealed class BreedingFarmVisualIdentityTemplateImageRenderer(IHtmlToPngRenderer htmlRenderer)
    : IVisualIdentityTemplateImageRenderer
{
    private const int ImageSize = 1024;
    private const string RenderStyles = """
        <style>
          html, body { width: 1024px; height: 1024px; min-height: 0; margin: 0; padding: 0; overflow: hidden; background: #fff; }
          body { display: block; }
          .frame { width: 1024px; height: 1024px; aspect-ratio: 1 / 1; box-shadow: none; }
          .helper { display: none; }
        </style>
        """;

    public async Task<byte[]> RenderPngAsync(
        VisualIdentityTemplateDefinition template,
        IReadOnlyDictionary<string, string> configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!template.DefaultConfiguration.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(configuration.Keys) ||
            !configuration.TryGetValue("name", out var name) ||
            string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("The identity template configuration does not match its declared fields.", nameof(configuration));
        }

        var html = InjectConfiguration(template.PreviewHtml, configuration);
        html = InjectRenderStyles(html);
        return await htmlRenderer.RenderPngAsync(html, ImageSize, ImageSize, cancellationToken);
    }

    private static string InjectConfiguration(
        string html,
        IReadOnlyDictionary<string, string> configuration)
    {
        var scriptIndex = html.IndexOf("<script", StringComparison.OrdinalIgnoreCase);
        if (scriptIndex < 0)
        {
            throw new InvalidOperationException("The identity template HTML has no configuration script.");
        }

        var serialized = JsonSerializer.Serialize(configuration);
        var script = $"<script>window.__CRIATORIO_CONFIG__ = {serialized};</script>";
        return html.Insert(scriptIndex, script);
    }

    private static string InjectRenderStyles(string html)
    {
        var headEndIndex = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (headEndIndex < 0)
        {
            throw new InvalidOperationException("The identity template HTML has no head element.");
        }

        return html.Insert(headEndIndex, RenderStyles);
    }
}
