using System.Security.Claims;
using CriatorioVirtual.Application.Competitions;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Competitions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/birds/{birdId:guid}/competitions")]
[Authorize]
public sealed class BirdCompetitionController(
    ICommandExecutor commandExecutor,
    IQueryExecutor queryExecutor) : ControllerBase
{
    [HttpGet(Name = "ListBirdCompetitions")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(BirdCompetitionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ListAsync(
        Guid birdId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401");
        }

        var result = await queryExecutor.Execute<
            ListBirdCompetitionsQuery,
            ListBirdCompetitionsResult>(
            new ListBirdCompetitionsQuery(userId, birdId),
            cancellationToken);

        return result.Status switch
        {
            ListBirdCompetitionsStatus.Success => Ok(ToResponse(result)),
            ListBirdCompetitionsStatus.UserNotFound => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401"),
            ListBirdCompetitionsStatus.BreedingFarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A breeding farm must be selected before listing competitions.",
                type: "https://httpstatuses.com/409"),
            ListBirdCompetitionsStatus.BreedingFarmNotFound or
                ListBirdCompetitionsStatus.BirdNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The bird was not found.",
                type: "https://httpstatuses.com/404"),
            _ => throw new InvalidOperationException("The bird competition listing result is not supported.")
        };
    }

    [HttpGet("{competitionId:guid}", Name = "GetBirdCompetition")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(BirdCompetitionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetAsync(
        Guid birdId,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401");
        }

        var result = await queryExecutor.Execute<
            GetBirdCompetitionQuery,
            GetBirdCompetitionResult>(
            new GetBirdCompetitionQuery(userId, birdId, competitionId),
            cancellationToken);

        return result.Status switch
        {
            GetBirdCompetitionStatus.Success => Ok(ToResponse(result.Competition!)),
            GetBirdCompetitionStatus.UserNotFound => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401"),
            GetBirdCompetitionStatus.BreedingFarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A breeding farm must be selected before consulting a competition.",
                type: "https://httpstatuses.com/409"),
            GetBirdCompetitionStatus.BreedingFarmNotFound or
                GetBirdCompetitionStatus.BirdNotFound or
                GetBirdCompetitionStatus.CompetitionNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The competition was not found.",
                type: "https://httpstatuses.com/404"),
            _ => throw new InvalidOperationException("The bird competition detail result is not supported.")
        };
    }

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

    [HttpPut("{competitionId:guid}", Name = "UpdateBirdCompetition")]
    [ProducesResponseType(typeof(BirdCompetitionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        Guid birdId,
        Guid competitionId,
        [FromBody] UpdateBirdCompetitionRequest? request,
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
                },
                "Competition update data is invalid.");
        }

        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return ValidationProblemResult(errors, "Competition update data is invalid.");
        }

        try
        {
            var result = await commandExecutor.Execute<
                UpdateBirdCompetitionCommand,
                UpdateBirdCompetitionResult>(
                new UpdateBirdCompetitionCommand(
                    userId,
                    birdId,
                    competitionId,
                    request.Name,
                    request.Date,
                    request.Category,
                    request.Placement,
                    request.Location,
                    request.Notes),
                cancellationToken);

            return result.Status switch
            {
                UpdateBirdCompetitionStatus.Updated => Ok(ToResponse(result.Competition!)),
                UpdateBirdCompetitionStatus.UserNotFound => Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Authentication is required.",
                    type: "https://httpstatuses.com/401"),
                UpdateBirdCompetitionStatus.BreedingFarmNotSelected => Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "A breeding farm must be selected before editing a competition.",
                    type: "https://httpstatuses.com/409"),
                UpdateBirdCompetitionStatus.BreedingFarmNotFound or
                    UpdateBirdCompetitionStatus.BirdNotFound or
                    UpdateBirdCompetitionStatus.CompetitionNotFound => Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "The competition was not found.",
                    type: "https://httpstatuses.com/404"),
                UpdateBirdCompetitionStatus.InvalidData => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        ["request"] = ["The competition update data is invalid."]
                    },
                    "Competition update data is invalid."),
                _ => throw new InvalidOperationException("The bird competition update result is not supported.")
            };
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The competition was changed by another request. Reload it and try again.",
                type: "https://httpstatuses.com/409");
        }
    }

    [HttpDelete("{competitionId:guid}", Name = "DeleteBirdCompetition")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(
        Guid birdId,
        Guid competitionId,
        [FromBody] DeleteBirdCompetitionRequest? request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401");
        }

        if (request is null || !request.Confirmed)
        {
            return ValidationProblemResult(
                new Dictionary<string, string[]>
                {
                    [nameof(DeleteBirdCompetitionRequest.Confirmed)] = ["Explicit confirmation is required."]
                },
                "Competition removal confirmation is required.");
        }

        try
        {
            var result = await commandExecutor.Execute<
                DeleteBirdCompetitionCommand,
                DeleteBirdCompetitionResult>(
                new DeleteBirdCompetitionCommand(userId, birdId, competitionId, request.Confirmed),
                cancellationToken);

            return result.Status switch
            {
                DeleteBirdCompetitionStatus.Deleted => NoContent(),
                DeleteBirdCompetitionStatus.UserNotFound => Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Authentication is required.",
                    type: "https://httpstatuses.com/401"),
                DeleteBirdCompetitionStatus.BreedingFarmNotSelected => Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "A breeding farm must be selected before removing a competition.",
                    type: "https://httpstatuses.com/409"),
                DeleteBirdCompetitionStatus.BreedingFarmNotFound or
                    DeleteBirdCompetitionStatus.BirdNotFound or
                    DeleteBirdCompetitionStatus.CompetitionNotFound => Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "The competition was not found.",
                    type: "https://httpstatuses.com/404"),
                DeleteBirdCompetitionStatus.ConfirmationRequired => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        [nameof(DeleteBirdCompetitionRequest.Confirmed)] = ["Explicit confirmation is required."]
                    },
                    "Competition removal confirmation is required."),
                DeleteBirdCompetitionStatus.InvalidData => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        ["request"] = ["The competition removal data is invalid."]
                    },
                    "Competition removal data is invalid."),
                _ => throw new InvalidOperationException("The bird competition removal result is not supported.")
            };
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The competition was changed by another request. Reload it and try again.",
                type: "https://httpstatuses.com/409");
        }
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

    private static Dictionary<string, string[]> Validate(UpdateBirdCompetitionRequest request)
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

    private static BirdCompetitionsResponse ToResponse(ListBirdCompetitionsResult result) =>
        new(
            result.BreedingFarmId!.Value,
            result.BirdId!.Value,
            result.Competitions!.Select(ToResponse).ToArray());
}

public sealed record CreateBirdCompetitionRequest(
    string? Name,
    DateOnly? Date,
    string? Category,
    int? Placement,
    string? Location,
    string? Notes);

public sealed record UpdateBirdCompetitionRequest(
    string? Name,
    DateOnly? Date,
    string? Category,
    int? Placement,
    string? Location,
    string? Notes);

public sealed record DeleteBirdCompetitionRequest(bool Confirmed);

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

public sealed record BirdCompetitionsResponse(
    Guid BreedingFarmId,
    Guid BirdId,
    IReadOnlyCollection<BirdCompetitionResponse> Items);
