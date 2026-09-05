using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Infrastructure.Messaging;

namespace CriatorioVirtual.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructurePersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<CriatorioVirtualDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    CriatorioVirtualDbContext.DefaultSchema)));

        services.AddScoped<ICommandExecutor, CommandExecutor>();
        services.AddScoped<IQueryExecutor, QueryExecutor>();

        return services;
    }
}
