using CriatorioVirtual.Infrastructure.Identity;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
        Assert.Contains(appliedMigrations, migration => migration.EndsWith("_AddSubscriptionPurchaseDetails", StringComparison.Ordinal));

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

    [Fact]
    public async Task MigrateAsync_AppliesParticipantSnapshotsMigrationWithBackfill_ToExistingReproductions()
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

        // 1. Migrate up to the migration immediately preceding participant snapshots
        await using (var initialContext = new CriatorioVirtualDbContext(options))
        {
            var migrator = initialContext.Database.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
            await migrator.MigrateAsync("20260917032641_PreserveBirdPrimaryPhotoScope");
        }

        // 2. Insert breeding farm, species, male bird, female bird, and reproductions without snapshot columns
        var farmId = Guid.NewGuid();
        var maleBirdId = Guid.NewGuid();
        var femaleBirdId = Guid.NewGuid();
        var reproductionId = Guid.NewGuid();

        var mutatedMaleBirdId = Guid.NewGuid();
        var mutatedFemaleBirdId = Guid.NewGuid();
        var mutatedReproductionId = Guid.NewGuid();

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        await using (var seedContext = new CriatorioVirtualDbContext(options))
        {
            await seedContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO app.breeding_farms ("Id", "Name", "ResponsibleName", "ContactEmail", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES ({farmId}, 'Migration Farm', 'Migration Owner', 'owner@example.com', {now}, {now});

                INSERT INTO app.birds ("Id", "BreedingFarmId", "Name", "SpeciesId", "Sex", "RingNumber", "Status", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES
                    ({maleBirdId}, {farmId}, 'Macho Preexistente', '00000000-0000-0000-0000-000000000001', 1, '123456', 1, {now}, {now}),
                    ({femaleBirdId}, {farmId}, 'Fêmea Preexistente', '00000000-0000-0000-0000-000000000001', 2, '654321', 1, {now}, {now}),
                    ({mutatedMaleBirdId}, {farmId}, 'Macho Sexo Alterado', '00000000-0000-0000-0000-000000000001', 3, '777888', 1, {now}, {now}),
                    ({mutatedFemaleBirdId}, {farmId}, 'Fêmea Sexo Alterado', '00000000-0000-0000-0000-000000000001', 3, '888999', 1, {now}, {now});

                INSERT INTO app.reproductions ("Id", "BreedingFarmId", "MaleBirdId", "FemaleBirdId", "StartDate", "Status", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES
                    ({reproductionId}, {farmId}, {maleBirdId}, {femaleBirdId}, {today}, 1, {now}, {now}),
                    ({mutatedReproductionId}, {farmId}, {mutatedMaleBirdId}, {mutatedFemaleBirdId}, {today}, 1, {now}, {now});
                """);
        }

        // 3. Now apply the latest migrations including 20260919004642_AddReproductionParticipantHistoricalSnapshots
        await using (var targetContext = new CriatorioVirtualDbContext(options))
        {
            await targetContext.Database.MigrateAsync();
        }

        // 4. Verify that both reproductions were backfilled with male and female snapshots and check constraints are met
        await using (var verifyContext = new CriatorioVirtualDbContext(options))
        {
            var reproduction = await verifyContext.Reproductions.AsNoTracking().SingleAsync(r => r.Id == reproductionId);
            Assert.Equal("Macho Preexistente", reproduction.MaleBirdName);
            Assert.Equal(CriatorioVirtual.Domain.Birds.BirdSex.Male, reproduction.MaleBirdSex);
            Assert.Equal("123456", reproduction.MaleBirdRingNumber);
            Assert.Equal(CriatorioVirtual.Domain.Birds.BirdStatus.Active, reproduction.MaleBirdStatus);

            Assert.Equal("Fêmea Preexistente", reproduction.FemaleBirdName);
            Assert.Equal(CriatorioVirtual.Domain.Birds.BirdSex.Female, reproduction.FemaleBirdSex);
            Assert.Equal("654321", reproduction.FemaleBirdRingNumber);
            Assert.Equal(CriatorioVirtual.Domain.Birds.BirdStatus.Active, reproduction.FemaleBirdStatus);

            var mutatedReproduction = await verifyContext.Reproductions.AsNoTracking().SingleAsync(r => r.Id == mutatedReproductionId);
            Assert.Equal("Macho Sexo Alterado", mutatedReproduction.MaleBirdName);
            Assert.Equal(CriatorioVirtual.Domain.Birds.BirdSex.Male, mutatedReproduction.MaleBirdSex);
            Assert.Equal("777888", mutatedReproduction.MaleBirdRingNumber);
            Assert.Equal(CriatorioVirtual.Domain.Birds.BirdStatus.Active, mutatedReproduction.MaleBirdStatus);

            Assert.Equal("Fêmea Sexo Alterado", mutatedReproduction.FemaleBirdName);
            Assert.Equal(CriatorioVirtual.Domain.Birds.BirdSex.Female, mutatedReproduction.FemaleBirdSex);
            Assert.Equal("888999", mutatedReproduction.FemaleBirdRingNumber);
            Assert.Equal(CriatorioVirtual.Domain.Birds.BirdStatus.Active, mutatedReproduction.FemaleBirdStatus);
        }
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
