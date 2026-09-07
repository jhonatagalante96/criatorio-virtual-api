using System.Diagnostics;

namespace CriatorioVirtual.Api;

public static class CorrelationIdMiddlewareExtensions
{
    public const string HeaderName = "X-Correlation-ID";

    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            var correlationId = GetCorrelationId(context.Request.Headers[HeaderName]);
            context.TraceIdentifier = correlationId;
            context.Response.Headers[HeaderName] = correlationId;

            using (context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("CriatorioVirtual.Api.Request")
                .BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId }))
            {
                await next();
            }
        });
    }

    private static string GetCorrelationId(string? suppliedValue) =>
        suppliedValue is { Length: > 0 and <= 64 } && suppliedValue.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            ? suppliedValue
            : ActivityTraceId.CreateRandom().ToString();
}
