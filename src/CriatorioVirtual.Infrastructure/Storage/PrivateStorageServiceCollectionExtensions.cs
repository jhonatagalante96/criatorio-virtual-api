using CriatorioVirtual.Application.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CriatorioVirtual.Infrastructure.Storage;

public static class PrivateStorageServiceCollectionExtensions
{
    public static IServiceCollection AddPrivateStorage(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services
            .AddOptions<PrivateStorageOptions>()
            .Bind(configuration.GetSection(PrivateStorageOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.PrivateRootPath) &&
                    (environment.IsDevelopment() || environment.IsEnvironment("Testing")))
                {
                    options.PrivateRootPath = Path.Combine(
                        Path.GetTempPath(),
                        "CriatorioVirtual",
                        "private-storage");
                }
            })
            .ValidateOnStart()
            .Services
            .AddSingleton<IValidateOptions<PrivateStorageOptions>, PrivateStorageOptionsValidator>()
            .AddSingleton<IPrivateObjectStorage, FileSystemPrivateObjectStorage>();

        return services;
    }
}
