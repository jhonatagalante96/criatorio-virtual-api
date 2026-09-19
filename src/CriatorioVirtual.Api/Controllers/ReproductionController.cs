using System.Security.Claims;
using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Reproductions;
using CriatorioVirtual.Domain.Reproductions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/reproductions")]
[Authorize]
public sealed class ReproductionController(
    ICommandExecutor commandExecutor,
    IQueryExecutor queryExecutor) : ControllerBase
{
    [HttpGet(Name = "ListReproductions")]
    [ProducesResponseType(typeof(ReproductionListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] string? status,
        [FromQuery] Guid? birdId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401");
        }

        var errors = ValidateListRequest(status, birdId, page, pageSize, out var parsedStatus);
        if (errors.Count > 0)
        {
            return ValidationProblemResult(errors, "Reproduction listing parameters are invalid.");
        }

        var result = await queryExecutor.Execute<ListReproductionsQuery, ListReproductionsResult>(
            new ListReproductionsQuery(userId, parsedStatus, birdId, page, pageSize),
            cancellationToken);

        return result.Status switch
        {
            ListReproductionsStatus.Success => Ok(ToResponse(result)),
            ListReproductionsStatus.UserNotFound => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401"),
            ListReproductionsStatus.BreedingFarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A breeding farm must be selected before listing reproductions.",
                type: "https://httpstatuses.com/409"),
            ListReproductionsStatus.BreedingFarmNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The selected breeding farm was not found.",
                type: "https://httpstatuses.com/404"),
            _ => throw new InvalidOperationException("The reproduction listing result is not supported.")
        };
    }

    [HttpGet("{reproductionId:guid}", Name = "GetReproduction")]
    [ProducesResponseType(typeof(ReproductionDetailsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetAsync(
        Guid reproductionId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401");
        }

        var result = await queryExecutor.Execute<GetReproductionQuery, GetReproductionResult>(
            new GetReproductionQuery(userId, reproductionId),
            cancellationToken);

        return result.Status switch
        {
            GetReproductionStatus.Success => Ok(ToResponse(result.Reproduction!)),
            GetReproductionStatus.UserNotFound => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401"),
            GetReproductionStatus.BreedingFarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A breeding farm must be selected before consulting a reproduction.",
                type: "https://httpstatuses.com/409"),
            GetReproductionStatus.BreedingFarmNotFound or GetReproductionStatus.ReproductionNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The reproduction was not found.",
                type: "https://httpstatuses.com/404"),
            _ => throw new InvalidOperationException("The reproduction detail result is not supported.")
        };
    }

    [HttpPost("{reproductionId:guid}/origin", Name = "LinkReproductionOrigin")]
    [ProducesResponseType(typeof(BirdResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> LinkOriginAsync(
        Guid reproductionId,
        [FromBody] LinkReproductionOriginRequest? request,
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

        var errors = ValidateOriginLink(request);
        if (errors.Count > 0)
        {
            return ValidationProblemResult(errors, "Reproduction origin data is invalid.");
        }

        try
        {
            var result = await commandExecutor.Execute<
                LinkReproductionOriginCommand,
                LinkReproductionOriginResult>(
                new LinkReproductionOriginCommand(
                    userId,
                    reproductionId,
                    request.BirdId!.Value,
                    request.Confirmed),
                cancellationToken);

            return result.Status switch
            {
                LinkReproductionOriginStatus.Linked => Ok(ToBirdResponse(result.Bird!)),
                LinkReproductionOriginStatus.UserNotFound => Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Authentication is required.",
                    type: "https://httpstatuses.com/401"),
                LinkReproductionOriginStatus.BreedingFarmNotSelected => Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "A breeding farm must be selected before linking a reproduction origin.",
                    type: "https://httpstatuses.com/409"),
                LinkReproductionOriginStatus.BreedingFarmNotFound or
                    LinkReproductionOriginStatus.ReproductionNotFound => Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "The reproduction was not found.",
                    type: "https://httpstatuses.com/404"),
                LinkReproductionOriginStatus.BirdNotFound => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        [nameof(request.BirdId)] =
                        ["The selected bird was not found in the selected breeding farm."]
                    }),
                LinkReproductionOriginStatus.BirdNotEligible => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        [nameof(request.BirdId)] =
                        ["The selected bird must be active and have a valid six-digit ring number."]
                    }),
                LinkReproductionOriginStatus.BirdAlreadyLinked => Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "The selected bird already has another genealogy origin.",
                    type: "https://httpstatuses.com/409"),
                LinkReproductionOriginStatus.SameBirdAsParent => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        [nameof(request.BirdId)] =
                        ["A reproduction parent cannot be linked as its own offspring."]
                    }),
                LinkReproductionOriginStatus.CycleDetected => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        [nameof(request.BirdId)] =
                        ["The selected bird would create a genealogy cycle."]
                    }),
                LinkReproductionOriginStatus.ConfirmationRequired => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        [nameof(request.Confirmed)] = ["Explicit confirmation is required."]
                    },
                    "Reproduction origin confirmation is required."),
                LinkReproductionOriginStatus.InvalidData => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        ["request"] = ["The reproduction origin data is invalid."]
                    },
                    "Reproduction origin data is invalid."),
                _ => throw new InvalidOperationException("The reproduction origin result is not supported.")
            };
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The selected bird was changed by another request. Reload it and try again.",
                type: "https://httpstatuses.com/409");
        }
    }

    [HttpPut("{reproductionId:guid}", Name = "UpdateReproduction")]
    [ProducesResponseType(typeof(ReproductionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        Guid reproductionId,
        [FromBody] UpdateReproductionRequest? request,
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

        var errors = ValidateUpdate(request);
        if (errors.Count > 0)
        {
            return ValidationProblemResult(errors, "Reproduction update data is invalid.");
        }

        try
        {
            var result = await commandExecutor.Execute<UpdateReproductionCommand, UpdateReproductionResult>(
                new UpdateReproductionCommand(
                    userId,
                    reproductionId,
                    request.MaleBirdId,
                    request.FemaleBirdId,
                    request.StartDate,
                    request.EndDate,
                    request.Notes,
                    request.HasMaleBirdId,
                    request.HasFemaleBirdId,
                    request.HasStartDate,
                    request.HasEndDate,
                    request.HasNotes),
                cancellationToken);

            return result.Status switch
            {
                UpdateReproductionStatus.Updated => Ok(ToResponse(result.Reproduction!)),
                UpdateReproductionStatus.UserNotFound => Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Authentication is required.",
                    type: "https://httpstatuses.com/401"),
                UpdateReproductionStatus.BreedingFarmNotSelected => Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "A breeding farm must be selected before editing a reproduction.",
                    type: "https://httpstatuses.com/409"),
                UpdateReproductionStatus.BreedingFarmNotFound or UpdateReproductionStatus.ReproductionNotFound => Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "The reproduction was not found.",
                    type: "https://httpstatuses.com/404"),
                UpdateReproductionStatus.BirdNotFound => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        ["birds"] = ["The male and female birds must belong to the selected breeding farm."]
                    }),
                UpdateReproductionStatus.BirdNotEligible => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        ["birds"] = ["Both birds must be active and have a valid ring number."]
                    }),
                UpdateReproductionStatus.MaleBirdSexInvalid => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        [nameof(request.MaleBirdId)] = ["The selected male bird must have Male sex."]
                    }),
                UpdateReproductionStatus.FemaleBirdSexInvalid => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        [nameof(request.FemaleBirdId)] = ["The selected female bird must have Female sex."]
                    }),
                UpdateReproductionStatus.InvalidState => Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Only notes can be changed after a reproduction reaches a terminal status.",
                    type: "https://httpstatuses.com/409"),
                UpdateReproductionStatus.InvalidData => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        ["request"] = ["The reproduction update data is invalid."]
                    },
                    "Reproduction update data is invalid."),
                _ => throw new InvalidOperationException("The reproduction update result is not supported.")
            };
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The reproduction was changed by another request. Reload it and try again.",
                type: "https://httpstatuses.com/409");
        }
    }

    [HttpPatch("{reproductionId:guid}/status", Name = "ChangeReproductionStatus")]
    [ProducesResponseType(typeof(ReproductionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeStatusAsync(
        Guid reproductionId,
        [FromBody] ChangeReproductionStatusRequest? request,
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

        var errors = ValidateStatusChange(request, out var status);
        if (errors.Count > 0)
        {
            return ValidationProblemResult(errors, "Reproduction status change data is invalid.");
        }

        try
        {
            var result = await commandExecutor.Execute<ChangeReproductionStatusCommand, ChangeReproductionStatusResult>(
                new ChangeReproductionStatusCommand(
                    userId,
                    reproductionId,
                    status!.Value,
                    request.Confirmed,
                    request.EndDate),
                cancellationToken);

            return result.Status switch
            {
                ChangeReproductionStatusStatus.Updated => Ok(ToResponse(result.Reproduction!)),
                ChangeReproductionStatusStatus.UserNotFound => Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Authentication is required.",
                    type: "https://httpstatuses.com/401"),
                ChangeReproductionStatusStatus.BreedingFarmNotSelected => Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "A breeding farm must be selected before changing a reproduction status.",
                    type: "https://httpstatuses.com/409"),
                ChangeReproductionStatusStatus.BreedingFarmNotFound or ChangeReproductionStatusStatus.ReproductionNotFound => Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "The reproduction was not found.",
                    type: "https://httpstatuses.com/404"),
                ChangeReproductionStatusStatus.ConfirmationRequired => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        [nameof(request.Confirmed)] = ["Explicit confirmation is required."]
                    },
                    "Reproduction status change confirmation is required."),
                ChangeReproductionStatusStatus.InvalidStatus => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        [nameof(request.Status)] = ["Only Finished or Cancelled can be applied manually."]
                    },
                    "Reproduction status change data is invalid."),
                ChangeReproductionStatusStatus.InvalidState => Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "The reproduction status cannot be changed from its current state.",
                    type: "https://httpstatuses.com/409"),
                ChangeReproductionStatusStatus.InvalidData => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        ["request"] = ["The reproduction status change data is invalid."]
                    },
                    "Reproduction status change data is invalid."),
                _ => throw new InvalidOperationException("The reproduction status change result is not supported.")
            };
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The reproduction was changed by another request. Reload it and try again.",
                type: "https://httpstatuses.com/409");
        }
    }

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

        try
        {
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
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A bird was changed by another request. Reload it and try again.",
                type: "https://httpstatuses.com/409");
        }
    }

    private IActionResult ValidationProblemResult(
        IReadOnlyDictionary<string, string[]> errors,
        string title = "Reproduction data is invalid.")
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

    private static Dictionary<string, string[]> ValidateUpdate(UpdateReproductionRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (request.MaleBirdId == Guid.Empty)
        {
            errors[nameof(request.MaleBirdId)] = ["The male bird identifier cannot be empty."];
        }

        if (request.FemaleBirdId == Guid.Empty)
        {
            errors[nameof(request.FemaleBirdId)] = ["The female bird identifier cannot be empty."];
        }

        if (request.MaleBirdId is not null &&
            request.MaleBirdId != Guid.Empty &&
            request.MaleBirdId == request.FemaleBirdId)
        {
            errors["birds"] = ["The same bird cannot be both parents."];
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (request.StartDate > today)
        {
            errors[nameof(request.StartDate)] = ["The reproduction start date cannot be in the future."];
        }

        if (request.EndDate is not null && request.EndDate > today)
        {
            errors[nameof(request.EndDate)] = ["The reproduction end date cannot be in the future."];
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

    private static Dictionary<string, string[]> ValidateStatusChange(
        ChangeReproductionStatusRequest request,
        out ReproductionStatus? status)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        status = null;

        if (!TryParseEnumName(request.Status ?? string.Empty, out ReproductionStatus parsedStatus) ||
            parsedStatus is not (ReproductionStatus.Finished or ReproductionStatus.Cancelled))
        {
            errors[nameof(request.Status)] = ["Only Finished or Cancelled can be applied manually."];
        }
        else
        {
            status = parsedStatus;
        }

        if (!request.Confirmed)
        {
            errors[nameof(request.Confirmed)] = ["Explicit confirmation is required."];
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (status == ReproductionStatus.Finished && request.EndDate is null)
        {
            errors[nameof(request.EndDate)] = ["An end date is required when finishing a reproduction."];
        }

        if (status == ReproductionStatus.Cancelled && request.EndDate is not null)
        {
            errors[nameof(request.EndDate)] = ["An end date is only valid when finishing a reproduction."];
        }

        if (request.EndDate > today)
        {
            errors[nameof(request.EndDate)] = ["The reproduction end date cannot be in the future."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateOriginLink(
        LinkReproductionOriginRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (request.BirdId is null || request.BirdId == Guid.Empty)
        {
            errors[nameof(request.BirdId)] = ["A bird is required."];
        }

        if (!request.Confirmed)
        {
            errors[nameof(request.Confirmed)] = ["Explicit confirmation is required."];
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

    private static ReproductionListResponse ToResponse(ListReproductionsResult result) =>
        new(
            result.BreedingFarmId!.Value,
            result.Items.Select(ToResponse).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount,
            result.TotalCount == 0
                ? 0
                : (int)Math.Ceiling(result.TotalCount / (double)result.PageSize));

    private static ReproductionListItemResponse ToResponse(ReproductionListItemResult result) =>
        new(
            result.ReproductionId,
            result.BreedingFarmId,
            ToResponse(result.MaleBird),
            ToResponse(result.FemaleBird),
            result.StartDate,
            result.EndDate,
            result.Status.ToString(),
            result.CreatedAtUtc,
            result.UpdatedAtUtc);

    private static ReproductionDetailsResponse ToResponse(ReproductionDetailsResult result) =>
        new(
            result.ReproductionId,
            result.BreedingFarmId,
            ToResponse(result.MaleBird),
            ToResponse(result.FemaleBird),
            result.StartDate,
            result.EndDate,
            result.Notes,
            result.Status.ToString(),
            result.CreatedAtUtc,
            result.UpdatedAtUtc);

    private static BirdResponse ToBirdResponse(BirdResult result) =>
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
            result.ExternalFatherSex?.ToString(),
            result.MotherBirdId,
            result.ExternalMotherName,
            result.ExternalMotherSex?.ToString(),
            result.Notes,
            result.Status.ToString(),
            result.IdentificationPending,
            result.AgeInYears,
            result.CreatedAtUtc,
            result.UpdatedAtUtc,
            result.PrimaryPhotoId);

    private static ReproductionBirdResponse ToResponse(ReproductionBirdResult result) =>
        new(
            result.BirdId,
            result.Name,
            result.Sex.ToString(),
            result.BirthDate,
            result.RingNumber,
            result.Status.ToString(),
            result.CanNavigate);

    private static Dictionary<string, string[]> ValidateListRequest(
        string? status,
        Guid? birdId,
        int page,
        int pageSize,
        out ReproductionStatus? parsedStatus)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TryParseEnumName(status, out ReproductionStatus value))
            {
                errors[nameof(status)] = ["The reproduction status is invalid."];
            }
            else
            {
                parsedStatus = value;
            }
        }

        if (birdId == Guid.Empty)
        {
            errors[nameof(birdId)] = ["The bird identifier cannot be empty."];
        }

        if (page < 1)
        {
            errors[nameof(page)] = ["The page must be at least 1."];
        }

        if (pageSize is < 1 or > 100)
        {
            errors[nameof(pageSize)] = ["The pageSize must be between 1 and 100."];
        }

        return errors;
    }

    private static bool TryParseEnumName<TEnum>(string value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        var normalized = value.Trim();
        return Enum.TryParse(normalized, ignoreCase: true, out parsed) &&
            Enum.IsDefined(parsed) &&
            string.Equals(parsed.ToString(), normalized, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record CreateReproductionRequest(
    Guid? MaleBirdId,
    Guid? FemaleBirdId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Notes);

public sealed class UpdateReproductionRequest
{
    private Guid? _maleBirdId;
    private Guid? _femaleBirdId;
    private DateOnly? _startDate;
    private DateOnly? _endDate;
    private string? _notes;

    public Guid? MaleBirdId
    {
        get => _maleBirdId;
        set
        {
            _maleBirdId = value;
            HasMaleBirdId = true;
        }
    }

    public Guid? FemaleBirdId
    {
        get => _femaleBirdId;
        set
        {
            _femaleBirdId = value;
            HasFemaleBirdId = true;
        }
    }

    public DateOnly? StartDate
    {
        get => _startDate;
        set
        {
            _startDate = value;
            HasStartDate = true;
        }
    }

    public DateOnly? EndDate
    {
        get => _endDate;
        set
        {
            _endDate = value;
            HasEndDate = true;
        }
    }

    public string? Notes
    {
        get => _notes;
        set
        {
            _notes = value;
            HasNotes = true;
        }
    }

    [JsonIgnore]
    public bool HasMaleBirdId { get; private set; }

    [JsonIgnore]
    public bool HasFemaleBirdId { get; private set; }

    [JsonIgnore]
    public bool HasStartDate { get; private set; }

    [JsonIgnore]
    public bool HasEndDate { get; private set; }

    [JsonIgnore]
    public bool HasNotes { get; private set; }
}

public sealed record ChangeReproductionStatusRequest(
    string? Status,
    bool Confirmed,
    DateOnly? EndDate);

public sealed record LinkReproductionOriginRequest(
    Guid? BirdId,
    bool Confirmed);

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

public sealed record ReproductionListResponse(
    Guid BreedingFarmId,
    IReadOnlyCollection<ReproductionListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record ReproductionListItemResponse(
    Guid ReproductionId,
    Guid BreedingFarmId,
    ReproductionBirdResponse MaleBird,
    ReproductionBirdResponse FemaleBird,
    DateOnly StartDate,
    DateOnly? EndDate,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ReproductionDetailsResponse(
    Guid ReproductionId,
    Guid BreedingFarmId,
    ReproductionBirdResponse MaleBird,
    ReproductionBirdResponse FemaleBird,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Notes,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ReproductionBirdResponse(
    Guid BirdId,
    string Name,
    string Sex,
    DateOnly? BirthDate,
    string? RingNumber,
    string Status,
    bool CanNavigate);
