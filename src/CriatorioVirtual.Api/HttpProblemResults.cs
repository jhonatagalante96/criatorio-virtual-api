using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api;

public static class HttpProblemResults
{
    public static Task Write(HttpContext context, int statusCode)
    {
        var problem = Results.Problem(
            statusCode: statusCode,
            title: TitleFor(statusCode),
            type: $"https://httpstatuses.com/{statusCode}");

        return problem.ExecuteAsync(context);
    }

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
