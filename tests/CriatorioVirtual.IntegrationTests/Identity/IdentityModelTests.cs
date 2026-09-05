using CriatorioVirtual.Infrastructure.Identity;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Identity;

public sealed class IdentityModelTests
{
    [Fact]
    public void Model_StoresIdentityAndDataProtectionOutsideTheApplicationSchema()
    {
        var options = new DbContextOptionsBuilder<CriatorioVirtualDbContext>()
            .UseNpgsql("Host=localhost;Database=identity_model;Username=postgres;Password=test")
            .Options;

        using var context = new CriatorioVirtualDbContext(options);

        Assert.Equal("identity", context.Model.FindEntityType(typeof(ApplicationUser))!.GetSchema());
        Assert.Equal("users", context.Model.FindEntityType(typeof(ApplicationUser))!.GetTableName());
        Assert.Equal("identity", context.Model.FindEntityType(typeof(DataProtectionKey))!.GetSchema());
        Assert.Equal("data_protection_keys", context.Model.FindEntityType(typeof(DataProtectionKey))!.GetTableName());
        Assert.DoesNotContain(typeof(ApplicationUser).GetProperties(), property => property.Name.Contains("Tenant", StringComparison.OrdinalIgnoreCase));
        var normalizedEmail = context.Model.FindEntityType(typeof(ApplicationUser))!
            .FindProperty(nameof(ApplicationUser.NormalizedEmail))!;
        var emailIndex = Assert.Single(normalizedEmail.GetContainingIndexes());
        Assert.True(emailIndex.IsUnique);
        Assert.Equal("\"NormalizedEmail\" IS NOT NULL", emailIndex.GetFilter());
    }

    [Fact]
    public void ApplicationUser_UsesGuidIdentityKeys()
    {
        Assert.Equal(typeof(Guid), typeof(ApplicationUser).BaseType!.GetGenericArguments().Single());
        Assert.True(typeof(IdentityUser<Guid>).IsAssignableFrom(typeof(ApplicationUser)));
    }
}
