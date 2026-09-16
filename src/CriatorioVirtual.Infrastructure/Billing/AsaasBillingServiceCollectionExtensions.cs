using CriatorioVirtual.Application.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using CriatorioVirtual.Infrastructure.Persistence;

namespace CriatorioVirtual.Infrastructure.Billing;

public static class AsaasBillingServiceCollectionExtensions
{
    public static IServiceCollection AddAsaasBillingGateway(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services
            .AddOptions<AsaasOptions>()
            .Bind(configuration.GetSection(AsaasOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.BaseUrl))
                {
                    var isSandboxEnvironment = environment.IsDevelopment() ||
                                               environment.IsEnvironment("Testing") ||
                                               environment.IsEnvironment(AsaasOptions.HomologationEnvironmentName);
                    options.BaseUrl = isSandboxEnvironment
                        ? AsaasOptions.SandboxBaseUrl
                        : AsaasOptions.ProductionBaseUrl;
                }
                else
                {
                    options.BaseUrl = $"{options.BaseUrl.Trim().TrimEnd('/')}/";
                }
            })
            .ValidateOnStart()
            .Services
            .AddSingleton<IValidateOptions<AsaasOptions>, AsaasOptionsValidator>()
            .AddSingleton<AsaasOperationCoordinator>()
            .AddScoped<IAsaasOperationCoordinator>(serviceProvider =>
            {
                var dbContext = serviceProvider.GetService<CriatorioVirtualDbContext>();
                return dbContext is null
                    ? serviceProvider.GetRequiredService<AsaasOperationCoordinator>()
                    : new PostgreSqlAsaasOperationCoordinator(dbContext);
            })
            .AddHttpClient<AsaasBillingGateway>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<AsaasOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(60);
            });

        services.AddTransient<IBillingGateway>(serviceProvider =>
            serviceProvider.GetRequiredService<AsaasBillingGateway>());

        return services;
    }
}
