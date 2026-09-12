using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Dashboard;
using CriatorioVirtual.Application.Identity;
using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Reproductions;
using CriatorioVirtual.Application.Species;
using CriatorioVirtual.Application.Transfers;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application;
using CriatorioVirtual.Infrastructure.Messaging;
using CriatorioVirtual.Infrastructure.Identity;
using CriatorioVirtual.Infrastructure.BreedingFarms;
using CriatorioVirtual.Infrastructure.Birds;
using CriatorioVirtual.Infrastructure.Dashboard;
using CriatorioVirtual.Infrastructure.Reproductions;
using CriatorioVirtual.Infrastructure.Species;
using CriatorioVirtual.Infrastructure.Transfers;
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
        services.AddScoped<IAccountEmailConfirmationService, AccountEmailConfirmationService>();
        services.AddScoped<IAccountPasswordService, AccountPasswordService>();
        services.AddScoped<IAccountSessionService, AccountSessionService>();
        services.AddScoped<IGoogleAccountAuthenticationService, GoogleAccountAuthenticationService>();
        services.AddScoped<ICommandHandler<CreateBreedingFarmCommand, CreateBreedingFarmResult>, CreateBreedingFarmCommandHandler>();
        services.AddScoped<ICommandHandler<SelectBreedingFarmCommand, SelectBreedingFarmResult>, SelectBreedingFarmCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateBreedingFarmSettingsCommand, UpdateBreedingFarmSettingsResult>, UpdateBreedingFarmSettingsCommandHandler>();
        services.AddScoped<ICommandHandler<CreateBirdCommand, CreateBirdResult>, CreateBirdCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateBirdCommand, UpdateBirdResult>, UpdateBirdCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateBirdGenealogyCommand, UpdateBirdGenealogyResult>, UpdateBirdGenealogyCommandHandler>();
        services.AddScoped<ICommandHandler<ChangeBirdStatusCommand, ChangeBirdStatusResult>, ChangeBirdStatusCommandHandler>();
        services.AddScoped<ICommandHandler<CreateReproductionCommand, CreateReproductionResult>, CreateReproductionCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateReproductionCommand, UpdateReproductionResult>, UpdateReproductionCommandHandler>();
        services.AddScoped<ICommandHandler<ChangeReproductionStatusCommand, ChangeReproductionStatusResult>, ChangeReproductionStatusCommandHandler>();
        services.AddScoped<ICommandHandler<LinkReproductionOriginCommand, LinkReproductionOriginResult>, LinkReproductionOriginCommandHandler>();
        services.AddScoped<IQueryHandler<ListReproductionsQuery, ListReproductionsResult>, ListReproductionsQueryHandler>();
        services.AddScoped<IQueryHandler<GetReproductionQuery, GetReproductionResult>, GetReproductionQueryHandler>();
        services.AddScoped<IQueryHandler<GetBirdQuery, GetBirdResult>, GetBirdQueryHandler>();
        services.AddScoped<IQueryHandler<GetDashboardQuery, GetDashboardResult>, GetDashboardQueryHandler>();
        services.AddScoped<IQueryHandler<GetBirdGenealogyQuery, GetBirdGenealogyResult>, GetBirdGenealogyQueryHandler>();
        services.AddScoped<IQueryHandler<GetBirdEligibilityQuery, GetBirdEligibilityResult>, GetBirdEligibilityQueryHandler>();
        services.AddScoped<IQueryHandler<ListBirdsQuery, ListBirdsResult>, ListBirdsQueryHandler>();
        services.AddScoped<IQueryHandler<SearchBirdParentOptionsQuery, SearchBirdParentOptionsResult>, SearchBirdParentOptionsQueryHandler>();
        services.AddScoped<ICommandHandler<RequestInternalTransferCommand, RequestInternalTransferResult>, RequestInternalTransferCommandHandler>();
        services.AddScoped<ICommandHandler<AcceptInternalTransferCommand, AcceptInternalTransferResult>, AcceptInternalTransferCommandHandler>();
        services.AddScoped<ICommandHandler<RejectInternalTransferCommand, RejectInternalTransferResult>, RejectInternalTransferCommandHandler>();
        services.AddScoped<ICommandHandler<CancelInternalTransferCommand, CancelInternalTransferResult>, CancelInternalTransferCommandHandler>();
        services.AddScoped<IQueryHandler<ListInternalTransferRequestsQuery, ListInternalTransferRequestsResult>, ListInternalTransferRequestsQueryHandler>();
        services.AddScoped<IQueryHandler<GetInternalTransferRequestQuery, GetInternalTransferRequestResult>, GetInternalTransferRequestQueryHandler>();
        services.AddScoped<IQueryHandler<SearchInternalTransferDestinationsQuery, SearchInternalTransferDestinationsResult>, SearchInternalTransferDestinationsQueryHandler>();
        services.AddScoped<IQueryHandler<ListBreedingFarmsQuery, BreedingFarmSelectionResult>, ListBreedingFarmsQueryHandler>();
        services.AddScoped<IQueryHandler<GetBreedingFarmSettingsQuery, BreedingFarmSettingsResult?>, GetBreedingFarmSettingsQueryHandler>();
        services.AddScoped<IQueryHandler<SearchSpeciesQuery, IReadOnlyCollection<SpeciesSearchResult>>, SearchSpeciesQueryHandler>();
        services.AddMemoryCache(options => options.SizeLimit = 10_000);
        services.AddSingleton<IAuthenticationEmailConfirmationThrottle, AuthenticationEmailConfirmationThrottle>();
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
