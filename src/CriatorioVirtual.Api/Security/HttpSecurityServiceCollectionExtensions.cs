using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
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

        var authentication = services.AddAuthentication(IdentityConstants.ApplicationScheme);
        authentication.AddIdentityCookies();
        ConfigureGoogleAuthentication(authentication, configuration, allowedOrigins, services);
        services.AddAuthorization();
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "__Host-CriatorioVirtual-Auth";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.None;
            options.SlidingExpiration = true;
            options.Events.OnRedirectToLogin = context => HandleApiRedirectAsync(context, StatusCodes.Status401Unauthorized);
            options.Events.OnRedirectToAccessDenied = context => HandleApiRedirectAsync(context, StatusCodes.Status403Forbidden);
        });
        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryHeaderName;
            options.Cookie.Name = "__Host-CriatorioVirtual-Antiforgery";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.None;
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

            options.ForwardLimit = 1;

            foreach (var trustedProxy in trustedProxies)
            {
                options.KnownProxies.Add(trustedProxy);
            }
        });

        return services;
    }

    private static void ConfigureGoogleAuthentication(
        AuthenticationBuilder authentication,
        IConfiguration configuration,
        IReadOnlyCollection<string> allowedOrigins,
        IServiceCollection services)
    {
        var google = configuration.GetSection("Security:Google");
        var clientId = google["ClientId"];
        var clientSecret = google["ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) && string.IsNullOrWhiteSpace(clientSecret))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "Security:Google:ClientId and Security:Google:ClientSecret must be configured together.");
        }

        var redirectOptions = GoogleAuthenticationRedirectOptions.Create(google, allowedOrigins);
        services.AddSingleton(redirectOptions);

        authentication.AddGoogle(options =>
        {
            options.ClientId = clientId;
            options.ClientSecret = clientSecret;
            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.CallbackPath = "/signin-google";
            options.SaveTokens = false;
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            options.ClaimActions.MapJsonKey("urn:google:email_verified", "email_verified");
            options.Events.OnRemoteFailure = async context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("CriatorioVirtual.Api.Authentication");
                logger.LogWarning(
                    context.Failure,
                    "Google remote authentication failed. FailureType: {FailureType}. CorrelationId: {CorrelationId}.",
                    context.Failure?.GetType().Name ?? "Unknown",
                    context.HttpContext.TraceIdentifier);
                context.HandleResponse();
                context.HttpContext.Response.Redirect(
                    redirectOptions.BuildFailureRedirect(
                        GoogleAuthenticationRedirectOptions.RemoteProviderFailureCode,
                        context.HttpContext.TraceIdentifier));
            };
        });
    }

    private static Task HandleApiRedirectAsync(
        RedirectContext<CookieAuthenticationOptions> context,
        int statusCode)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
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
