using System.Security.Claims;
using CriatorioVirtual.Application.Competitions;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Competitions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/birds/{birdId:guid}/competitions")]
[Authorize]
public sealed class BirdCompetitionController(ICommandExecutor commandExecutor) : ControllerBase
{
    [HttpPost(Name = "CreateBirdCompetition")]
    [ProducesResponseType(typeof(BirdCompetitionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        Guid birdId,
        [FromBody] CreateBirdCompetitionRequest? request,
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
            return ValidationProblemResult(
                new Dictionary<string, string[]>
                {
                    ["request"] = ["The request body is required."]
                });
        }

        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return ValidationProblemResult(errors);
        }

        var result = await commandExecutor.Execute<
            CreateBirdCompetitionCommand,
            CreateBirdCompetitionResult>(
            new CreateBirdCompetitionCommand(
                userId,
                birdId,
                request.Name,
                request.Date,
                request.Category,
                request.Placement,
                request.Location,
                request.Notes),
            cancellationToken);

        return result.Status switch
        {
            CreateBirdCompetitionStatus.Created => Created(
                $"/api/birds/{birdId}/competitions/{result.Competition!.CompetitionId}",
                ToResponse(result.Competition)),
            CreateBirdCompetitionStatus.UserNotFound => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401"),
            CreateBirdCompetitionStatus.BreedingFarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A breeding farm must be selected before registering a competition.",
                type: "https://httpstatuses.com/409"),
            CreateBirdCompetitionStatus.BreedingFarmNotFound or
                CreateBirdCompetitionStatus.BirdNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The bird was not found.",
                type: "https://httpstatuses.com/404"),
            CreateBirdCompetitionStatus.InvalidData => ValidationProblemResult(
                new Dictionary<string, string[]>
                {
                    ["request"] = ["The competition data is invalid."]
                },
                "Competition data is invalid."),
            _ => throw new InvalidOperationException("The bird competition creation result is not supported.")
        };
    }

    private IActionResult ValidationProblemResult(
        IReadOnlyDictionary<string, string[]> errors,
        string title = "Competition data is invalid.")
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

    private static Dictionary<string, string[]> Validate(CreateBirdCompetitionRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors[nameof(request.Name)] = ["A competition name is required."];
        }
        else if (request.Name.Trim().Length > BirdCompetition.NameMaxLength)
        {
            errors[nameof(request.Name)] = ["The competition name cannot exceed 200 characters."];
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (request.Date > today)
        {
            errors[nameof(request.Date)] = ["The competition date cannot be in the future."];
        }

        if (request.Category?.Trim().Length > BirdCompetition.CategoryMaxLength)
        {
            errors[nameof(request.Category)] = ["The competition category cannot exceed 200 characters."];
        }

        if (request.Placement is <= 0)
        {
            errors[nameof(request.Placement)] = ["The competition placement must be positive."];
        }

        if (request.Location?.Trim().Length > BirdCompetition.LocationMaxLength)
        {
            errors[nameof(request.Location)] = ["The competition location cannot exceed 200 characters."];
        }

        if (request.Notes?.Trim().Length > BirdCompetition.NotesMaxLength)
        {
            errors[nameof(request.Notes)] = ["Competition notes cannot exceed 2000 characters."];
        }

        return errors;
    }

    private static BirdCompetitionResponse ToResponse(BirdCompetitionResult result) =>
        new(
            result.CompetitionId,
            result.BirdId,
            result.Name,
            result.CompetitionDate,
            result.Category,
            result.Placement,
            result.Location,
            result.Notes,
            result.CreatedAtUtc,
            result.UpdatedAtUtc);
}

public sealed record CreateBirdCompetitionRequest(
    string? Name,
    DateOnly? Date,
    string? Category,
    int? Placement,
    string? Location,
    string? Notes);

public sealed record BirdCompetitionResponse(
    Guid CompetitionId,
    Guid BirdId,
    string Name,
    DateOnly? Date,
    string? Category,
    int? Placement,
    string? Location,
    string? Notes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
