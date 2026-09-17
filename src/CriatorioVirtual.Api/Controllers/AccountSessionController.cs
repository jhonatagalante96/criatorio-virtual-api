using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CriatorioVirtual.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AccountSessionController(IAccountSessionService? sessionService = null) : ControllerBase
{
    [HttpPost("login", Name = "LoginAccount")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> LoginAsync(
        [FromBody] LoginAccountRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return InvalidRequest("The request body is required.");
        }

        var email = request.Email?.Trim();
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email))
        {
            errors["email"] = ["A valid email address is required."];
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            errors["password"] = ["A password is required."];
        }

        if (errors.Count > 0)
        {
            return ValidationProblemResult(errors);
        }

        if (sessionService is null)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Account authentication is unavailable.",
                type: "https://httpstatuses.com/503");
        }

        var result = await sessionService.LoginAsync(email!, request.Password!, cancellationToken);
        return result.Status switch
        {
            AccountLoginStatus.Succeeded => NoContent(),
            AccountLoginStatus.EmailUnconfirmed => Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Email confirmation is required.",
                type: "https://httpstatuses.com/403",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "email_confirmation_required"
                }),
            _ => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid email or password.",
                type: "https://httpstatuses.com/401")
        };
    }

    [HttpGet("session", Name = "GetCurrentAccountSession")]
    [Authorize]
    [ProducesResponseType(typeof(AccountSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetCurrentAsync(CancellationToken cancellationToken)
    {
        if (sessionService is null)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Account authentication is unavailable.",
                type: "https://httpstatuses.com/503");
        }

        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401");
        }

        var session = await sessionService.GetCurrentAsync(userId, cancellationToken);
        return session is null
            ? Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401")
            : Ok(new AccountSessionResponse(
                session.UserId,
                session.Email,
                session.EmailConfirmed,
                session.HasAvatar ? Url.RouteUrl("GetCurrentUserAvatar") : null));
    }

    [HttpPost("logout", Name = "LogoutAccount")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> LogoutAsync(CancellationToken cancellationToken)
    {
        if (sessionService is null)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Account authentication is unavailable.",
                type: "https://httpstatuses.com/503");
        }

        await sessionService.LogoutAsync(cancellationToken);
        return NoContent();
    }

    private IActionResult InvalidRequest(string detail) =>
        Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Login data is invalid.",
            detail: detail,
            type: "https://httpstatuses.com/400");

    private IActionResult ValidationProblemResult(IReadOnlyDictionary<string, string[]> errors)
    {
        ModelState.Clear();
        foreach (var (key, messages) in errors)
        {
            foreach (var message in messages)
            {
                ModelState.AddModelError(key, message);
            }
        }

        return ValidationProblem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Login data is invalid.",
            type: "https://httpstatuses.com/400",
            modelStateDictionary: ModelState);
    }
}

public sealed record LoginAccountRequest(string? Email, string? Password);

public sealed record AccountSessionResponse(Guid UserId, string Email, bool EmailConfirmed, string? AvatarUrl);
