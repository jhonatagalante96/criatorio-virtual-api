using CriatorioVirtual.Api;
using CriatorioVirtual.Api.Controllers;
using CriatorioVirtual.Application.Identity;
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
    public void ProblemDetailsFilter_RecordsInvalidSelectedTenantWithCorrelationId()
    {
        using var loggerProvider = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.AddProvider(loggerProvider);
            logging.SetMinimumLevel(LogLevel.Trace);
        });
        var filter = new ApiProblemDetailsLoggingFilter(
            loggerFactory.CreateLogger<ApiProblemDetailsLoggingFilter>());
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "The selected breeding farm was not found."
        };
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "tenant-request-123"
        };
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Path = "/api/dashboard";
        var context = new ResultExecutingContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            [],
            new ObjectResult(problem),
            controller: new object());

        filter.OnResultExecuting(context);

        Assert.Contains(
            loggerProvider.Messages,
            message => message.Contains("InvalidTenantAccess", StringComparison.Ordinal) &&
                       message.Contains("tenant-request-123", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(AccountLoginStatus.Invalid, "LoginFailed")]
    [InlineData(AccountLoginStatus.LockedOut, "AccountLocked")]
    public async Task AccountSessionController_LogsAuthenticationEventWithCorrelationId(
        AccountLoginStatus status,
        string eventName)
    {
        using var loggerProvider = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.AddProvider(loggerProvider);
            logging.SetMinimumLevel(LogLevel.Trace);
        });
        var controller = new AccountSessionController(
            loggerFactory.CreateLogger<AccountSessionController>(),
            new StubAccountSessionService(status))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { TraceIdentifier = "login-request-123" }
            }
        };

        _ = await controller.LoginAsync(
            new LoginAccountRequest("owner@example.com", "not-logged-password"),
            CancellationToken.None);

        Assert.Contains(
            loggerProvider.Messages,
            message => message.Contains(eventName, StringComparison.Ordinal) &&
                       message.Contains("login-request-123", StringComparison.Ordinal));
        Assert.DoesNotContain("not-logged-password", string.Join(Environment.NewLine, loggerProvider.Messages), StringComparison.Ordinal);
        Assert.DoesNotContain("owner@example.com", string.Join(Environment.NewLine, loggerProvider.Messages), StringComparison.Ordinal);
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

    private sealed class StubAccountSessionService(AccountLoginStatus loginStatus) : IAccountSessionService
    {
        public Task<AccountLoginResult> LoginAsync(
            string email,
            string password,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccountLoginResult(loginStatus));

        public Task<AccountSession?> GetCurrentAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountSession?>(null);

        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
