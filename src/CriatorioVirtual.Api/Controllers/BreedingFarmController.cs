using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.BreedingFarms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/breeding-farms")]
[Authorize]
public sealed class BreedingFarmController(
    ICommandExecutor commandExecutor,
    IQueryExecutor queryExecutor) : ControllerBase
{
    [HttpGet(Name = "ListBreedingFarms")]
    [ProducesResponseType(typeof(BreedingFarmSelectionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401");
        }

        var result = await queryExecutor.Execute<ListBreedingFarmsQuery, BreedingFarmSelectionResult>(
            new ListBreedingFarmsQuery(userId),
            cancellationToken);

        return Ok(ToResponse(result));
    }

    [HttpPut("selection", Name = "SelectBreedingFarm")]
    [ProducesResponseType(typeof(BreedingFarmSelectionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SelectAsync(
        [FromBody] SelectBreedingFarmRequest? request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401");
        }

        if (request is null || request.BreedingFarmId == Guid.Empty)
        {
            return InvalidRequest("A breeding farm identifier is required.");
        }

        var result = await commandExecutor.Execute<SelectBreedingFarmCommand, SelectBreedingFarmResult>(
            new SelectBreedingFarmCommand(userId, request.BreedingFarmId),
            cancellationToken);

        return result.Status switch
        {
            SelectBreedingFarmStatus.Selected => Ok(ToResponse(result.Selection!)),
            SelectBreedingFarmStatus.NotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The breeding farm was not found.",
                type: "https://httpstatuses.com/404"),
            _ => throw new InvalidOperationException("The breeding farm selection result is not supported.")
        };
    }

    [HttpGet("{breedingFarmId:guid}/settings", Name = "GetBreedingFarmSettings")]
    [ProducesResponseType(typeof(BreedingFarmSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSettingsAsync(
        Guid breedingFarmId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401");
        }

        var result = await queryExecutor.Execute<GetBreedingFarmSettingsQuery, BreedingFarmSettingsResult?>(
            new GetBreedingFarmSettingsQuery(userId, breedingFarmId),
            cancellationToken);

        return result is null
            ? NotFoundResult()
            : Ok(ToResponse(result));
    }

    [HttpPut("{breedingFarmId:guid}/settings", Name = "UpdateBreedingFarmSettings")]
    [ProducesResponseType(typeof(BreedingFarmSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateSettingsAsync(
        Guid breedingFarmId,
        [FromBody] UpdateBreedingFarmSettingsRequest? request,
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

        var errors = ValidateUpdate(request);
        if (errors.Count > 0)
        {
            return ValidationProblemResult(errors);
        }

        try
        {
            var result = await commandExecutor.Execute<UpdateBreedingFarmSettingsCommand, UpdateBreedingFarmSettingsResult>(
                new UpdateBreedingFarmSettingsCommand(
                    userId,
                    breedingFarmId,
                    request.Name!,
                    request.ResponsibleName!,
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
                UpdateBreedingFarmSettingsStatus.Updated => Ok(ToResponse(result.Settings!)),
                UpdateBreedingFarmSettingsStatus.NotFound => NotFoundResult(),
                UpdateBreedingFarmSettingsStatus.DuplicateOfficialRegistration => DuplicateRegistrationConflict(),
                _ => throw new InvalidOperationException("The breeding farm settings update result is not supported.")
            };
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(
                new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "The breeding farm was changed by another request. Reload its settings and try again.",
                    Type = "https://httpstatuses.com/409"
                });
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return DuplicateRegistrationConflict();
        }
    }

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
            AddAddressValidation(errors, request.Address);
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateUpdate(UpdateBreedingFarmSettingsRequest request)
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
            AddAddressValidation(errors, request.Address);
        }

        return errors;
    }

    private static void AddAddressValidation(
        IDictionary<string, string[]> errors,
        BreedingFarmAddressRequest address)
    {
        AddMaxLengthError(errors, $"Address.{nameof(address.Street)}", address.Street, 200);
        AddMaxLengthError(errors, $"Address.{nameof(address.Number)}", address.Number, 32);
        AddMaxLengthError(errors, $"Address.{nameof(address.Complement)}", address.Complement, 100);
        AddMaxLengthError(errors, $"Address.{nameof(address.Neighborhood)}", address.Neighborhood, 120);
        AddMaxLengthError(errors, $"Address.{nameof(address.City)}", address.City, 120);
        AddMaxLengthError(errors, $"Address.{nameof(address.State)}", address.State, 100);
        AddMaxLengthError(errors, $"Address.{nameof(address.PostalCode)}", address.PostalCode, 20);

        var state = address.State?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(state) &&
            (state.Length != 2 || state.Any(character => character is < 'A' or > 'Z')))
        {
            errors[$"Address.{nameof(address.State)}"] = ["The state must contain exactly two letters."];
        }

        var postalCode = address.PostalCode?.Trim();
        if (!string.IsNullOrEmpty(postalCode) &&
            (postalCode.Any(character => !char.IsDigit(character) && character is not '-' and not ' ') ||
             postalCode.Count(char.IsDigit) != 8))
        {
            errors[$"Address.{nameof(address.PostalCode)}"] = ["The postal code must contain eight digits and may use separators."];
        }
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

    private IActionResult NotFoundResult() =>
        Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "The breeding farm was not found.",
            type: "https://httpstatuses.com/404");

    private IActionResult DuplicateRegistrationConflict() =>
        Conflict(
            new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The official registration number is already in use.",
                Type = "https://httpstatuses.com/409"
            });

    private static BreedingFarmSettingsResponse ToResponse(BreedingFarmSettingsResult result) =>
        new(
            result.BreedingFarmId,
            result.Name,
            result.ResponsibleName,
            result.ContactEmail,
            result.ContactPhone,
            result.OfficialRegistrationNumber,
            new BreedingFarmAddressResponse(
                result.Address.Street,
                result.Address.Number,
                result.Address.Complement,
                result.Address.Neighborhood,
                result.Address.City,
                result.Address.State,
                result.Address.PostalCode),
            result.UpdatedAtUtc);

    private static BreedingFarmSelectionResponse ToResponse(BreedingFarmSelectionResult result) =>
        new(
            result.SelectedBreedingFarmId,
            result.BreedingFarms
                .Select(farm => new BreedingFarmSummaryResponse(
                    farm.BreedingFarmId,
                    farm.Name,
                    farm.ResponsibleName,
                    farm.Role,
                    farm.IsSelected))
                .ToArray());

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

public sealed record SelectBreedingFarmRequest(Guid BreedingFarmId);

public sealed record BreedingFarmSelectionResponse(
    Guid? SelectedBreedingFarmId,
    IReadOnlyCollection<BreedingFarmSummaryResponse> BreedingFarms);

public sealed record BreedingFarmSummaryResponse(
    Guid BreedingFarmId,
    string Name,
    string ResponsibleName,
    BreedingFarmRole Role,
    bool IsSelected);

public sealed record UpdateBreedingFarmSettingsRequest(
    string? Name,
    string? ResponsibleName,
    string? ContactEmail,
    string? ContactPhone,
    string? OfficialRegistrationNumber,
    BreedingFarmAddressRequest? Address);

public sealed record BreedingFarmSettingsResponse(
    Guid BreedingFarmId,
    string Name,
    string ResponsibleName,
    string ContactEmail,
    string? ContactPhone,
    string? OfficialRegistrationNumber,
    BreedingFarmAddressResponse Address,
    DateTimeOffset UpdatedAtUtc);

public sealed record BreedingFarmAddressResponse(
    string? Street,
    string? Number,
    string? Complement,
    string? Neighborhood,
    string? City,
    string? State,
    string? PostalCode);
