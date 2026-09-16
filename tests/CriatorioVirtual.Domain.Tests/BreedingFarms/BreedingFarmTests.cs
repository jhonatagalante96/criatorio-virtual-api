using CriatorioVirtual.Domain.BreedingFarms;
using Xunit;

namespace CriatorioVirtual.Domain.Tests.BreedingFarms;

public sealed class BreedingFarmTests
{
    [Fact]
    public void Address_NormalizesStateAndPostalCode()
    {
        var address = new BreedingFarmAddress(
            " Rua A ",
            null,
            null,
            null,
            " São Paulo ",
            "sp",
            "12345-678");

        Assert.Equal("SP", address.State);
        Assert.Equal("12345678", address.PostalCode);
        Assert.Equal("Rua A", address.Street);
    }

    [Theory]
    [InlineData("São Paulo", null)]
    [InlineData(null, "1234")]
    [InlineData(null, "12345-67A")]
    public void Address_RejectsInvalidStateOrPostalCode(string? state, string? postalCode)
    {
        Assert.Throws<ArgumentException>(() => new BreedingFarmAddress(
            null,
            null,
            null,
            null,
            null,
            state,
            postalCode));
    }

    [Fact]
    public void UpdateSettings_ChangesSettingsAndTimestamp()
    {
        var createdAt = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        var updatedAt = createdAt.AddMinutes(1);
        var farm = new BreedingFarm(
            Guid.NewGuid(),
            createdAt,
            "Sítio Aurora",
            "Owner Principal",
            "owner@example.com",
            null,
            null,
            new BreedingFarmAddress(null, null, null, null, null, null, null));

        farm.UpdateSettings(
            "Sítio Boreal",
            "Nova Responsável",
            "contact@example.com",
            "+55 11 99999-0000",
            null,
            new BreedingFarmAddress(null, null, null, null, null, "rj", "98765 432"),
            updatedAt);

        Assert.Equal("Sítio Boreal", farm.Name);
        Assert.Equal("Nova Responsável", farm.ResponsibleName);
        Assert.Equal("contact@example.com", farm.ContactEmail);
        Assert.Equal("RJ", farm.Address.State);
        Assert.Equal("98765432", farm.Address.PostalCode);
        Assert.Equal(updatedAt, farm.UpdatedAtUtc);
    }

    [Fact]
    public void VisualIdentity_CanBeReplacedAndRemovedWithoutLosingThePreviousReference()
    {
        var createdAt = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        var farm = new BreedingFarm(
            Guid.NewGuid(),
            createdAt,
            "Sítio Aurora",
            "Owner Principal",
            "owner@example.com",
            null,
            null,
            new BreedingFarmAddress(null, null, null, null, null, null, null));

        Assert.Null(farm.SetVisualIdentity(
            BreedingFarmVisualIdentitySource.Upload,
            "visual-identity/first",
            "logo.png",
            "image/png",
            100,
            createdAt.AddMinutes(1)));

        var previous = farm.SetVisualIdentity(
            BreedingFarmVisualIdentitySource.Upload,
            "visual-identity/second",
            "replacement.jpg",
            "image/jpeg",
            200,
            createdAt.AddMinutes(2));

        Assert.Equal("visual-identity/first", previous!.Reference);
        Assert.Equal(BreedingFarmVisualIdentitySource.Upload, previous.Source);
        Assert.Equal("visual-identity/second", farm.VisualIdentityReference);
        Assert.Equal("replacement.jpg", farm.VisualIdentityFileName);
        Assert.Equal("image/jpeg", farm.VisualIdentityContentType);
        Assert.Equal(200, farm.VisualIdentityLength);
        Assert.Equal(createdAt.AddMinutes(2), farm.UpdatedAtUtc);

        var removed = farm.RemoveVisualIdentity(createdAt.AddMinutes(3));

        Assert.Equal("visual-identity/second", removed!.Reference);
        Assert.Null(farm.GetVisualIdentity());
        Assert.Null(farm.VisualIdentityReference);
        Assert.Null(farm.VisualIdentitySource);
        Assert.Null(farm.VisualIdentityFileName);
        Assert.Null(farm.VisualIdentityContentType);
        Assert.Null(farm.VisualIdentityLength);
        Assert.Null(farm.VisualIdentityTemplateModelId);
        Assert.Null(farm.VisualIdentityTemplateVersion);
        Assert.Null(farm.VisualIdentityTemplateConfiguration);
        Assert.Equal(createdAt.AddMinutes(3), farm.UpdatedAtUtc);
        Assert.Null(farm.RemoveVisualIdentity(createdAt.AddMinutes(4)));
        Assert.Equal(createdAt.AddMinutes(3), farm.UpdatedAtUtc);
    }

    [Fact]
    public void VisualIdentity_TemplateRequiresGeneratedPngAndModelMetadata()
    {
        var farm = new BreedingFarm(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "Sítio Aurora",
            "Owner Principal",
            "owner@example.com",
            null,
            null,
            new BreedingFarmAddress(null, null, null, null, null, null, null));

        farm.SetVisualIdentity(
            BreedingFarmVisualIdentitySource.Template,
            "visual-identity/generated.png",
            "identity-template.png",
            "image/png",
            100,
            DateTimeOffset.UtcNow.AddMinutes(1),
            "classico",
            "1.0.0",
            "{\"name\":\"Sítio Aurora\",\"subtitle\":\"MODELO CLÁSSICO\"}");

        var identity = farm.GetVisualIdentity();
        Assert.Equal("identity-template.png", identity!.FileName);
        Assert.Equal("image/png", identity.ContentType);
        Assert.Equal(100, identity.Length);
        Assert.Equal("classico", identity.TemplateModelId);
        Assert.Equal("1.0.0", identity.TemplateVersion);
        Assert.Equal("{\"name\":\"Sítio Aurora\",\"subtitle\":\"MODELO CLÁSSICO\"}", identity.TemplateConfiguration);

        Assert.Throws<ArgumentException>(() => farm.SetVisualIdentity(
            BreedingFarmVisualIdentitySource.Template,
            "visual-identity/invalid.png",
            "identity-template.png",
            "image/png",
            100,
            DateTimeOffset.UtcNow.AddMinutes(2)));
    }
}
