using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api;

public static class HttpProblemResults
{
    public static Task Write(HttpContext context, int statusCode, string? detail = null)
    {
        var problem = Results.Problem(
            statusCode: statusCode,
            title: TitleFor(statusCode),
            detail: detail ?? DetailFor(statusCode),
            type: $"https://httpstatuses.com/{statusCode}",
            extensions: new Dictionary<string, object?>
            {
                ["correlationId"] = context.TraceIdentifier
            });

        return problem.ExecuteAsync(context);
    }

    public static string DetailFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "The request was rejected because it is invalid or could not be validated.",
        StatusCodes.Status401Unauthorized => "Authentication is required to access this resource.",
        StatusCodes.Status403Forbidden => "The authenticated user is not authorized to access this resource.",
        StatusCodes.Status404NotFound => "No route matched the requested resource.",
        StatusCodes.Status409Conflict => "The request conflicts with the current state of the resource.",
        StatusCodes.Status413PayloadTooLarge => "The request payload exceeds the permitted size.",
        StatusCodes.Status415UnsupportedMediaType => "The request content type is not supported.",
        StatusCodes.Status429TooManyRequests => "Too many requests were received. Try again later.",
        StatusCodes.Status503ServiceUnavailable => "The service is temporarily unavailable.",
        _ => "An unexpected error occurred while processing the request."
    };

    private static string TitleFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status413PayloadTooLarge => "Payload Too Large",
        StatusCodes.Status415UnsupportedMediaType => "Unsupported Media Type",
        StatusCodes.Status429TooManyRequests => "Too Many Requests",
        _ => "Internal Server Error"
    };
}
