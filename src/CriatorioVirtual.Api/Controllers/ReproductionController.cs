using System.Security.Claims;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Reproductions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/reproductions")]
[Authorize]
public sealed class ReproductionController(ICommandExecutor commandExecutor) : ControllerBase
{
    [HttpPost(Name = "CreateReproduction")]
    [ProducesResponseType(typeof(ReproductionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateReproductionRequest? request,
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

        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return ValidationProblemResult(errors);
        }

        var result = await commandExecutor.Execute<CreateReproductionCommand, CreateReproductionResult>(
            new CreateReproductionCommand(
                userId,
                request.MaleBirdId!.Value,
                request.FemaleBirdId!.Value,
                request.StartDate!.Value,
                request.EndDate,
                request.Notes),
            cancellationToken);

        return result.Status switch
        {
            CreateReproductionStatus.Created => Created(
                $"/api/reproductions/{result.Reproduction!.ReproductionId}",
                ToResponse(result.Reproduction)),
            CreateReproductionStatus.UserNotFound => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401"),
            CreateReproductionStatus.BreedingFarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A breeding farm must be selected before registering a reproduction.",
                type: "https://httpstatuses.com/409"),
            CreateReproductionStatus.BreedingFarmNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The selected breeding farm was not found.",
                type: "https://httpstatuses.com/404"),
            CreateReproductionStatus.BirdNotFound => ValidationProblemResult(
                new Dictionary<string, string[]>
                {
                    ["birds"] = ["The male and female birds must belong to the selected breeding farm."]
                }),
            CreateReproductionStatus.BirdNotEligible => ValidationProblemResult(
                new Dictionary<string, string[]>
                {
                    ["birds"] = ["Both birds must be active and have a valid ring number."]
                }),
            CreateReproductionStatus.MaleBirdSexInvalid => ValidationProblemResult(
                new Dictionary<string, string[]>
                {
                    [nameof(request.MaleBirdId)] = ["The selected male bird must have Male sex."]
                }),
            CreateReproductionStatus.FemaleBirdSexInvalid => ValidationProblemResult(
                new Dictionary<string, string[]>
                {
                    [nameof(request.FemaleBirdId)] = ["The selected female bird must have Female sex."]
                }),
            CreateReproductionStatus.InvalidData => ValidationProblemResult(
                new Dictionary<string, string[]>
                {
                    ["request"] = ["The reproduction data is invalid."]
                }),
            _ => throw new InvalidOperationException("The reproduction creation result is not supported.")
        };
    }

    private IActionResult ValidationProblemResult(
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
            title: "Reproduction data is invalid.",
            type: "https://httpstatuses.com/400",
            modelStateDictionary: ModelState);
    }

    private static Dictionary<string, string[]> Validate(CreateReproductionRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (request.MaleBirdId is null || request.MaleBirdId == Guid.Empty)
        {
            errors[nameof(request.MaleBirdId)] = ["A male bird is required."];
        }

        if (request.FemaleBirdId is null || request.FemaleBirdId == Guid.Empty)
        {
            errors[nameof(request.FemaleBirdId)] = ["A female bird is required."];
        }

        if (request.MaleBirdId is not null &&
            request.MaleBirdId != Guid.Empty &&
            request.MaleBirdId == request.FemaleBirdId)
        {
            errors["birds"] = ["The same bird cannot be both parents."];
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (request.StartDate is null)
        {
            errors[nameof(request.StartDate)] = ["A reproduction start date is required."];
        }
        else if (request.StartDate > today)
        {
            errors[nameof(request.StartDate)] = ["The reproduction start date cannot be in the future."];
        }

        if (request.EndDate is not null &&
            request.StartDate is not null &&
            request.EndDate < request.StartDate)
        {
            errors[nameof(request.EndDate)] = ["The reproduction end date cannot be before its start date."];
        }

        if (request.Notes?.Trim().Length > 2000)
        {
            errors[nameof(request.Notes)] = ["Reproduction notes cannot exceed 2000 characters."];
        }

        return errors;
    }

    private static ReproductionResponse ToResponse(ReproductionResult result) =>
        new(
            result.ReproductionId,
            result.BreedingFarmId,
            result.MaleBirdId,
            result.FemaleBirdId,
            result.StartDate,
            result.EndDate,
            result.Notes,
            result.Status.ToString(),
            result.CreatedAtUtc,
            result.UpdatedAtUtc);
}

public sealed record CreateReproductionRequest(
    Guid? MaleBirdId,
    Guid? FemaleBirdId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Notes);

public sealed record ReproductionResponse(
    Guid ReproductionId,
    Guid BreedingFarmId,
    Guid MaleBirdId,
    Guid FemaleBirdId,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Notes,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
