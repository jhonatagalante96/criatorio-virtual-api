using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CriatorioVirtual.Api;

public sealed class ApiProblemDetailsLoggingFilter(
    ILogger<ApiProblemDetailsLoggingFilter> logger) : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not ObjectResult { Value: ProblemDetails problemDetails })
        {
            return;
        }

        var statusCode = problemDetails.Status ?? context.HttpContext.Response.StatusCode;
        if (statusCode < StatusCodes.Status400BadRequest)
        {
            return;
        }

        var detail = string.IsNullOrWhiteSpace(problemDetails.Detail)
            ? HttpProblemResults.DetailFor(statusCode)
            : problemDetails.Detail;
        var validationErrors = problemDetails is ValidationProblemDetails validationProblemDetails
            ? FormatValidationErrors(validationProblemDetails.Errors)
            : null;
        var logLevel = statusCode >= StatusCodes.Status500InternalServerError
            ? LogLevel.Error
            : LogLevel.Warning;

        logger.Log(
            logLevel,
            "API route returned a problem. Method: {Method}. Path: {Path}. StatusCode: {StatusCode}. Title: {Title}. Detail: {Detail}. Type: {Type}. ValidationErrors: {ValidationErrors}. CorrelationId: {CorrelationId}.",
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path,
            statusCode,
            problemDetails.Title,
            detail,
            problemDetails.Type,
            validationErrors,
            context.HttpContext.TraceIdentifier);
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }

    private static string? FormatValidationErrors(IEnumerable<KeyValuePair<string, string[]>> errors)
    {
        var errorList = errors.ToArray();
        if (errorList.Length == 0)
        {
            return null;
        }

        var formatted = string.Join(
            "; ",
            errorList
                .OrderBy(error => error.Key, StringComparer.Ordinal)
                .Select(error => $"{error.Key}: {string.Join(" | ", error.Value)}"));

        return formatted.Length <= 2000
            ? formatted
            : $"{formatted[..2000]}...";
    }
}
