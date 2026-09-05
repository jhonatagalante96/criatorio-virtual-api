using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;

namespace CriatorioVirtual.Api;

public static class HttpSecurityServiceCollectionExtensions
{
    public const string AntiforgeryHeaderName = "X-XSRF-TOKEN";

    public static IServiceCollection AddHttpSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var allowedOrigins = GetAllowedOrigins(configuration);
        var trustedProxies = GetTrustedProxies(configuration);

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();
        services.AddAuthorization();
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "__Host-CriatorioVirtual-Auth";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.SlidingExpiration = true;
        });
        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryHeaderName;
            options.Cookie.Name = "__Host-CriatorioVirtual-Antiforgery";
            options.Cookie.Path = "/";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
        });
        services.AddCors(options => options.AddPolicy("trusted-client", policy => policy
            .WithOrigins(allowedOrigins)
            .AllowCredentials()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders(AntiforgeryHeaderName)));
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();

            options.ForwardedHeaders = trustedProxies.Length == 0
                ? ForwardedHeaders.None
                : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            foreach (var trustedProxy in trustedProxies)
            {
                options.KnownProxies.Add(trustedProxy);
            }
        });

        return services;
    }

    private static string[] GetAllowedOrigins(IConfiguration configuration)
    {
        var origins = configuration.GetSection("Security:AllowedOrigins").Get<string[]>() ?? ["http://localhost:3000"];
        if (origins.Length == 0 || origins.Any(string.IsNullOrWhiteSpace) || origins.Any(origin => origin.Contains('*')))
        {
            throw new InvalidOperationException("Security:AllowedOrigins must contain explicit, non-wildcard origins.");
        }

        return origins;
    }

    private static IPAddress[] GetTrustedProxies(IConfiguration configuration)
    {
        var configuredProxies = configuration.GetSection("Security:TrustedProxyAddresses").Get<string[]>() ?? [];
        return configuredProxies.Select(proxy =>
                IPAddress.TryParse(proxy, out var address)
                    ? address
                    : throw new InvalidOperationException("Security:TrustedProxyAddresses must contain IP addresses."))
            .ToArray();
    }
}
