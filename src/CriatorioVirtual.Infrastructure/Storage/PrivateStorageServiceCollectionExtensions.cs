using Amazon.Runtime;
using Amazon.S3;
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
            .AddSingleton<IValidateOptions<PrivateStorageOptions>, PrivateStorageOptionsValidator>();

        if (string.Equals(
            configuration[$"{PrivateStorageOptions.SectionName}:Provider"],
            "S3",
            StringComparison.OrdinalIgnoreCase))
        {
            services
                .AddSingleton<IAmazonS3>(provider =>
                {
                    var options = provider.GetRequiredService<IOptions<PrivateStorageOptions>>().Value.S3;
                    var credentials = new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey);
                    var clientOptions = new AmazonS3Config
                    {
                        ServiceURL = options.Endpoint,
                        AuthenticationRegion = options.Region,
                        ForcePathStyle = options.ForcePathStyle
                    };
                    return new AmazonS3Client(credentials, clientOptions);
                })
                .AddSingleton<S3PrivateObjectStorage>()
                .AddSingleton<IPrivateObjectStorage>(provider =>
                    provider.GetRequiredService<S3PrivateObjectStorage>())
                .AddSingleton<IPrivateStorageMigrationTarget>(provider =>
                    provider.GetRequiredService<S3PrivateObjectStorage>());
        }
        else
        {
            services.AddSingleton<IPrivateObjectStorage, FileSystemPrivateObjectStorage>();
        }

        return services;
    }
}
