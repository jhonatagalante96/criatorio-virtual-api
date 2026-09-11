using System.Security.Claims;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Reproductions;
using CriatorioVirtual.Domain.Reproductions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    private static ReproductionBirdResponse ToResponse(ReproductionBirdResult result) =>
        new(
            result.BirdId,
            result.Name,
            result.Sex.ToString(),
            result.BirthDate,
            result.RingNumber,
            result.Status.ToString());

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
    string Status);
