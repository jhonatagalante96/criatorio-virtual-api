using System.Security.Claims;
using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/birds")]
[Authorize]
public sealed class BirdController(ICommandExecutor commandExecutor) : ControllerBase
{
    [HttpPost(Name = "CreateBird")]
    [ProducesResponseType(typeof(BirdResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateBirdRequest? request,
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
            return ValidationProblemResult(new Dictionary<string, string[]>
            {
                ["request"] = ["The request body is required."]
            });
        }

        var errors = Validate(request, out var sex);
        if (errors.Count > 0)
        {
            return ValidationProblemResult(errors);
        }

        try
        {
            var result = await commandExecutor.Execute<CreateBirdCommand, CreateBirdResult>(
                new CreateBirdCommand(
                    userId,
                    request.Name,
                    sex,
                    request.SpeciesId,
                    request.BirthDate,
                    request.RingNumber,
                    request.FatherBirdId,
                    request.ExternalFatherName,
                    request.MotherBirdId,
                    request.ExternalMotherName,
                    request.Notes),
                cancellationToken);

            return result.Status switch
            {
                CreateBirdStatus.Created => Created(
                    $"/api/birds/{result.Bird!.BirdId}",
                    ToResponse(result.Bird)),
                CreateBirdStatus.UserNotFound => Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Authentication is required.",
                    type: "https://httpstatuses.com/401"),
                CreateBirdStatus.BreedingFarmNotSelected => Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "A breeding farm must be selected before registering a bird.",
                    type: "https://httpstatuses.com/409"),
                CreateBirdStatus.BreedingFarmNotFound => Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "The selected breeding farm was not found.",
                    type: "https://httpstatuses.com/404"),
                CreateBirdStatus.SpeciesNotFound => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        [nameof(request.SpeciesId)] = ["The species must exist and be active."]
                    }),
                CreateBirdStatus.ParentNotFound => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        ["parent"] = ["A linked parent must belong to the selected breeding farm."]
                    }),
                CreateBirdStatus.ParentSexInvalid => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        ["parent"] = ["The father must be male and the mother must be female."]
                    }),
                CreateBirdStatus.DuplicateParent => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        ["parent"] = ["The same bird cannot be both parents."]
                    }),
                CreateBirdStatus.DuplicateRingNumber => Conflict(
                    new ProblemDetails
                    {
                        Status = StatusCodes.Status409Conflict,
                        Title = "The ring number is already in use.",
                        Type = "https://httpstatuses.com/409"
                    }),
                _ => throw new InvalidOperationException("The bird creation result is not supported.")
            };
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict(
                new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "The ring number is already in use.",
                    Type = "https://httpstatuses.com/409"
                });
        }
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
            title: "Bird data is invalid.",
            type: "https://httpstatuses.com/400",
            modelStateDictionary: ModelState);
    }

    private static Dictionary<string, string[]> Validate(CreateBirdRequest request, out BirdSex? sex)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        sex = null;
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors[nameof(request.Name)] = ["A bird name is required."];
        }
        else if (request.Name.Trim().Length > 100)
        {
            errors[nameof(request.Name)] = ["A bird name cannot exceed 100 characters."];
        }

        var sexValue = request.Sex?.Trim();
        if (!Enum.TryParse<BirdSex>(sexValue, ignoreCase: true, out var parsedSex) ||
            !Enum.IsDefined(typeof(BirdSex), parsedSex) ||
            !string.Equals(parsedSex.ToString(), sexValue, StringComparison.OrdinalIgnoreCase))
        {
            errors[nameof(request.Sex)] = ["A valid bird sex is required."];
        }
        else
        {
            sex = parsedSex;
        }

        if (request.SpeciesId is null || request.SpeciesId == Guid.Empty)
        {
            errors[nameof(request.SpeciesId)] = ["An active species is required."];
        }

        if (request.BirthDate > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            errors[nameof(request.BirthDate)] = ["The birth date cannot be in the future."];
        }

        var ringNumber = request.RingNumber?.Trim();
        if (!string.IsNullOrWhiteSpace(ringNumber) &&
            (ringNumber.Length != 6 || ringNumber.Any(character => !char.IsAsciiDigit(character))))
        {
            errors[nameof(request.RingNumber)] = ["The ring number must contain exactly six digits."];
        }

        AddMaxLengthError(errors, nameof(request.ExternalFatherName), request.ExternalFatherName, 200);
        AddMaxLengthError(errors, nameof(request.ExternalMotherName), request.ExternalMotherName, 200);
        AddMaxLengthError(errors, nameof(request.Notes), request.Notes, 2000);
        if (request.FatherBirdId == Guid.Empty)
        {
            errors[nameof(request.FatherBirdId)] = ["The father bird identifier cannot be empty."];
        }

        if (request.MotherBirdId == Guid.Empty)
        {
            errors[nameof(request.MotherBirdId)] = ["The mother bird identifier cannot be empty."];
        }

        if (request.FatherBirdId is not null && !string.IsNullOrWhiteSpace(request.ExternalFatherName))
        {
            errors["father"] = ["The father must be linked to a bird or represented by an external name, not both."];
        }

        if (request.MotherBirdId is not null && !string.IsNullOrWhiteSpace(request.ExternalMotherName))
        {
            errors["mother"] = ["The mother must be linked to a bird or represented by an external name, not both."];
        }

        if (request.FatherBirdId is not null && request.FatherBirdId == request.MotherBirdId)
        {
            errors["parent"] = ["The same bird cannot be both parents."];
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

    private static BirdResponse ToResponse(BirdResult result) =>
        new(
            result.BirdId,
            result.GenealogyRootId,
            result.BreedingFarmId,
            result.Name,
            result.SpeciesId,
            result.Sex.ToString(),
            result.BirthDate,
            result.DeathDate,
            result.RingNumber,
            result.FatherBirdId,
            result.ExternalFatherName,
            result.MotherBirdId,
            result.ExternalMotherName,
            result.Notes,
            result.Status.ToString(),
            result.IdentificationPending,
            result.AgeInYears,
            result.CreatedAtUtc);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } ||
        exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

public sealed record CreateBirdRequest(
    string? Name,
    string? Sex,
    Guid? SpeciesId,
    DateOnly? BirthDate,
    string? RingNumber,
    Guid? FatherBirdId,
    string? ExternalFatherName,
    Guid? MotherBirdId,
    string? ExternalMotherName,
    string? Notes);

public sealed record BirdResponse(
    Guid BirdId,
    Guid GenealogyRootId,
    Guid BreedingFarmId,
    string Name,
    Guid SpeciesId,
    string Sex,
    DateOnly? BirthDate,
    DateOnly? DeathDate,
    string? RingNumber,
    Guid? FatherBirdId,
    string? ExternalFatherName,
    Guid? MotherBirdId,
    string? ExternalMotherName,
    string? Notes,
    string Status,
    bool IdentificationPending,
    int? AgeInYears,
    DateTimeOffset CreatedAtUtc);
