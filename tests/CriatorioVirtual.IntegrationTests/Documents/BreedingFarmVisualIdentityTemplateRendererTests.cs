using System.Buffers.Binary;
using CriatorioVirtual.Infrastructure.BreedingFarms.IdentityTemplates;
using CriatorioVirtual.Infrastructure.Documents;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Documents;

public sealed class BreedingFarmVisualIdentityTemplateRendererTests
{
    [Fact]
    public void CatalogPublishesTheUpdatedVersionForEveryTemplate()
    {
        var catalog = new BreedingFarmVisualIdentityTemplateCatalog();
        var templates = catalog.GetAll();

        Assert.Equal(4, templates.Count);
        Assert.All(templates, template =>
        {
            Assert.Equal("1.2.0", template.Version);
            Assert.Equal($"/api/breeding-farms/visual-identity/templates/{template.Id}/1.2.0/preview", template.PreviewUrl);
            Assert.Equal(new[] { "name" }, template.DefaultConfiguration.Keys);
            Assert.Empty(template.Options);
        });
    }

    [Fact]
    public async Task RendererProducesDeterministic1024PixelPngForEveryHtmlModel()
    {
        using var chromium = new ChromiumHtmlToPdfRenderer();
        var catalog = new BreedingFarmVisualIdentityTemplateCatalog();
        var renderer = new BreedingFarmVisualIdentityTemplateImageRenderer(chromium);

        foreach (var template in catalog.GetAll())
        {
            var first = await renderer.RenderPngAsync(template, template.DefaultConfiguration);
            var repeated = await renderer.RenderPngAsync(template, template.DefaultConfiguration);

            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, first.Take(8));
            Assert.Equal(1024U, BinaryPrimitives.ReadUInt32BigEndian(first.AsSpan(16, 4)));
            Assert.Equal(1024U, BinaryPrimitives.ReadUInt32BigEndian(first.AsSpan(20, 4)));
            Assert.Equal(first, repeated);
        }
    }

    [Fact]
    public async Task RendererUsesFarmNameOnly()
    {
        using var chromium = new ChromiumHtmlToPdfRenderer();
        var template = new BreedingFarmVisualIdentityTemplateCatalog().GetAll()
            .Single(candidate => candidate.Id == "natural");
        var renderer = new BreedingFarmVisualIdentityTemplateImageRenderer(chromium);
        var configuration = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in template.DefaultConfiguration)
        {
            configuration.Add(key, value);
        }

        configuration["name"] = "Criatório Aurora de Serra Azul e Vale Verde para Criação Especial";

        var first = await renderer.RenderPngAsync(template, configuration);
        var repeated = await renderer.RenderPngAsync(template, configuration);
        var defaults = await renderer.RenderPngAsync(template, template.DefaultConfiguration);

        Assert.Equal(first, repeated);
        Assert.NotEqual(defaults, first);
        Assert.Equal(1024U, BinaryPrimitives.ReadUInt32BigEndian(first.AsSpan(16, 4)));
        Assert.Equal(1024U, BinaryPrimitives.ReadUInt32BigEndian(first.AsSpan(20, 4)));
    }
}
