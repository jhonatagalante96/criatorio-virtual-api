using CriatorioVirtual.Api;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CriatorioVirtual.IntegrationTests;

public sealed class RequiredAntiforgeryMiddlewareExtensionsTests
{
    [Fact]
    public async Task InvalidAntiforgeryToken_DoesNotExecuteMutation()
    {
        var didMutate = false;
        var app = BuildPipeline(new StubAntiforgery(isValid: false), () => didMutate = true);
        var context = CreateContext(HttpMethods.Post, app.ApplicationServices);

        await app.Invoke(context);

        Assert.False(didMutate);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task ValidAntiforgeryToken_AllowsMutation()
    {
        var didMutate = false;
        var app = BuildPipeline(new StubAntiforgery(isValid: true), () => didMutate = true);
        var context = CreateContext(HttpMethods.Post, app.ApplicationServices);

        await app.Invoke(context);

        Assert.True(didMutate);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task SafeMethod_DoesNotRequireAntiforgeryToken()
    {
        var didExecute = false;
        var antiforgery = new StubAntiforgery(isValid: false);
        var app = BuildPipeline(antiforgery, () => didExecute = true);
        var context = CreateContext(HttpMethods.Get, app.ApplicationServices);

        await app.Invoke(context);

        Assert.True(didExecute);
        Assert.False(antiforgery.WasValidated);
    }

    private static (RequestDelegate Invoke, IServiceProvider ApplicationServices) BuildPipeline(
        IAntiforgery antiforgery,
        Action terminalAction)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        services.AddSingleton(antiforgery);
        var provider = services.BuildServiceProvider();
        var builder = new ApplicationBuilder(provider);
        builder.UseRequiredAntiforgeryProtection();
        builder.Run(_ =>
        {
            terminalAction();
            return Task.CompletedTask;
        });

        return (builder.Build(), provider);
    }

    private static DefaultHttpContext CreateContext(string method, IServiceProvider services) => new()
    {
        RequestServices = services,
        Request =
        {
            Method = method
        },
        Response =
        {
            Body = new MemoryStream()
        }
    };

    private sealed class StubAntiforgery(bool isValid) : IAntiforgery
    {
        public bool WasValidated { get; private set; }

        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) => throw new NotSupportedException();

        public AntiforgeryTokenSet GetTokens(HttpContext httpContext) => throw new NotSupportedException();

        public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(isValid);

        public void SetCookieTokenAndHeader(HttpContext httpContext) => throw new NotSupportedException();

        public Task ValidateRequestAsync(HttpContext httpContext)
        {
            WasValidated = true;
            return isValid
                ? Task.CompletedTask
                : Task.FromException(new AntiforgeryValidationException("Invalid test token."));
        }
    }
}
