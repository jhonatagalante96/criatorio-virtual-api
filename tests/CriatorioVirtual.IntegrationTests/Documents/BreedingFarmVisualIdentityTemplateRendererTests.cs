using System.Buffers.Binary;
using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Infrastructure.BreedingFarms.IdentityTemplates;
using CriatorioVirtual.Infrastructure.Documents;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Documents;

public sealed class BreedingFarmVisualIdentityTemplateRendererTests
{
    [Fact]
    public async Task RendererProducesDeterministic1024PixelPngForEveryDeclaredVariant()
    {
        using var chromium = new ChromiumHtmlToPdfRenderer();
        var catalog = new BreedingFarmVisualIdentityTemplateCatalog();
        var renderer = new BreedingFarmVisualIdentityTemplateImageRenderer(chromium);

        foreach (var template in catalog.GetAll())
        {
            foreach (var variant in template.Variants)
            {
                var first = await renderer.RenderPngAsync(template, "Sítio Aurora", variant);
                var repeated = await renderer.RenderPngAsync(template, "Sítio Aurora", variant);

                Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, first.Take(8));
                Assert.Equal(1024U, BinaryPrimitives.ReadUInt32BigEndian(first.AsSpan(16, 4)));
                Assert.Equal(1024U, BinaryPrimitives.ReadUInt32BigEndian(first.AsSpan(20, 4)));
                Assert.Equal(first, repeated);
            }
        }
    }

    [Fact]
    public async Task RendererHandlesLongNamesWithoutNondeterminism()
    {
        using var chromium = new ChromiumHtmlToPdfRenderer();
        var template = new BreedingFarmVisualIdentityTemplateCatalog().GetAll()
            .Single(candidate => candidate.Id == "noturno-minimalista");
        var renderer = new BreedingFarmVisualIdentityTemplateImageRenderer(chromium);
        const string name = "Criatório Aurora de Serra Azul e Vale Verde para Criação Especial";

        var first = await renderer.RenderPngAsync(template, name, "forest-night");
        var repeated = await renderer.RenderPngAsync(template, name, "forest-night");

        Assert.Equal(first, repeated);
        Assert.Equal(1024U, BinaryPrimitives.ReadUInt32BigEndian(first.AsSpan(16, 4)));
        Assert.Equal(1024U, BinaryPrimitives.ReadUInt32BigEndian(first.AsSpan(20, 4)));
    }
}
