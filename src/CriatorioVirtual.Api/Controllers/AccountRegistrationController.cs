using System.ComponentModel.DataAnnotations;
using CriatorioVirtual.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AccountRegistrationController(IServiceProvider serviceProvider) : ControllerBase
{
    [HttpPost("register", Name = "RegisterAccount")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AccountRegistrationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RegisterAsync(
        [FromBody] RegisterAccountRequest? request,
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

        var registrationService = serviceProvider.GetService<AccountRegistrationService>();
        if (registrationService is null)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Account registration is unavailable.",
                type: "https://httpstatuses.com/503");
        }

        var result = await registrationService.RegisterAsync(email!, request.Password!, cancellationToken);
        return result.Status switch
        {
            AccountRegistrationStatus.Created => Created(
                $"/api/auth/accounts/{result.User!.Id}",
                new AccountRegistrationResponse(result.User.Id, result.User.Email!, EmailConfirmationRequired: true)),
            AccountRegistrationStatus.Duplicate => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "An account with this email already exists.",
                type: "https://httpstatuses.com/409"),
            AccountRegistrationStatus.EmailDeliveryFailed => Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Account registration is temporarily unavailable.",
                type: "https://httpstatuses.com/503"),
            _ => InvalidIdentityResult(result.Errors)
        };
    }

    private IActionResult InvalidRequest(string detail) =>
        Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Registration data is invalid.",
            detail: detail,
            type: "https://httpstatuses.com/400");

    private IActionResult InvalidIdentityResult(IEnumerable<Microsoft.AspNetCore.Identity.IdentityError> identityErrors)
    {
        var errors = identityErrors
            .GroupBy(error => error.Code.StartsWith("Password", StringComparison.Ordinal) ? "password" : "email")
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Description).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return ValidationProblemResult(errors);
    }

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
            title: "Registration data is invalid.",
            type: "https://httpstatuses.com/400",
            modelStateDictionary: ModelState);
    }
}

public sealed record RegisterAccountRequest(string? Email, string? Password);

public sealed record AccountRegistrationResponse(Guid UserId, string Email, bool EmailConfirmationRequired);
