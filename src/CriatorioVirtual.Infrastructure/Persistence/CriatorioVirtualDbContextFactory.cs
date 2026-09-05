using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CriatorioVirtual.Infrastructure.Persistence;

public sealed class CriatorioVirtualDbContextFactory : IDesignTimeDbContextFactory<CriatorioVirtualDbContext>
{
    public CriatorioVirtualDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CriatorioVirtual")
            ?? throw new InvalidOperationException(
                "Set ConnectionStrings__CriatorioVirtual before using EF Core design-time commands.");

        var options = new DbContextOptionsBuilder<CriatorioVirtualDbContext>()
            .UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", CriatorioVirtualDbContext.DefaultSchema))
            .Options;

        return new CriatorioVirtualDbContext(options);
    }
}
