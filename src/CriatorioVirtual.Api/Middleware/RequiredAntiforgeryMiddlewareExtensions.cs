using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Metadata;

namespace CriatorioVirtual.Api;

public static class RequiredAntiforgeryMiddlewareExtensions
{
    public static IApplicationBuilder UseRequiredAntiforgeryProtection(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            var antiforgeryMetadata = context.GetEndpoint()?.Metadata.GetMetadata<IAntiforgeryMetadata>();
            var skipRequiredAntiforgery = context.GetEndpoint()?.Metadata
                .GetMetadata<SkipRequiredAntiforgeryAttribute>() is not null;
            if (RequiresValidation(context.Request.Method) &&
                antiforgeryMetadata?.RequiresValidation is not false &&
                !skipRequiredAntiforgery)
            {
                try
                {
                    var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
                    await antiforgery.ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException exception)
                {
                    context.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("CriatorioVirtual.Api.Errors")
                        .LogWarning(
                            "Antiforgery validation failed. Method: {Method}. Path: {Path}. Reason: {Reason}. CorrelationId: {CorrelationId}.",
                            context.Request.Method,
                            context.Request.Path,
                            exception.Message,
                            context.TraceIdentifier);

                    await HttpProblemResults.Write(
                        context,
                        StatusCodes.Status400BadRequest,
                        "The antiforgery token is missing, invalid, or expired.");
                    return;
                }
            }

            await next(context);
        });
    }

    private static bool RequiresValidation(string method) =>
        HttpMethods.IsPost(method) ||
        HttpMethods.IsPut(method) ||
        HttpMethods.IsPatch(method) ||
        HttpMethods.IsDelete(method);
}
