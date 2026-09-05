using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Persistence;

public sealed class CriatorioVirtualDbContext(DbContextOptions<CriatorioVirtualDbContext> options) : DbContext(options)
{
    public const string DefaultSchema = "app";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DefaultSchema);

        base.OnModelCreating(modelBuilder);
    }
}
