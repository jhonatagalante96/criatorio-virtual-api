using System.Security.Claims;
using System.Text;
using CriatorioVirtual.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/auth/passkeys")]
[Authorize]
public sealed class AccountPasskeyController(
    IAccountPasskeyService? passkeyService = null) : ControllerBase
{
    [HttpPost("register/options", Name = "CreatePasskeyRegistrationOptions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreateRegistrationOptionsAsync(CancellationToken cancellationToken)
    {
        SetNoStoreHeader();
        if (passkeyService is null)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Passkey management is unavailable.",
                type: "https://httpstatuses.com/503");
        }

        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var result = await passkeyService.CreateRegistrationOptionsAsync(userId, cancellationToken);
        return result.Status switch
        {
            PasskeyOperationStatus.Succeeded => Content(result.OptionsJson!, "application/json", Encoding.UTF8),
            PasskeyOperationStatus.Unsupported => Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Passkey management is unavailable.",
                type: "https://httpstatuses.com/503"),
            _ => AuthenticationRequired()
        };
    }

    [HttpPost("register/verify", Name = "RegisterPasskey")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RegisterAsync(
        [FromBody] RegisterPasskeyRequest? request,
        CancellationToken cancellationToken)
    {
        SetNoStoreHeader();
        if (passkeyService is null)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Passkey management is unavailable.",
                type: "https://httpstatuses.com/503");
        }

        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var result = await passkeyService.RegisterAsync(userId, request?.CredentialJson, cancellationToken);
        return result.Status switch
        {
            PasskeyOperationStatus.Succeeded => NoContent(),
            PasskeyOperationStatus.Conflict => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The passkey could not be registered.",
                type: "https://httpstatuses.com/409"),
            PasskeyOperationStatus.Unsupported => Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Passkey management is unavailable.",
                type: "https://httpstatuses.com/503"),
            PasskeyOperationStatus.UserNotFound => AuthenticationRequired(),
            _ => ValidationProblemResult(
                "Passkey registration data is invalid.",
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["credentialJson"] = ["A valid passkey credential is required."]
                })
        };
    }

    [HttpGet(Name = "ListAccountPasskeys")]
    [ProducesResponseType(typeof(AccountPasskeyListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken)
    {
        SetNoStoreHeader();
        if (passkeyService is null)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Passkey management is unavailable.",
                type: "https://httpstatuses.com/503");
        }

        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var result = await passkeyService.ListAsync(userId, cancellationToken);
        return result.Status switch
        {
            PasskeyOperationStatus.Succeeded => Ok(new AccountPasskeyListResponse(
                result.Passkeys.Select(passkey => new AccountPasskeyResponse(
                    passkey.CredentialId,
                    passkey.Name,
                    passkey.CreatedAt,
                    passkey.Transports,
                    passkey.IsUserVerified,
                    passkey.IsBackupEligible,
                    passkey.IsBackedUp)))),
            PasskeyOperationStatus.Unsupported => Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Passkey management is unavailable.",
                type: "https://httpstatuses.com/503"),
            _ => AuthenticationRequired()
        };
    }

    [HttpPatch("{credentialId}", Name = "RenameAccountPasskey")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RenameAsync(
        string credentialId,
        [FromBody] RenamePasskeyRequest? request,
        CancellationToken cancellationToken)
    {
        SetNoStoreHeader();
        if (passkeyService is null)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Passkey management is unavailable.",
                type: "https://httpstatuses.com/503");
        }

        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var result = await passkeyService.RenameAsync(userId, credentialId, request?.Name, cancellationToken);
        return result.Status switch
        {
            PasskeyOperationStatus.Succeeded => NoContent(),
            PasskeyOperationStatus.PasskeyNotFound => PasskeyNotFound(),
            PasskeyOperationStatus.InvalidCredentialId => ValidationProblemResult(
                "Passkey data is invalid.",
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["credentialId"] = ["A valid credential id is required."]
                }),
            PasskeyOperationStatus.InvalidName => ValidationProblemResult(
                "Passkey data is invalid.",
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["name"] = ["A passkey name between 1 and 100 characters is required."]
                }),
            _ => AuthenticationRequired()
        };
    }

    [HttpDelete("{credentialId}", Name = "RemoveAccountPasskey")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveAsync(
        string credentialId,
        CancellationToken cancellationToken)
    {
        SetNoStoreHeader();
        if (passkeyService is null)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Passkey management is unavailable.",
                type: "https://httpstatuses.com/503");
        }

        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var result = await passkeyService.RemoveAsync(userId, credentialId, cancellationToken);
        return result.Status switch
        {
            PasskeyOperationStatus.Succeeded => NoContent(),
            PasskeyOperationStatus.PasskeyNotFound => PasskeyNotFound(),
            PasskeyOperationStatus.InvalidCredentialId => ValidationProblemResult(
                "Passkey data is invalid.",
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["credentialId"] = ["A valid credential id is required."]
                }),
            _ => AuthenticationRequired()
        };
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId != Guid.Empty;

    private IActionResult AuthenticationRequired() =>
        Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Authentication is required.",
            type: "https://httpstatuses.com/401");

    private IActionResult PasskeyNotFound() =>
        Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "The passkey was not found.",
            type: "https://httpstatuses.com/404");

    private IActionResult ValidationProblemResult(
        string title,
        IReadOnlyDictionary<string, string[]> errors)
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
            title: title,
            type: "https://httpstatuses.com/400",
            modelStateDictionary: ModelState);
    }

    private void SetNoStoreHeader() => Response.Headers.CacheControl = "no-store";
}

public sealed record RegisterPasskeyRequest(string? CredentialJson);

public sealed record RenamePasskeyRequest(string? Name);

public sealed record AccountPasskeyListResponse(IEnumerable<AccountPasskeyResponse> Passkeys);

public sealed record AccountPasskeyResponse(
    string CredentialId,
    string? Name,
    DateTimeOffset CreatedAt,
    IReadOnlyCollection<string> Transports,
    bool IsUserVerified,
    bool IsBackupEligible,
    bool IsBackedUp);
