using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CriatorioVirtual.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AccountPasswordController(
    IAccountPasswordService? passwordService = null)
    : ControllerBase
{
    [HttpPost("forgot-password", Name = "RequestPasswordReset")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RequestResetAsync(
        [FromBody] ForgotPasswordRequest? request,
        CancellationToken cancellationToken)
    {
        var email = request?.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email))
        {
            return ValidationProblemResult(
                "Password recovery data is invalid.",
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["email"] = ["A valid email address is required."]
                });
        }

        if (passwordService is null)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Password recovery is unavailable.",
                type: "https://httpstatuses.com/503");
        }

        await passwordService.RequestResetAsync(email, cancellationToken);
        return NoContent();
    }

    [HttpPost("reset-password", Name = "ResetAccountPassword")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ResetAsync(
        [FromBody] ResetPasswordRequest? request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var userId = Guid.Empty;
        if (request is null || !Guid.TryParse(request.UserId, out userId) || userId == Guid.Empty)
        {
            errors["userId"] = ["A valid user id is required."];
        }

        if (string.IsNullOrWhiteSpace(request?.Token))
        {
            errors["token"] = ["A password reset token is required."];
        }

        if (string.IsNullOrEmpty(request?.NewPassword))
        {
            errors["newPassword"] = ["A new password is required."];
        }

        if (string.IsNullOrEmpty(request?.ConfirmPassword))
        {
            errors["confirmPassword"] = ["Password confirmation is required."];
        }

        if (errors.Count > 0)
        {
            return ValidationProblemResult("Password reset data is invalid.", errors);
        }

        if (passwordService is null)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Password recovery is unavailable.",
                type: "https://httpstatuses.com/503");
        }

        var result = await passwordService.ResetAsync(
            userId,
            request!.Token,
            request.NewPassword,
            request.ConfirmPassword,
            cancellationToken);

        return result.Status switch
        {
            PasswordResetStatus.Succeeded => NoContent(),
            PasswordResetStatus.InvalidToken => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The password reset link is invalid, expired, or already used.",
                type: "https://httpstatuses.com/400"),
            PasswordResetStatus.ConfirmationMismatch => ValidationProblemResult(
                "Password reset data is invalid.",
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["confirmPassword"] = ["Password confirmation does not match the new password."]
                }),
            PasswordResetStatus.InvalidPassword => ValidationProblemResult(
                "Password reset data is invalid.",
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["newPassword"] = ["The new password does not meet the password policy."]
                }),
            _ => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The password reset request is invalid.",
                type: "https://httpstatuses.com/400")
        };
    }

    [HttpPost("change-password", Name = "ChangeAccountPassword")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ChangeAsync(
        [FromBody] ChangePasswordRequest? request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (request is null || string.IsNullOrEmpty(request.CurrentPassword))
        {
            errors["currentPassword"] = ["The current password is required."];
        }

        if (string.IsNullOrEmpty(request?.NewPassword))
        {
            errors["newPassword"] = ["A new password is required."];
        }

        if (string.IsNullOrEmpty(request?.ConfirmPassword))
        {
            errors["confirmPassword"] = ["Password confirmation is required."];
        }

        if (errors.Count > 0)
        {
            return ValidationProblemResult("Password change data is invalid.", errors);
        }

        if (passwordService is null)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Password management is unavailable.",
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

        var result = await passwordService.ChangeAsync(
            userId,
            request!.CurrentPassword,
            request.NewPassword,
            request.ConfirmPassword,
            cancellationToken);

        return result.Status switch
        {
            PasswordChangeStatus.Succeeded => NoContent(),
            PasswordChangeStatus.InvalidCurrentPassword => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The current password is invalid.",
                type: "https://httpstatuses.com/400"),
            PasswordChangeStatus.ConfirmationMismatch => ValidationProblemResult(
                "Password change data is invalid.",
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["confirmPassword"] = ["Password confirmation does not match the new password."]
                }),
            PasswordChangeStatus.InvalidPassword => ValidationProblemResult(
                "Password change data is invalid.",
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["newPassword"] = ["The new password does not meet the password policy."]
                }),
            PasswordChangeStatus.NoLocalPassword => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "A local password is not configured for this account.",
                type: "https://httpstatuses.com/400"),
            PasswordChangeStatus.UserNotFound => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401"),
            _ => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The password change request is invalid.",
                type: "https://httpstatuses.com/400")
        };
    }

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
}

public sealed record ForgotPasswordRequest(string? Email);

public sealed record ResetPasswordRequest(
    string? UserId,
    string? Token,
    string? NewPassword,
    string? ConfirmPassword);

public sealed record ChangePasswordRequest(
    string? CurrentPassword,
    string? NewPassword,
    string? ConfirmPassword);
