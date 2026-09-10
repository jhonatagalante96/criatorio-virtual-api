using CriatorioVirtual.Application.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class GoogleAuthenticationController(
    IGoogleAccountAuthenticationService? googleAuthenticationService = null,
    IAuthenticationSchemeProvider? authenticationSchemes = null) : ControllerBase
{
    [HttpGet("google", Name = "BeginGoogleAuthentication")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> BeginAsync()
    {
        var googleScheme = googleAuthenticationService is null || authenticationSchemes is null
            ? null
            : await authenticationSchemes.GetSchemeAsync(GoogleDefaults.AuthenticationScheme);
        if (googleScheme is null)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Google authentication is unavailable.",
                type: "https://httpstatuses.com/503");
        }

        var callbackUrl = Url.RouteUrl(
            "CompleteGoogleAuthentication",
            values: null,
            protocol: Request.Scheme);
        if (string.IsNullOrWhiteSpace(callbackUrl))
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Google authentication is unavailable.",
                type: "https://httpstatuses.com/503");
        }

        return Challenge(
            new AuthenticationProperties { RedirectUri = callbackUrl },
            GoogleDefaults.AuthenticationScheme);
    }

    [HttpGet("google/callback", Name = "CompleteGoogleAuthentication")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CompleteAsync(CancellationToken cancellationToken)
    {
        if (googleAuthenticationService is null)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Google authentication is unavailable.",
                type: "https://httpstatuses.com/503");
        }

        try
        {
            var result = await googleAuthenticationService.CompleteAsync(cancellationToken);
            return result.Status == GoogleAuthenticationStatus.Succeeded
                ? NoContent()
                : Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Google authentication failed.",
                    detail: DetailFor(result),
                    type: "https://httpstatuses.com/401");
        }
        finally
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        }
    }

    private static string DetailFor(GoogleAuthenticationResult result) =>
        result.Status == GoogleAuthenticationStatus.EmailConflict
            ? "An account already exists with this Google e-mail. Use the existing sign-in method."
            : result.FailureReason switch
            {
                GoogleAuthenticationFailureReason.EmailMissing =>
                    "Google did not provide a usable e-mail address. Try again with a different Google account.",
                GoogleAuthenticationFailureReason.EmailUnverified =>
                    "The Google account e-mail must be verified before it can be used to sign in.",
                GoogleAuthenticationFailureReason.AccountUnavailable =>
                    "The account is currently unavailable for Google sign-in.",
                GoogleAuthenticationFailureReason.AccountProvisioningFailed =>
                    "The account could not be created. Try again later or use another sign-in method.",
                _ => "The Google sign-in response was invalid or expired. Start the sign-in flow again."
            };
}
