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
}
