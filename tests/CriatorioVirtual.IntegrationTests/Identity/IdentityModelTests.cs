using CriatorioVirtual.Infrastructure.Identity;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

    [Fact]
    public void Persistence_ConfiguresStrongPasswordsUniqueEmailsAndLockout()
    {
        using var certificate = TestCertificate.Create();
        var services = new ServiceCollection();
        services.AddInfrastructurePersistence("Host=localhost;Database=identity_options;Username=postgres;Password=test", certificate);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<IdentityOptions>>().Value;

        Assert.True(options.User.RequireUniqueEmail);
        Assert.True(options.SignIn.RequireConfirmedEmail);
        Assert.True(options.Lockout.AllowedForNewUsers);
        Assert.Equal(5, options.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), options.Lockout.DefaultLockoutTimeSpan);
        Assert.Equal(12, options.Password.RequiredLength);
        Assert.True(options.Password.RequireDigit);
        Assert.True(options.Password.RequireLowercase);
        Assert.True(options.Password.RequireUppercase);
        Assert.True(options.Password.RequireNonAlphanumeric);
    }
}
