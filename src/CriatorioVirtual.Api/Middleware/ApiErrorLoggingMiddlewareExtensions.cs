namespace CriatorioVirtual.Api;

public static class ApiErrorLoggingMiddlewareExtensions
{
    private const string LoggerCategory = "CriatorioVirtual.Api.Errors";

    public static IApplicationBuilder UseApiErrorLogging(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            await next(context);

            if (context.Response.StatusCode < StatusCodes.Status400BadRequest)
            {
                return;
            }

            var logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(LoggerCategory);
            var endpoint = context.GetEndpoint()?.DisplayName ?? "No endpoint matched";
            var logLevel = context.Response.StatusCode >= StatusCodes.Status500InternalServerError
                ? LogLevel.Error
                : LogLevel.Warning;

            logger.Log(
                logLevel,
                "HTTP request failed. Method: {Method}. Path: {Path}. StatusCode: {StatusCode}. Reason: {Reason}. Endpoint: {Endpoint}. CorrelationId: {CorrelationId}.",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                HttpProblemResults.DetailFor(context.Response.StatusCode),
                endpoint,
                context.TraceIdentifier);
        });
    }
}
