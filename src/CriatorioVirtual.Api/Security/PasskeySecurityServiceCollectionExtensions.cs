using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Hosting;

namespace CriatorioVirtual.Api;

public static class PasskeySecurityServiceCollectionExtensions
{
    public const string PasskeyLoginRateLimitPolicyName = "passkey-login";

    private const string PasskeyServerDomainConfigurationKey = "Security:Passkeys:ServerDomain";
    private const string RequiredUserVerification = "required";
    private const string RequiredResidentKey = "required";
    private const string NoAttestation = "none";
    private static readonly TimeSpan AuthenticatorTimeout = TimeSpan.FromMinutes(2);

    public static IServiceCollection AddPasskeySecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var allowedOrigins = HttpSecurityServiceCollectionExtensions.GetAllowedOrigins(configuration)
            .Select(ParseOrigin)
            .ToArray();
        var serverDomain = ResolveServerDomain(configuration, environment, allowedOrigins);

        ValidateServerDomain(serverDomain);
        ValidateAllowedOrigins(serverDomain, allowedOrigins);

        var normalizedAllowedOrigins = allowedOrigins
            .Select(GetOrigin)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        services.Configure<IdentityPasskeyOptions>(options =>
        {
            options.ServerDomain = serverDomain;
            options.AuthenticatorTimeout = AuthenticatorTimeout;
            options.ChallengeSize = 32;
            options.UserVerificationRequirement = RequiredUserVerification;
            options.ResidentKeyRequirement = RequiredResidentKey;
            options.AttestationConveyancePreference = NoAttestation;
            options.ValidateOrigin = context =>
            {
                var isAllowedOrigin = !context.CrossOrigin &&
                    normalizedAllowedOrigins.Contains(NormalizeOrigin(context.Origin));

                return ValueTask.FromResult(isAllowedOrigin);
            };
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return ValueTask.CompletedTask;
            };
            options.AddPolicy(PasskeyLoginRateLimitPolicyName, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        return services;
    }

    private static string ResolveServerDomain(
        IConfiguration configuration,
        IHostEnvironment environment,
        IReadOnlyCollection<Uri> allowedOrigins)
    {
        var configuredServerDomain = configuration[PasskeyServerDomainConfigurationKey];
        if (!string.IsNullOrWhiteSpace(configuredServerDomain))
        {
            return configuredServerDomain.Trim().TrimEnd('.').ToLowerInvariant();
        }

        if ((environment.IsDevelopment() || environment.IsEnvironment("Testing")) &&
            allowedOrigins.All(origin => origin.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)))
        {
            return "localhost";
        }

        throw new InvalidOperationException(
            $"{PasskeyServerDomainConfigurationKey} must be configured explicitly outside local localhost environments.");
    }

    private static Uri ParseOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var parsedOrigin) ||
            (parsedOrigin.Scheme != Uri.UriSchemeHttps && parsedOrigin.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(parsedOrigin.UserInfo) ||
            parsedOrigin.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(parsedOrigin.Query) ||
            !string.IsNullOrEmpty(parsedOrigin.Fragment))
        {
            throw new InvalidOperationException(
                "Security:AllowedOrigins must contain valid HTTP(S) origins without paths, credentials, queries, or fragments.");
        }

        if (parsedOrigin.Scheme == Uri.UriSchemeHttp && !IsLoopbackHost(parsedOrigin.Host))
        {
            throw new InvalidOperationException(
                "Security:AllowedOrigins may use HTTP only for localhost or loopback origins.");
        }

        return parsedOrigin;
    }

    private static void ValidateServerDomain(string serverDomain)
    {
        if (string.IsNullOrWhiteSpace(serverDomain) ||
            serverDomain.Contains('/', StringComparison.Ordinal) ||
            serverDomain.Contains(':', StringComparison.Ordinal) ||
            serverDomain.Contains('@', StringComparison.Ordinal) ||
            serverDomain.Contains('*', StringComparison.Ordinal) ||
            Uri.CheckHostName(serverDomain) != UriHostNameType.Dns)
        {
            throw new InvalidOperationException(
                $"{PasskeyServerDomainConfigurationKey} must be a valid DNS RP ID without a scheme, port, path, wildcard, or credentials.");
        }
    }

    private static void ValidateAllowedOrigins(string serverDomain, IEnumerable<Uri> allowedOrigins)
    {
        foreach (var origin in allowedOrigins)
        {
            var normalizedHost = origin.Host.TrimEnd('.');
            var isServerDomainOrSubdomain = normalizedHost.Equals(serverDomain, StringComparison.OrdinalIgnoreCase) ||
                normalizedHost.EndsWith($".{serverDomain}", StringComparison.OrdinalIgnoreCase);

            if (!isServerDomainOrSubdomain)
            {
                throw new InvalidOperationException(
                    $"Security:AllowedOrigins origin '{GetOrigin(origin)}' is outside the configured passkey RP ID '{serverDomain}'.");
            }
        }
    }

    private static bool IsLoopbackHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));

    private static string GetOrigin(Uri origin) => origin.GetLeftPart(UriPartial.Authority);

    private static string NormalizeOrigin(string origin)
    {
        return Uri.TryCreate(origin, UriKind.Absolute, out var parsedOrigin) &&
            (parsedOrigin.Scheme == Uri.UriSchemeHttps || parsedOrigin.Scheme == Uri.UriSchemeHttp)
            ? GetOrigin(parsedOrigin)
            : string.Empty;
    }
}
