using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CriatorioVirtual");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings__CriatorioVirtual is required to apply database migrations.");
}

var options = new DbContextOptionsBuilder<CriatorioVirtualDbContext>()
    .UseNpgsql(
        connectionString,
        npgsqlOptions => npgsqlOptions.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            CriatorioVirtualDbContext.DefaultSchema))
    .Options;

await using var dbContext = new CriatorioVirtualDbContext(options);

Console.WriteLine("Applying database migrations...");
await dbContext.Database.MigrateAsync();
Console.WriteLine("Database migrations applied successfully.");
