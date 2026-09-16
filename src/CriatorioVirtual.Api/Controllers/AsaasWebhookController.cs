using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Infrastructure.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/webhooks/asaas")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[SkipRequiredAntiforgery]
public sealed class AsaasWebhookController(
    ICommandExecutor commandExecutor,
    IOptions<AsaasOptions> asaasOptions) : ControllerBase
{
    public const int MaximumPayloadSizeBytes = 256 * 1024;

    private const string AuthenticationHeaderName = "asaas-access-token";

    [HttpPost]
    [RequestSizeLimit(MaximumPayloadSizeBytes)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ReceiveAsync(CancellationToken cancellationToken)
    {
        var expectedToken = asaasOptions.Value.WebhookToken;
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The Asaas webhook endpoint is not configured.",
                type: "https://httpstatuses.com/503");
        }

        if (!Request.Headers.TryGetValue(AuthenticationHeaderName, out var suppliedTokens) ||
            suppliedTokens.Count != 1 ||
            !IsValidToken(suppliedTokens[0], expectedToken))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Webhook authentication failed.",
                type: "https://httpstatuses.com/401");
        }

        if (!Request.HasJsonContentType())
        {
            return Problem(
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "The webhook endpoint accepts application/json only.",
                type: "https://httpstatuses.com/415");
        }

        if (Request.ContentLength is > MaximumPayloadSizeBytes)
        {
            return PayloadTooLarge();
        }

        var body = await ReadBodyAsync(cancellationToken);
        if (body is null)
        {
            return PayloadTooLarge();
        }

        JsonDocument payload;
        try
        {
            payload = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The webhook payload is not valid JSON.",
                type: "https://httpstatuses.com/400");
        }

        using (payload)
        {
            var result = await commandExecutor.Execute<ReceiveAsaasWebhookCommand, ReceiveAsaasWebhookResult>(
                new ReceiveAsaasWebhookCommand(payload.RootElement.Clone()),
                cancellationToken);

            return result.Status switch
            {
                ReceiveAsaasWebhookStatus.Received or ReceiveAsaasWebhookStatus.Duplicate => Ok(),
                ReceiveAsaasWebhookStatus.InvalidPayload => Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "The webhook payload does not contain a valid event envelope.",
                    type: "https://httpstatuses.com/400"),
                _ => throw new InvalidOperationException("The Asaas webhook result is not supported.")
            };
        }
    }

    private static bool IsValidToken(string? suppliedToken, string expectedToken)
    {
        if (string.IsNullOrEmpty(suppliedToken))
        {
            return false;
        }

        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedToken);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
        return CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }

    private async Task<byte[]?> ReadBodyAsync(CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        var buffer = new byte[81920];

        while (true)
        {
            var bytesRead = await Request.Body.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                return body.ToArray();
            }

            if (body.Length + bytesRead > MaximumPayloadSizeBytes)
            {
                return null;
            }

            body.Write(buffer, 0, bytesRead);
        }
    }

    private IActionResult PayloadTooLarge() => Problem(
        statusCode: StatusCodes.Status413PayloadTooLarge,
        title: "The webhook payload exceeds the allowed size.",
        type: "https://httpstatuses.com/413");
}
