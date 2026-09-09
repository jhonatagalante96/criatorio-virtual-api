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
                        : "Resend";
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

        services.AddHttpClient<ResendAuthenticationEmailSender>(client =>
        {
            client.BaseAddress = new Uri("https://api.resend.com/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            services.AddSingleton<InMemoryAuthenticationEmailSender>();
            services.AddSingleton<IAuthenticationEmailInbox>(serviceProvider =>
                serviceProvider.GetRequiredService<InMemoryAuthenticationEmailSender>());
            services.AddTransient<IAuthenticationEmailSender>(serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<AuthenticationEmailOptions>>().Value;
                if (string.Equals(options.Provider, "InMemory", StringComparison.OrdinalIgnoreCase))
                {
                    return serviceProvider.GetRequiredService<InMemoryAuthenticationEmailSender>();
                }

                return string.Equals(options.Provider, "Resend", StringComparison.OrdinalIgnoreCase)
                    ? serviceProvider.GetRequiredService<ResendAuthenticationEmailSender>()
                    : new UnavailableAuthenticationEmailSender();
            });
        }
        else
        {
            services.TryAddTransient<IAuthenticationEmailSender>(serviceProvider =>
                serviceProvider.GetRequiredService<ResendAuthenticationEmailSender>());
        }

        return services;
    }
}
