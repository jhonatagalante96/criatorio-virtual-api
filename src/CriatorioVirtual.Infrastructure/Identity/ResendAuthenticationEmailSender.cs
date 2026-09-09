using System.Net.Http.Headers;
using System.Net.Mail;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CriatorioVirtual.Application.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CriatorioVirtual.Infrastructure.Identity;

public sealed class ResendAuthenticationEmailSender(
    HttpClient httpClient,
    IOptions<AuthenticationEmailOptions> options,
    ILogger<ResendAuthenticationEmailSender> logger)
    : IAuthenticationEmailSender
{
    public async Task<AuthenticationEmailDeliveryResult> SendAsync(
        AuthenticationEmailMessage message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settings = options.Value;
        if (!AuthenticationEmailActionUrlPolicy.IsSafeMessage(message, settings))
        {
            return AuthenticationEmailDeliveryResult.Failed();
        }

        var content = AuthenticationEmailTemplateRenderer.Render(message);
        var senderAddress = new MailAddress(settings.SenderAddress).Address;
        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new ResendEmailRequest(
                senderAddress,
                [message.Recipient],
                content.Subject,
                content.HtmlBody,
                content.TextBody))
        };
        request.Headers.UserAgent.ParseAdd("CriatorioVirtual.Api/1.0");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            settings.ResendApiKey.Trim());

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return AuthenticationEmailDeliveryResult.Delivered();
            }

            logger.LogWarning(
                "Resend rejected authentication email delivery with HTTP status {StatusCode}.",
                (int)response.StatusCode);
            return AuthenticationEmailDeliveryResult.Failed();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Do not log the message, recipient, action URL, token, API key, or
            // provider response body.
            logger.LogWarning(exception, "Resend authentication email delivery failed.");
            return AuthenticationEmailDeliveryResult.Failed();
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Resend authentication email delivery timed out.");
            return AuthenticationEmailDeliveryResult.Failed();
        }
    }

    private sealed record ResendEmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] IReadOnlyCollection<string> To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string Text);
}
