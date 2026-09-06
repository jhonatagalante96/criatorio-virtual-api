using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CriatorioVirtual.Application.Identity;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application;
using CriatorioVirtual.Infrastructure.Messaging;
using CriatorioVirtual.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace CriatorioVirtual.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructurePersistence(
        this IServiceCollection services,
        string connectionString,
        X509Certificate2 dataProtectionCertificate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(dataProtectionCertificate);

        if (!dataProtectionCertificate.HasPrivateKey)
        {
            throw new ArgumentException("The Data Protection certificate must contain a private key.", nameof(dataProtectionCertificate));
        }

        services.AddDbContext<CriatorioVirtualDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    CriatorioVirtualDbContext.DefaultSchema)));

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<CriatorioVirtualDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddDataProtection()
            .PersistKeysToDbContext<CriatorioVirtualDbContext>()
            .ProtectKeysWithCertificate(dataProtectionCertificate)
            .SetApplicationName("CriatorioVirtual");

        services.AddScoped<ICommandExecutor, CommandExecutor>();
        services.AddScoped<IQueryExecutor, QueryExecutor>();
        services.AddScoped<AccountRegistrationService>();
        services.AddScoped<IAccountSessionService, AccountSessionService>();
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
