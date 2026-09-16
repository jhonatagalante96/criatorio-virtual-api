using System.Text.Json;
using CriatorioVirtual.Application.BreedingFarms;
using Xunit;

namespace CriatorioVirtual.Application.Tests.BreedingFarms;

public sealed class BreedingFarmVisualIdentityTemplateConfigurationTests
{
    private static readonly VisualIdentityTemplateDefinition Template = new(
        "folhagem-classica",
        "Folhagem Clássica",
        "1.0.0",
        "/preview.svg",
        ["brand", "forest"],
        "brand",
        "<svg />",
        IsActive: true);

    [Fact]
    public void MissingConfigurationUsesAndPersistsTheDeclaredDefault()
    {
        var succeeded = BreedingFarmVisualIdentityTemplateConfiguration.TryGetEffectiveVariant(
            Template,
            default,
            out var variant,
            out var configuration);

        Assert.True(succeeded);
        Assert.Equal("brand", variant);
        Assert.Equal("{\"variant\":\"brand\"}", configuration);
    }

    [Fact]
    public void DeclaredVariantIsAcceptedAndConfigurationIsCanonicalized()
    {
        using var json = JsonDocument.Parse("{\"variant\":\"forest\"}");

        var succeeded = BreedingFarmVisualIdentityTemplateConfiguration.TryGetEffectiveVariant(
            Template,
            json.RootElement,
            out var variant,
            out var configuration);

        Assert.True(succeeded);
        Assert.Equal("forest", variant);
        Assert.Equal("{\"variant\":\"forest\"}", configuration);
    }

    [Theory]
    [InlineData("{\"variant\":\"unknown\"}")]
    [InlineData("{\"variant\":\"brand\",\"prompt\":\"arbitrary\"}")]
    [InlineData("{\"variant\":\"brand\",\"variant\":\"forest\"}")]
    [InlineData("null")]
    [InlineData("{\"variant\":42}")]
    public void UnsupportedConfigurationIsRejected(string value)
    {
        using var json = JsonDocument.Parse(value);

        Assert.False(BreedingFarmVisualIdentityTemplateConfiguration.TryGetEffectiveVariant(
            Template,
            json.RootElement,
            out _,
            out _));
    }
}
