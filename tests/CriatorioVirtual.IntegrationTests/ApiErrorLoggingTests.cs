using CriatorioVirtual.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CriatorioVirtual.IntegrationTests;

public sealed class ApiErrorLoggingTests
{
    [Fact]
    public void ProblemDetailsFilter_LogsProblemDetailAndValidationErrors()
    {
        using var loggerProvider = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.AddProvider(loggerProvider);
            logging.SetMinimumLevel(LogLevel.Trace);
        });
        var filter = new ApiProblemDetailsLoggingFilter(
            loggerFactory.CreateLogger<ApiProblemDetailsLoggingFilter>());
        var problem = new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            ["password"] = ["The password does not meet the required policy."]
        })
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Registration data is invalid.",
            Detail = "The submitted registration data contains validation errors."
        };
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "request-123"
        };
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.Path = "/api/auth/register";
        var context = new ResultExecutingContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            [],
            new ObjectResult(problem),
            controller: new object());

        filter.OnResultExecuting(context);

        var message = Assert.Single(loggerProvider.Messages);
        Assert.Contains("/api/auth/register", message, StringComparison.Ordinal);
        Assert.Contains("Registration data is invalid.", message, StringComparison.Ordinal);
        Assert.Contains("submitted registration data", message, StringComparison.Ordinal);
        Assert.Contains("password", message, StringComparison.Ordinal);
        Assert.Contains("request-123", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ErrorMiddleware_LogsRequestContextForFailedResponses()
    {
        using var loggerProvider = new RecordingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.AddProvider(loggerProvider);
            logging.SetMinimumLevel(LogLevel.Trace);
        });
        using var serviceProvider = services.BuildServiceProvider();
        var builder = new ApplicationBuilder(serviceProvider);
        builder.UseApiErrorLogging();
        builder.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return Task.CompletedTask;
        });
        var application = builder.Build();
        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider,
            TraceIdentifier = "request-456"
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/auth/session";

        await application(context);

        var message = Assert.Single(loggerProvider.Messages);
        Assert.Contains("/api/auth/session", message, StringComparison.Ordinal);
        Assert.Contains("StatusCode: 503", message, StringComparison.Ordinal);
        Assert.Contains("temporarily unavailable", message, StringComparison.Ordinal);
        Assert.Contains("request-456", message, StringComparison.Ordinal);
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(ICollection<string> messages) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Warning)
                {
                    messages.Add(formatter(state, exception));
                }
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
