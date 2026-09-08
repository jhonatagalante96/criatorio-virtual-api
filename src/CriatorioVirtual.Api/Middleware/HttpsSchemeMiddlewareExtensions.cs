namespace CriatorioVirtual.Api;

public static class HttpsSchemeMiddlewareExtensions
{
    private const string AssumeHttpsBehindProxyConfigurationKey = "Security:AssumeHttpsBehindProxy";

    public static IApplicationBuilder UseAssumedHttpsBehindProxy(
        this IApplicationBuilder app,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!configuration.GetValue<bool>(AssumeHttpsBehindProxyConfigurationKey))
        {
            return app;
        }

        return app.Use((context, next) =>
        {
            context.Request.Scheme = Uri.UriSchemeHttps;
            return next(context);
        });
    }
}
