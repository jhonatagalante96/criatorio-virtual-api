using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CriatorioVirtual.Infrastructure.Storage;

public static class SpeciesDefaultImageServiceCollectionExtensions
{
    public static IServiceCollection AddSpeciesDefaultImageStorage(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services
            .AddOptions<SpeciesDefaultImageStorageOptions>()
            .Bind(configuration.GetSection(SpeciesDefaultImageStorageOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.SpeciesDefaultImagesRootPath) &&
                    (environment.IsDevelopment() || environment.IsEnvironment("Testing")))
                {
                    options.SpeciesDefaultImagesRootPath = Path.Combine(
                        Path.GetTempPath(),
                        "CriatorioVirtual",
                        "species-default-images");
                }
            })
            .ValidateOnStart()
            .Services
            .AddSingleton<IValidateOptions<SpeciesDefaultImageStorageOptions>, SpeciesDefaultImageStorageOptionsValidator>()
            .AddHostedService<SpeciesDefaultImageProvisioner>();

        return services;
    }
}
