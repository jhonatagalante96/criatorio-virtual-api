using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.IntegrationTests.Security;

[ApiController]
[Route("api/test/google")]
[AllowAnonymous]
public sealed class GoogleExternalCookieController : ControllerBase
{
    [HttpGet("seed")]
    public async Task<IActionResult> SeedAsync(
        [FromQuery] string providerKey,
        [FromQuery] string email,
        [FromQuery] bool verified = true)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, providerKey),
            new(ClaimTypes.Email, email),
            new("urn:google:email_verified", verified.ToString())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Google"));
        var properties = new AuthenticationProperties();
        properties.Items["LoginProvider"] = "Google";

        await HttpContext.SignInAsync(IdentityConstants.ExternalScheme, principal, properties);
        return NoContent();
    }
}
