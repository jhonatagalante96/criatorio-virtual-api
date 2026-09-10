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
    IAuthenticationSchemeProvider? authenticationSchemes = null,
    GoogleAuthenticationRedirectOptions? redirectOptions = null) : ControllerBase
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

        // GetExternalLoginInfoAsync relies on LoginProvider when it reads the
        // Identity.External cookie after the remote handler completes.
        var properties = CreateExternalAuthenticationProperties(callbackUrl);

        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    [HttpGet("google/callback", Name = "CompleteGoogleAuthentication")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CompleteAsync(CancellationToken cancellationToken)
    {
        if (googleAuthenticationService is null || redirectOptions is null)
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
                ? Redirect(redirectOptions.BuildSuccessRedirect())
                : Redirect(redirectOptions.BuildFailureRedirect(
                    ErrorCodeFor(result),
                    HttpContext.TraceIdentifier));
        }
        finally
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        }
    }

    private static string ErrorCodeFor(GoogleAuthenticationResult result) =>
        result.Status == GoogleAuthenticationStatus.EmailConflict
            ? "email_conflict"
            : result.FailureReason switch
            {
                GoogleAuthenticationFailureReason.EmailMissing => "email_missing",
                GoogleAuthenticationFailureReason.EmailUnverified => "email_unverified",
                GoogleAuthenticationFailureReason.AccountUnavailable => "account_unavailable",
                GoogleAuthenticationFailureReason.AccountProvisioningFailed => "account_provisioning_failed",
                _ => "external_login_unavailable"
            };

    private static AuthenticationProperties CreateExternalAuthenticationProperties(string callbackUrl)
    {
        var properties = new AuthenticationProperties
        {
            RedirectUri = callbackUrl
        };
        properties.Items["LoginProvider"] = GoogleDefaults.AuthenticationScheme;
        return properties;
    }
}
