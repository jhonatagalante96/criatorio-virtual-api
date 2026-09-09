using CriatorioVirtual.Application.Identity;
using CriatorioVirtual.Infrastructure.Identity;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Identity;

public sealed class AuthenticationEmailDeliveryTests
{
    [Theory]
    [InlineData(AuthenticationEmailKind.Confirmation, "/auth/confirm-email")]
    [InlineData(AuthenticationEmailKind.PasswordReset, "/auth/reset-password")]
    public void LinkBuilder_UsesTheConfiguredLocalBaseAndEncodesTheToken(
        AuthenticationEmailKind kind,
        string expectedPath)
    {
        var builder = new AuthenticationEmailLinkBuilder(Options.Create(new AuthenticationEmailOptions
        {
            ClientBaseUrl = "http://localhost:3000",
            ConfirmationPath = "/auth/confirm-email",
            PasswordResetPath = "/auth/reset-password"
        }));
        var userId = Guid.Parse("f3dd7fbb-7d5f-4cf6-8f4f-0b0c8a57d8d1");

        var link = builder.Build(kind, userId, "token+/=?");

        Assert.Equal("http", link.Scheme);
        Assert.Equal("localhost", link.Host);
        Assert.Equal(3000, link.Port);
        Assert.Equal(expectedPath, link.AbsolutePath);
        Assert.Contains("userId=f3dd7fbb-7d5f-4cf6-8f4f-0b0c8a57d8d1", link.Query, StringComparison.Ordinal);
        Assert.Contains("token=token%2B%2F%3D%3F", link.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InMemorySender_DeliversConfirmationAndResetMessagesToTheTestInbox()
    {
        var sender = new InMemoryAuthenticationEmailSender(Options.Create(new AuthenticationEmailOptions
        {
            ClientBaseUrl = "http://localhost:3000"
        }));
        var messages = new[]
        {
            new AuthenticationEmailMessage(
                AuthenticationEmailKind.Confirmation,
                "owner@example.com",
                new Uri("http://localhost:3000/auth/confirm-email?token=confirmation")),
            new AuthenticationEmailMessage(
                AuthenticationEmailKind.PasswordReset,
                "owner@example.com",
                new Uri("http://localhost:3000/auth/reset-password?token=reset"))
        };

        foreach (var message in messages)
        {
            var result = await sender.SendAsync(message);
            Assert.True(result.Succeeded);
        }

        Assert.Equal(messages, sender.Messages);
    }

    [Theory]
    [InlineData(AuthenticationEmailKind.Confirmation, "Confirme seu e-mail", "Confirmar meu e-mail")]
    [InlineData(AuthenticationEmailKind.PasswordReset, "Redefina sua senha", "Redefinir minha senha")]
    public void TemplateRenderer_ProducesTheRequestedPortugueseHtmlLayout(
        AuthenticationEmailKind kind,
        string expectedHeading,
        string expectedButton)
    {
        var actionPath = kind == AuthenticationEmailKind.Confirmation
            ? "/auth/confirm-email"
            : "/auth/reset-password";
        var message = new AuthenticationEmailMessage(
            kind,
            "owner@example.com",
            new Uri($"http://localhost:3000{actionPath}?userId=1&token=token%2B%2F"));

        var content = AuthenticationEmailTemplateRenderer.Render(message);

        Assert.Contains(expectedHeading, content.Subject, StringComparison.Ordinal);
        Assert.Contains(expectedHeading, content.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(expectedButton, content.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Criatório Virtual", content.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("&amp;token=", content.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("token%2B%2F", content.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InMemorySender_RejectsAnExternalActionUrlWithoutReturningTheToken()
    {
        var sender = new InMemoryAuthenticationEmailSender(Options.Create(new AuthenticationEmailOptions
        {
            ClientBaseUrl = "http://localhost:3000"
        }));

        var result = await sender.SendAsync(new AuthenticationEmailMessage(
            AuthenticationEmailKind.PasswordReset,
            "owner@example.com",
            new Uri("https://production.example.com/reset?token=secret-token")));

        Assert.False(result.Succeeded);
        Assert.Empty(sender.Messages);
    }

    [Theory]
    [InlineData("http://localhost:3000/auth/other?token=secret-token")]
    [InlineData("http://localhost:3000/auth/confirm-email")]
    public async Task InMemorySender_RejectsAnUnexpectedOrTokenlessActionUrl(string actionUrl)
    {
        var sender = new InMemoryAuthenticationEmailSender(Options.Create(new AuthenticationEmailOptions
        {
            ClientBaseUrl = "http://localhost:3000"
        }));

        var result = await sender.SendAsync(new AuthenticationEmailMessage(
            AuthenticationEmailKind.Confirmation,
            "owner@example.com",
            new Uri(actionUrl)));

        Assert.False(result.Succeeded);
        Assert.Empty(sender.Messages);
    }

    [Fact]
    public async Task ResendSender_PostsTheRenderedMessageWithTheApiKey()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.resend.com/")
        };
        var sender = new ResendAuthenticationEmailSender(
            httpClient,
            Options.Create(new AuthenticationEmailOptions
            {
                ClientBaseUrl = "http://localhost:3000",
                ResendApiKey = "re_test-key",
                SenderAddress = "noreply@example.com"
            }),
            NullLogger<ResendAuthenticationEmailSender>.Instance);

        var result = await sender.SendAsync(new AuthenticationEmailMessage(
            AuthenticationEmailKind.Confirmation,
            "owner@example.com",
            new Uri("http://localhost:3000/auth/confirm-email?token=secret-token")));

        Assert.True(result.Succeeded);
        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.resend.com/emails", handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("re_test-key", handler.Request.Headers.Authorization.Parameter);
        Assert.Contains(handler.Request.Headers.UserAgent, value => value.Product?.Name == "CriatorioVirtual.Api");

        using var payload = JsonDocument.Parse(handler.Body!);
        Assert.Equal("noreply@example.com", payload.RootElement.GetProperty("from").GetString());
        Assert.Equal("owner@example.com", payload.RootElement.GetProperty("to")[0].GetString());
        Assert.Contains("secret-token", payload.RootElement.GetProperty("html").GetString(), StringComparison.Ordinal);
        Assert.Contains("secret-token", payload.RootElement.GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResendSender_ReturnsFailureWhenTheProviderRejectsTheMessage()
    {
        using var handler = new RecordingHandler(HttpStatusCode.Unauthorized);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.resend.com/")
        };
        var sender = new ResendAuthenticationEmailSender(
            httpClient,
            Options.Create(new AuthenticationEmailOptions
            {
                ClientBaseUrl = "http://localhost:3000",
                ResendApiKey = "re_test-key",
                SenderAddress = "noreply@example.com"
            }),
            NullLogger<ResendAuthenticationEmailSender>.Instance);

        var result = await sender.SendAsync(new AuthenticationEmailMessage(
            AuthenticationEmailKind.PasswordReset,
            "owner@example.com",
            new Uri("http://localhost:3000/auth/reset-password?token=secret-token")));

        Assert.False(result.Succeeded);
        Assert.DoesNotContain("secret-token", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("re_test-key", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResendSender_ReturnsFailureWhenTheProviderTimesOut()
    {
        using var handler = new TimeoutHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.resend.com/")
        };
        var sender = new ResendAuthenticationEmailSender(
            httpClient,
            Options.Create(new AuthenticationEmailOptions
            {
                ClientBaseUrl = "http://localhost:3000",
                ResendApiKey = "re_test-key",
                SenderAddress = "noreply@example.com"
            }),
            NullLogger<ResendAuthenticationEmailSender>.Instance);

        var result = await sender.SendAsync(new AuthenticationEmailMessage(
            AuthenticationEmailKind.Confirmation,
            "owner@example.com",
            new Uri("http://localhost:3000/auth/confirm-email?token=secret-token")));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UnavailableSender_ReturnsARecoverableDetailFreeFailure()
    {
        var sender = new UnavailableAuthenticationEmailSender();

        var result = await sender.SendAsync(new AuthenticationEmailMessage(
            AuthenticationEmailKind.Confirmation,
            "owner@example.com",
            new Uri("https://app.example.com/auth/confirm-email?token=secret-token")));

        Assert.Equal(AuthenticationEmailDeliveryStatus.Failed, result.Status);
        Assert.DoesNotContain("secret-token", result.ToString(), StringComparison.Ordinal);
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                RequestMessage = request
            };
        }
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("provider timeout"));
    }
}
