using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application;
using CriatorioVirtual.Infrastructure.Messaging;
using System.Reflection;

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
        services.AddMessagingHandlers(typeof(ApplicationAssemblyMarker).Assembly);

        return services;
    }
}

internal static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddMessagingHandlers(this IServiceCollection services, Assembly assembly)
    {
        foreach (var implementation in assembly.DefinedTypes.Where(type => type is { IsAbstract: false, IsInterface: false }))
        {
            foreach (var contract in implementation.ImplementedInterfaces.Where(IsMessagingContract))
            {
                services.AddScoped(contract, implementation);
            }
        }

        return services;
    }

    private static bool IsMessagingContract(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() is var definition &&
        (definition == typeof(ICommandHandler<,>) || definition == typeof(ICommandPreProcessor<>) || definition == typeof(IQueryHandler<,>));
}
