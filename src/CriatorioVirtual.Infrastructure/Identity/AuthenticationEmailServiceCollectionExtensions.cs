using CriatorioVirtual.Application.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CriatorioVirtual.Infrastructure.Identity;

public static class AuthenticationEmailServiceCollectionExtensions
{
    public static IServiceCollection AddAuthenticationEmailDelivery(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services
            .AddOptions<AuthenticationEmailOptions>()
            .Bind(configuration.GetSection(AuthenticationEmailOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.Provider))
                {
                    options.Provider = environment.IsDevelopment() || environment.IsEnvironment("Testing")
                        ? "InMemory"
                        : "Unavailable";
                }

                if (string.IsNullOrWhiteSpace(options.ClientBaseUrl) &&
                    (environment.IsDevelopment() || environment.IsEnvironment("Testing")))
                {
                    options.ClientBaseUrl = "http://localhost:3000";
                }
            })
            .ValidateOnStart()
            .Services
            .AddSingleton<IValidateOptions<AuthenticationEmailOptions>, AuthenticationEmailOptionsValidator>()
            .AddSingleton<IAuthenticationEmailLinkBuilder, AuthenticationEmailLinkBuilder>();

        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            services.AddSingleton<InMemoryAuthenticationEmailSender>();
            services.AddSingleton<IAuthenticationEmailInbox>(serviceProvider =>
                serviceProvider.GetRequiredService<InMemoryAuthenticationEmailSender>());
            services.AddSingleton<IAuthenticationEmailSender>(serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<AuthenticationEmailOptions>>().Value;
                if (string.Equals(options.Provider, "InMemory", StringComparison.OrdinalIgnoreCase))
                {
                    return serviceProvider.GetRequiredService<InMemoryAuthenticationEmailSender>();
                }

                return string.Equals(options.Provider, "Smtp", StringComparison.OrdinalIgnoreCase)
                    ? new SmtpAuthenticationEmailSender(
                        serviceProvider.GetRequiredService<IOptions<AuthenticationEmailOptions>>())
                    : new UnavailableAuthenticationEmailSender();
            });
        }
        else
        {
            services.TryAddSingleton<IAuthenticationEmailSender>(serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<AuthenticationEmailOptions>>();
                return new SmtpAuthenticationEmailSender(options);
            });
        }

        return services;
    }
}
