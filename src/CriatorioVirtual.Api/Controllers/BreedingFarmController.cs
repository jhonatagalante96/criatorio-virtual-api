using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/breeding-farms")]
[Authorize]
public sealed class BreedingFarmController(ICommandExecutor commandExecutor) : ControllerBase
{
    [HttpPost(Name = "CreateBreedingFarm")]
    [ProducesResponseType(typeof(CreateBreedingFarmResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateBreedingFarmRequest? request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401");
        }

        if (request is null)
        {
            return InvalidRequest("The request body is required.");
        }

        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return ValidationProblemResult(errors);
        }

        try
        {
            var result = await commandExecutor.Execute<CreateBreedingFarmCommand, CreateBreedingFarmResult>(
                new CreateBreedingFarmCommand(
                    userId,
                    request.Name,
                    request.ResponsibleName,
                    request.ContactEmail,
                    request.ContactPhone,
                    request.OfficialRegistrationNumber,
                    request.Address is null
                        ? null
                        : new BreedingFarmAddressInput(
                            request.Address.Street,
                            request.Address.Number,
                            request.Address.Complement,
                            request.Address.Neighborhood,
                            request.Address.City,
                            request.Address.State,
                            request.Address.PostalCode)),
                cancellationToken);

            return result.Status switch
            {
                CreateBreedingFarmStatus.Created => Created(
                    $"/api/breeding-farms/{result.BreedingFarmId}",
                    new CreateBreedingFarmResponse(result.BreedingFarmId!.Value, result.OwnerUserId!.Value)),
                CreateBreedingFarmStatus.AccountNotFound => Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Authentication is required.",
                    type: "https://httpstatuses.com/401"),
                CreateBreedingFarmStatus.DuplicateOfficialRegistration => Conflict(
                    new ProblemDetails
                    {
                        Status = StatusCodes.Status409Conflict,
                        Title = "The official registration number is already in use.",
                        Type = "https://httpstatuses.com/409"
                    }),
                _ => throw new InvalidOperationException("The breeding farm creation result is not supported.")
            };
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict(
                new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "The official registration number is already in use.",
                    Type = "https://httpstatuses.com/409"
                });
        }
    }

    private static Dictionary<string, string[]> Validate(CreateBreedingFarmRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors[nameof(request.Name)] = ["A breeding farm name is required."];
        }
        else if (request.Name.Trim().Length > 200)
        {
            errors[nameof(request.Name)] = ["A breeding farm name cannot exceed 200 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.ResponsibleName))
        {
            errors[nameof(request.ResponsibleName)] = ["A responsible person name is required."];
        }
        else if (request.ResponsibleName.Trim().Length > 200)
        {
            errors[nameof(request.ResponsibleName)] = ["A responsible person name cannot exceed 200 characters."];
        }

        if (!string.IsNullOrWhiteSpace(request.ContactEmail) &&
            (!new EmailAddressAttribute().IsValid(request.ContactEmail.Trim()) || request.ContactEmail.Trim().Length > 320))
        {
            errors[nameof(request.ContactEmail)] = ["A valid contact email address is required."];
        }

        AddMaxLengthError(errors, nameof(request.ContactPhone), request.ContactPhone, 32);
        AddMaxLengthError(errors, nameof(request.OfficialRegistrationNumber), request.OfficialRegistrationNumber, 100);
        if (request.Address is not null)
        {
            AddMaxLengthError(errors, $"{nameof(request.Address)}.{nameof(request.Address.Street)}", request.Address.Street, 200);
            AddMaxLengthError(errors, $"{nameof(request.Address)}.{nameof(request.Address.Number)}", request.Address.Number, 32);
            AddMaxLengthError(errors, $"{nameof(request.Address)}.{nameof(request.Address.Complement)}", request.Address.Complement, 100);
            AddMaxLengthError(errors, $"{nameof(request.Address)}.{nameof(request.Address.Neighborhood)}", request.Address.Neighborhood, 120);
            AddMaxLengthError(errors, $"{nameof(request.Address)}.{nameof(request.Address.City)}", request.Address.City, 120);
            AddMaxLengthError(errors, $"{nameof(request.Address)}.{nameof(request.Address.State)}", request.Address.State, 100);
            AddMaxLengthError(errors, $"{nameof(request.Address)}.{nameof(request.Address.PostalCode)}", request.Address.PostalCode, 20);
        }

        return errors;
    }

    private static void AddMaxLengthError(
        IDictionary<string, string[]> errors,
        string key,
        string? value,
        int maxLength)
    {
        if (value?.Trim().Length > maxLength)
        {
            errors[key] = [$"The value cannot exceed {maxLength} characters."];
        }
    }

    private IActionResult InvalidRequest(string detail) =>
        Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Breeding farm data is invalid.",
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
            title: "Breeding farm data is invalid.",
            type: "https://httpstatuses.com/400",
            modelStateDictionary: ModelState);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } ||
        exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

public sealed record CreateBreedingFarmRequest(
    string? Name,
    string? ResponsibleName,
    string? ContactEmail,
    string? ContactPhone,
    string? OfficialRegistrationNumber,
    BreedingFarmAddressRequest? Address);

public sealed record BreedingFarmAddressRequest(
    string? Street,
    string? Number,
    string? Complement,
    string? Neighborhood,
    string? City,
    string? State,
    string? PostalCode);

public sealed record CreateBreedingFarmResponse(Guid BreedingFarmId, Guid OwnerUserId);
