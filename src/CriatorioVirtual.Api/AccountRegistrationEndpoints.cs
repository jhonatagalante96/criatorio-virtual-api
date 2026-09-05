using System.ComponentModel.DataAnnotations;
using CriatorioVirtual.Infrastructure.Identity;

namespace CriatorioVirtual.Api;

public static class AccountRegistrationEndpoints
{
    public static IEndpointRouteBuilder MapAccountRegistration(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/api/auth/register", RegisterAsync)
            .AllowAnonymous()
            .WithName("RegisterAccount")
            .Produces<AccountRegistrationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterAccountRequest? request,
        IServiceProvider serviceProvider,
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
            return Results.ValidationProblem(errors, statusCode: StatusCodes.Status400BadRequest, title: "Registration data is invalid.");
        }

        var registrationService = serviceProvider.GetService<AccountRegistrationService>();
        if (registrationService is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Account registration is unavailable.",
                type: "https://httpstatuses.com/503");
        }

        var result = await registrationService.RegisterAsync(email!, request.Password!, cancellationToken);
        return result.Status switch
        {
            AccountRegistrationStatus.Created => Results.Created(
                $"/api/auth/accounts/{result.User!.Id}",
                new AccountRegistrationResponse(result.User.Id, result.User.Email!, EmailConfirmationRequired: true)),
            AccountRegistrationStatus.Duplicate => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "An account with this email already exists.",
                type: "https://httpstatuses.com/409"),
            _ => InvalidIdentityResult(result.Errors)
        };
    }

    private static IResult InvalidRequest(string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Registration data is invalid.",
            detail: detail,
            type: "https://httpstatuses.com/400");

    private static IResult InvalidIdentityResult(IEnumerable<Microsoft.AspNetCore.Identity.IdentityError> identityErrors)
    {
        var errors = identityErrors
            .GroupBy(error => error.Code.StartsWith("Password", StringComparison.Ordinal) ? "password" : "email")
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Description).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return Results.ValidationProblem(
            errors,
            statusCode: StatusCodes.Status400BadRequest,
            title: "Registration data is invalid.");
    }
}

public sealed record RegisterAccountRequest(string? Email, string? Password);

public sealed record AccountRegistrationResponse(Guid UserId, string Email, bool EmailConfirmationRequired);
