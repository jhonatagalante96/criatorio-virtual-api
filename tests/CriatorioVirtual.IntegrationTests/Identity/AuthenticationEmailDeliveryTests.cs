using CriatorioVirtual.Application.Identity;
using CriatorioVirtual.Infrastructure.Identity;
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
}
