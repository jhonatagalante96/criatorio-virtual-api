using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CriatorioVirtual.Infrastructure.Persistence;

public sealed class CriatorioVirtualDbContextFactory : IDesignTimeDbContextFactory<CriatorioVirtualDbContext>
{
    public CriatorioVirtualDbContext CreateDbContext(string[] args)
    {
        const string connectionString = "Host=localhost;Port=5432;Database=criatorio_virtual;Username=postgres";

        var options = new DbContextOptionsBuilder<CriatorioVirtualDbContext>()
            .UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", CriatorioVirtualDbContext.DefaultSchema))
            .Options;

        return new CriatorioVirtualDbContext(options);
    }
}
