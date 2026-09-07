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
            if (RequiresValidation(context.Request.Method) && antiforgeryMetadata?.RequiresValidation is not false)
            {
                try
                {
                    var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
                    await antiforgery.ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException)
                {
                    await HttpProblemResults.Write(context, StatusCodes.Status400BadRequest);
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
