using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Persistence;

public sealed class PostgreSqlMigrationTests
{
    [Fact]
    public async Task MigrateAsync_AppliesInitialMigrationToAnEmptyPostgreSqlDatabase()
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

        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();

        Assert.Contains(appliedMigrations, migration => migration.EndsWith("_InitializePersistence", StringComparison.Ordinal));
    }
}
