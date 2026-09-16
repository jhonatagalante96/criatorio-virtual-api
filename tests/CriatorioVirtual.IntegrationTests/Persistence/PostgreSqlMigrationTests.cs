using CriatorioVirtual.Infrastructure.Identity;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Persistence;

public sealed class PostgreSqlMigrationTests
{
    [Fact]
    public async Task MigrateAsync_AppliesMigrationsAndEnforcesIdentitySecurityInPostgreSql()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();

        var options = new DbContextOptionsBuilder<CriatorioVirtualDbContext>()
            .UseNpgsql(
                database.GetConnectionString(),
                npgsqlOptions => npgsqlOptions.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    CriatorioVirtualDbContext.DefaultSchema))
            .Options;

        await using var context = new CriatorioVirtualDbContext(options);
        await context.Database.MigrateAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());

        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();

        Assert.Contains(appliedMigrations, migration => migration.EndsWith("_InitializePersistence", StringComparison.Ordinal));
        Assert.Contains(appliedMigrations, migration => migration.EndsWith("_AddIdentityAndDataProtection", StringComparison.Ordinal));
        Assert.Contains(appliedMigrations, migration => migration.EndsWith("_EnablePasskeyStore", StringComparison.Ordinal));
        Assert.Contains(appliedMigrations, migration => migration.EndsWith("_DefineDocumentContracts", StringComparison.Ordinal));
        Assert.Contains(appliedMigrations, migration => migration.EndsWith("_ReplaceInternalRecordWithGenealogyCertificate", StringComparison.Ordinal));
        Assert.Contains(appliedMigrations, migration => migration.EndsWith("_AddProvenanceDocument", StringComparison.Ordinal));
        Assert.Contains(appliedMigrations, migration => migration.EndsWith("_AddGenealogyCertificateModels", StringComparison.Ordinal));
        Assert.Contains(appliedMigrations, migration => migration.EndsWith("_AddSubscriptionAndPaymentModel", StringComparison.Ordinal));

        await using var firstUserContext = new CriatorioVirtualDbContext(options);
        await using var secondUserContext = new CriatorioVirtualDbContext(options);
        firstUserContext.Users.Add(new ApplicationUser
        {
            UserName = "first-user",
            NormalizedUserName = "FIRST-USER",
            Email = "user@example.com",
            NormalizedEmail = "USER@EXAMPLE.COM"
        });
        secondUserContext.Users.Add(new ApplicationUser
        {
            UserName = "second-user",
            NormalizedUserName = "SECOND-USER",
            Email = "user@example.com",
            NormalizedEmail = "USER@EXAMPLE.COM"
        });

        var concurrentResults = await Task.WhenAll(
            TrySaveUser(firstUserContext),
            TrySaveUser(secondUserContext));

        Assert.Equal(1, concurrentResults.Count(wasSaved => wasSaved));
        Assert.Equal(1, concurrentResults.Count(wasSaved => !wasSaved));

        using var certificate = TestCertificate.Create();
        var firstServices = CreatePersistenceServices(database.GetConnectionString(), certificate);
        await using var firstProvider = firstServices.BuildServiceProvider();
        var firstProtector = firstProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("session-survival-test");
        var protectedSession = firstProtector.Protect("session-payload");

        var secondServices = CreatePersistenceServices(database.GetConnectionString(), certificate);
        await using var secondProvider = secondServices.BuildServiceProvider();
        var secondProtector = secondProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("session-survival-test");

        Assert.Equal("session-payload", secondProtector.Unprotect(protectedSession));

        await using var keyContext = new CriatorioVirtualDbContext(options);
        var persistedKeys = await keyContext.DataProtectionKeys.AsNoTracking().ToListAsync();
        Assert.NotEmpty(persistedKeys);
        Assert.All(persistedKeys, key =>
        {
            Assert.Contains("EncryptedData", key.Xml, StringComparison.Ordinal);
            Assert.DoesNotContain("masterKey", key.Xml, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static ServiceCollection CreatePersistenceServices(
        string connectionString,
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructurePersistence(connectionString, certificate);
        return services;
    }

    private static async Task<bool> TrySaveUser(CriatorioVirtualDbContext context)
    {
        try
        {
            await context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }
}
