using System.Text.Json;
using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Infrastructure.Documents;

namespace CriatorioVirtual.Infrastructure.BreedingFarms.CoverTemplates;

public interface IBreedingFarmCoverRenderer
{
    Task<byte[]> RenderTemplatePngAsync(
        BreedingFarmCoverTemplateDefinition template,
        IReadOnlyDictionary<string, object?> configuration,
        string? logoDataUrl,
        CancellationToken cancellationToken = default);

    Task<byte[]> CropUploadToPngAsync(
        ReadOnlyMemory<byte> image,
        string contentType,
        CancellationToken cancellationToken = default);
}

public sealed class BreedingFarmCoverRenderer(IHtmlToPngRenderer htmlRenderer)
    : IBreedingFarmCoverRenderer
{
    public Task<byte[]> RenderTemplatePngAsync(
        BreedingFarmCoverTemplateDefinition template,
        IReadOnlyDictionary<string, object?> configuration,
        string? logoDataUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(configuration);

        var runtimeConfiguration = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in configuration)
        {
            if (!pair.Key.Equals("logoAssetId", StringComparison.Ordinal))
            {
                runtimeConfiguration[pair.Key] = pair.Value;
            }
        }

        runtimeConfiguration["logoUrl"] = logoDataUrl;
        var serialized = JsonSerializer.Serialize(runtimeConfiguration);
        var html = template.EntryHtml;
        var headEnd = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (headEnd < 0)
        {
            throw new InvalidOperationException("The cover template HTML has no head element.");
        }

        html = html.Insert(headEnd,
            $"<script>window.__COVER_REQUIRE_READY__=true;window.__COVER_CONFIG__={serialized};</script>");
        return htmlRenderer.RenderPngAsync(
            html,
            BreedingFarmCoverUploadLimits.CanonicalWidth,
            BreedingFarmCoverUploadLimits.CanonicalHeight,
            cancellationToken);
    }

    public Task<byte[]> CropUploadToPngAsync(
        ReadOnlyMemory<byte> image,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (image.IsEmpty || contentType is not ("image/png" or "image/jpeg" or "image/webp"))
        {
            throw new ArgumentException("The uploaded cover image cannot be rendered.", nameof(image));
        }

        var source = $"data:{contentType};base64,{Convert.ToBase64String(image.Span)}";
        var html = $$"""
            <!doctype html>
            <html><head><meta charset="utf-8"><style>
            html,body{width:1920px;height:640px;margin:0;padding:0;overflow:hidden;background:#fff}
            img{display:block;width:1920px;height:640px;object-fit:cover;object-position:center}
            </style><script>
            window.__COVER_REQUIRE_READY__=true;window.__COVER_READY__=false;
            </script></head><body><img id="cover" alt="" src="{{source}}"><script>
            const image=document.getElementById('cover');
            image.decode().then(()=>{window.__COVER_READY__=true}).catch(()=>{window.__COVER_RENDER_ERROR__=true});
            </script></body></html>
            """;
        return htmlRenderer.RenderPngAsync(
            html,
            BreedingFarmCoverUploadLimits.CanonicalWidth,
            BreedingFarmCoverUploadLimits.CanonicalHeight,
            cancellationToken);
    }
}
