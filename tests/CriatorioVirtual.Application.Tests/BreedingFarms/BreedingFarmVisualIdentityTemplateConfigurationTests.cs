using System.Text.Json;
using CriatorioVirtual.Application.BreedingFarms;
using Xunit;

namespace CriatorioVirtual.Application.Tests.BreedingFarms;

public sealed class BreedingFarmVisualIdentityTemplateConfigurationTests
{
    private static readonly VisualIdentityTemplateDefinition Template = new(
        "natural",
        "Natural",
        "1.0.0",
        "/preview",
        "<html><script></script></html>",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = "MORAIS",
            ["subtitle"] = "TRADIÇÃO E RESPEITO",
            ["tagline"] = "PÁSSAROS DE QUALIDADE"
        },
        [
            new("tagline", "text", Required: false, "PÁSSAROS DE QUALIDADE", []),
            new("subtitle", "text", Required: false, "TRADIÇÃO E RESPEITO", [])
        ],
        IsActive: true);

    [Fact]
    public void MissingConfigurationUsesAndPersistsTemplateDefaultsAndFarmName()
    {
        var succeeded = BreedingFarmVisualIdentityTemplateConfiguration.TryGetEffectiveConfiguration(
            Template,
            default,
            "Sítio Aurora",
            out var effective,
            out var serialized);

        Assert.True(succeeded);
        Assert.Equal("Sítio Aurora", effective["name"]);
        Assert.Equal("TRADIÇÃO E RESPEITO", effective["subtitle"]);
        Assert.Equal("PÁSSAROS DE QUALIDADE", effective["tagline"]);
        using var persisted = JsonDocument.Parse(serialized);
        Assert.Equal("Sítio Aurora", persisted.RootElement.GetProperty("name").GetString());
        Assert.Equal("TRADIÇÃO E RESPEITO", persisted.RootElement.GetProperty("subtitle").GetString());
        Assert.Equal("PÁSSAROS DE QUALIDADE", persisted.RootElement.GetProperty("tagline").GetString());
        Assert.StartsWith("{\"name\":", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void DeclaredTextOptionsAreAcceptedTrimmedAndCanonicalized()
    {
        using var json = JsonDocument.Parse("""{"subtitle":"  NOVA TRADIÇÃO  ","tagline":"AVES SELETAS"}""");

        var succeeded = BreedingFarmVisualIdentityTemplateConfiguration.TryGetEffectiveConfiguration(
            Template,
            json.RootElement,
            "Sítio Aurora",
            out var effective,
            out var serialized);

        Assert.True(succeeded);
        Assert.Equal("NOVA TRADIÇÃO", effective["subtitle"]);
        Assert.Equal("AVES SELETAS", effective["tagline"]);
        using var persisted = JsonDocument.Parse(serialized);
        Assert.Equal("Sítio Aurora", persisted.RootElement.GetProperty("name").GetString());
        Assert.Equal("NOVA TRADIÇÃO", persisted.RootElement.GetProperty("subtitle").GetString());
        Assert.Equal("AVES SELETAS", persisted.RootElement.GetProperty("tagline").GetString());
        Assert.StartsWith("{\"name\":", serialized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"prompt\":\"arbitrary\"}")]
    [InlineData("{\"subtitle\":\"x\",\"subtitle\":\"y\"}")]
    [InlineData("{\"subtitle\":42}")]
    [InlineData("{\"subtitle\":\"  \"}")]
    [InlineData("{\"name\":\"Another farm\"}")]
    [InlineData("null")]
    [InlineData("[]")]
    public void UnsupportedConfigurationIsRejected(string value)
    {
        using var json = JsonDocument.Parse(value);

        Assert.False(BreedingFarmVisualIdentityTemplateConfiguration.TryGetEffectiveConfiguration(
            Template,
            json.RootElement,
            "Sítio Aurora",
            out _,
            out _));
    }

    [Fact]
    public void TextOptionsHaveASupportedLengthLimit()
    {
        using var json = JsonDocument.Parse($"{{\"subtitle\":\"{new string('x', 121)}\"}}");

        Assert.False(BreedingFarmVisualIdentityTemplateConfiguration.TryGetEffectiveConfiguration(
            Template,
            json.RootElement,
            "Sítio Aurora",
            out _,
            out _));
    }
}
