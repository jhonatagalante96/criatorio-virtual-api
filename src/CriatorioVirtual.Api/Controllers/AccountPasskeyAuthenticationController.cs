using System.Text;
using CriatorioVirtual.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/auth/passkeys/login")]
[AllowAnonymous]
[EnableRateLimiting(PasskeySecurityServiceCollectionExtensions.PasskeyLoginRateLimitPolicyName)]
public sealed class AccountPasskeyAuthenticationController(
    IAccountPasskeyAuthenticationService? authenticationService = null) : ControllerBase
{
    [HttpPost("options", Name = "CreatePasskeyLoginOptions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CreateOptionsAsync(CancellationToken cancellationToken)
    {
        SetNoStoreHeader();
        if (authenticationService is null)
        {
            return Unavailable();
        }

        var result = await authenticationService.CreateRequestOptionsAsync(cancellationToken);
        return result.Status switch
        {
            PasskeyAuthenticationStatus.Succeeded => Content(result.OptionsJson!, "application/json", Encoding.UTF8),
            PasskeyAuthenticationStatus.Unsupported => Unavailable(),
            _ => Unavailable()
        };
    }

    [HttpPost("verify", Name = "LoginWithPasskey")]
    [RequestSizeLimit(128 * 1024)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> VerifyAsync(
        [FromBody] PasskeyLoginRequest? request,
        CancellationToken cancellationToken)
    {
        SetNoStoreHeader();
        if (authenticationService is null)
        {
            return Unavailable();
        }

        var result = await authenticationService.AuthenticateAsync(request?.CredentialJson, cancellationToken);
        return result.Status switch
        {
            PasskeyAuthenticationStatus.Succeeded => NoContent(),
            PasskeyAuthenticationStatus.NotAllowed => Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Email confirmation is required.",
                type: "https://httpstatuses.com/403",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "email_confirmation_required"
                }),
            PasskeyAuthenticationStatus.Unsupported => Unavailable(),
            _ => InvalidPasskey()
        };
    }

    private IActionResult InvalidPasskey() =>
        Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Invalid passkey.",
            type: "https://httpstatuses.com/401");

    private IActionResult Unavailable() =>
        Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Passkey authentication is unavailable.",
            type: "https://httpstatuses.com/503");

    private void SetNoStoreHeader() => Response.Headers.CacheControl = "no-store";
}

public sealed record PasskeyLoginRequest(string? CredentialJson);
