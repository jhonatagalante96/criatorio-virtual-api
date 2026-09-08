using System.ComponentModel.DataAnnotations;
using CriatorioVirtual.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AccountEmailConfirmationController(
    IAccountEmailConfirmationService? confirmationService = null)
    : ControllerBase
{
    [HttpPost("confirm-email", Name = "ConfirmAccountEmail")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ConfirmAsync(
        [FromBody] ConfirmEmailRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            !Guid.TryParse(request.UserId, out var userId) ||
            string.IsNullOrWhiteSpace(request.Token))
        {
            return InvalidConfirmation();
        }

        if (confirmationService is null)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Email confirmation is unavailable.",
                type: "https://httpstatuses.com/503");
        }

        var result = await confirmationService.ConfirmAsync(userId, request.Token, cancellationToken);
        return result.Status is EmailConfirmationStatus.Confirmed or EmailConfirmationStatus.AlreadyConfirmed
            ? NoContent()
            : InvalidConfirmation();
    }

    [HttpPost("confirm-email/resend", Name = "ResendAccountEmailConfirmation")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ResendAsync(
        [FromBody] ResendEmailConfirmationRequest? request,
        CancellationToken cancellationToken)
    {
        var email = request?.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email))
        {
            return ValidationProblem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Email confirmation data is invalid.",
                type: "https://httpstatuses.com/400");
        }

        if (confirmationService is null)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Email confirmation is unavailable.",
                type: "https://httpstatuses.com/503");
        }

        var result = await confirmationService.ResendAsync(email, cancellationToken);
        return result.Status switch
        {
            EmailConfirmationResendStatus.Accepted => NoContent(),
            EmailConfirmationResendStatus.RateLimited => Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Please wait before requesting another confirmation email.",
                type: "https://httpstatuses.com/429"),
            EmailConfirmationResendStatus.DeliveryFailed => Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Email confirmation is temporarily unavailable.",
                type: "https://httpstatuses.com/503"),
            _ => Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Email confirmation is temporarily unavailable.",
                type: "https://httpstatuses.com/503")
        };
    }

    private IActionResult InvalidConfirmation() =>
        Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "The email confirmation link is invalid or expired.",
            type: "https://httpstatuses.com/400");
}

public sealed record ConfirmEmailRequest(string? UserId, string? Token);

public sealed record ResendEmailConfirmationRequest(string? Email);
