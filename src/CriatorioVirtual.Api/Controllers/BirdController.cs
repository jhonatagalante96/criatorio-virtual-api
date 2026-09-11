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
public sealed class BirdController(
    ICommandExecutor commandExecutor,
    IQueryExecutor queryExecutor) : ControllerBase
{
    [HttpGet(Name = "ListBirds")]
    [ProducesResponseType(typeof(BirdListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] string? search,
        [FromQuery] string? sex,
        [FromQuery] Guid? speciesId,
        [FromQuery] string? status,
        [FromQuery] string? identificationPending,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
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

        var errors = ValidateListRequest(
            search,
            sex,
            speciesId,
            status,
            identificationPending,
            sortBy,
            sortDirection,
            page,
            pageSize,
            out var parsedSex,
            out var parsedStatus,
            out var parsedIdentificationPending,
            out var parsedSortBy,
            out var parsedSortDirection);
        if (errors.Count > 0)
        {
            return ValidationProblemResult(
                errors,
                "Bird listing parameters are invalid.");
        }

        var result = await queryExecutor.Execute<ListBirdsQuery, ListBirdsResult>(
            new ListBirdsQuery(
                userId,
                string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                parsedSex,
                speciesId,
                parsedStatus,
                parsedIdentificationPending,
                parsedSortBy,
                parsedSortDirection,
                page,
                pageSize),
            cancellationToken);

        return result.Status switch
        {
            ListBirdsStatus.Success => Ok(ToResponse(result)),
            ListBirdsStatus.UserNotFound => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401"),
            ListBirdsStatus.BreedingFarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A breeding farm must be selected before listing birds.",
                type: "https://httpstatuses.com/409"),
            ListBirdsStatus.BreedingFarmNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The selected breeding farm was not found.",
                type: "https://httpstatuses.com/404"),
            _ => throw new InvalidOperationException("The bird listing result is not supported.")
        };
    }

    [HttpGet("{birdId:guid}", Name = "GetBird")]
    [ProducesResponseType(typeof(BirdDetailsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetAsync(
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

        var result = await queryExecutor.Execute<GetBirdQuery, GetBirdResult>(
            new GetBirdQuery(userId, birdId),
            cancellationToken);

        return result.Status switch
        {
            GetBirdStatus.Success => Ok(ToResponse(result.Bird!)),
            GetBirdStatus.UserNotFound => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401"),
            GetBirdStatus.BreedingFarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A breeding farm must be selected before consulting a bird.",
                type: "https://httpstatuses.com/409"),
            GetBirdStatus.BreedingFarmNotFound or GetBirdStatus.BirdNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The bird was not found.",
                type: "https://httpstatuses.com/404"),
            _ => throw new InvalidOperationException("The bird detail result is not supported.")
        };
    }

    [HttpGet("parent-options", Name = "SearchBirdParentOptions")]
    [ProducesResponseType(typeof(BirdParentOptionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ParentOptionsAsync(
        [FromQuery] string? search,
        [FromQuery] string? sex,
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401");
        }

        var errors = ValidateParentOptionsRequest(search, sex, limit, out var parsedSex);
        if (errors.Count > 0)
        {
            return ValidationProblemResult(errors, "Bird parent search parameters are invalid.");
        }

        var result = await queryExecutor.Execute<SearchBirdParentOptionsQuery, SearchBirdParentOptionsResult>(
            new SearchBirdParentOptionsQuery(
                userId,
                string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                parsedSex,
                limit),
            cancellationToken);

        return result.Status switch
        {
            SearchBirdParentOptionsStatus.Success => Ok(ToResponse(result)),
            SearchBirdParentOptionsStatus.UserNotFound => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401"),
            SearchBirdParentOptionsStatus.BreedingFarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A breeding farm must be selected before searching bird parents.",
                type: "https://httpstatuses.com/409"),
            SearchBirdParentOptionsStatus.BreedingFarmNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The selected breeding farm was not found.",
                type: "https://httpstatuses.com/404"),
            _ => throw new InvalidOperationException("The bird parent search result is not supported.")
        };
    }

    [HttpGet("{birdId:guid}/eligibility", Name = "GetBirdEligibility")]
    [ProducesResponseType(typeof(BirdEligibilityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetEligibilityAsync(
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

        var result = await queryExecutor.Execute<GetBirdEligibilityQuery, GetBirdEligibilityResult>(
            new GetBirdEligibilityQuery(userId, birdId),
            cancellationToken);

        return result.Status switch
        {
            GetBirdEligibilityStatus.Success => Ok(ToResponse(result.Bird!)),
            GetBirdEligibilityStatus.UserNotFound => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401"),
            GetBirdEligibilityStatus.BreedingFarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A breeding farm must be selected before consulting bird eligibility.",
                type: "https://httpstatuses.com/409"),
            GetBirdEligibilityStatus.BreedingFarmNotFound or GetBirdEligibilityStatus.BirdNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The bird was not found.",
                type: "https://httpstatuses.com/404"),
            _ => throw new InvalidOperationException("The bird eligibility result is not supported.")
        };
    }

    [HttpPut("{birdId:guid}", Name = "UpdateBird")]
    [ProducesResponseType(typeof(BirdResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        Guid birdId,
        [FromBody] UpdateBirdRequest? request,
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

        var errors = ValidateUpdate(request, out var sex);
        if (errors.Count > 0)
        {
            return ValidationProblemResult(errors, "Bird update data is invalid.");
        }

        try
        {
            var result = await commandExecutor.Execute<UpdateBirdCommand, UpdateBirdResult>(
                new UpdateBirdCommand(
                    userId,
                    birdId,
                    request.Name,
                    sex,
                    request.SpeciesId,
                    request.BirthDate,
                    request.RingNumber,
                    request.Notes),
                cancellationToken);

            return result.Status switch
            {
                UpdateBirdStatus.Updated => Ok(ToResponse(result.Bird!)),
                UpdateBirdStatus.UserNotFound => Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Authentication is required.",
                    type: "https://httpstatuses.com/401"),
                UpdateBirdStatus.BreedingFarmNotSelected => Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "A breeding farm must be selected before editing a bird.",
                    type: "https://httpstatuses.com/409"),
                UpdateBirdStatus.BreedingFarmNotFound or UpdateBirdStatus.BirdNotFound => Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "The bird was not found.",
                    type: "https://httpstatuses.com/404"),
                UpdateBirdStatus.SpeciesNotFound => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        [nameof(request.SpeciesId)] = ["The species must exist and be active."]
                    }),
                UpdateBirdStatus.DuplicateRingNumber => DuplicateRingNumberConflict(),
                UpdateBirdStatus.InvalidData => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        ["request"] = ["The bird update data is invalid."]
                    },
                    "Bird update data is invalid."),
                _ => throw new InvalidOperationException("The bird update result is not supported.")
            };
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return DuplicateRingNumberConflict();
        }
    }

    [HttpPut("{birdId:guid}/genealogy", Name = "UpdateBirdGenealogy")]
    [ProducesResponseType(typeof(BirdResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateGenealogyAsync(
        Guid birdId,
        [FromBody] UpdateBirdGenealogyRequest? request,
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

        var errors = ValidateGenealogy(
            request,
            out var externalFatherSex,
            out var externalMotherSex);
        if (errors.Count > 0)
        {
            return ValidationProblemResult(errors, "Bird genealogy data is invalid.");
        }

        var result = await commandExecutor.Execute<UpdateBirdGenealogyCommand, UpdateBirdGenealogyResult>(
            new UpdateBirdGenealogyCommand(
                userId,
                birdId,
                request.FatherBirdId,
                request.ExternalFatherName,
                request.MotherBirdId,
                request.ExternalMotherName,
                externalFatherSex,
                externalMotherSex),
            cancellationToken);

        return result.Status switch
        {
            UpdateBirdGenealogyStatus.Updated => Ok(ToResponse(result.Bird!)),
            UpdateBirdGenealogyStatus.UserNotFound => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401"),
            UpdateBirdGenealogyStatus.BreedingFarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A breeding farm must be selected before editing genealogy.",
                type: "https://httpstatuses.com/409"),
            UpdateBirdGenealogyStatus.BreedingFarmNotFound or UpdateBirdGenealogyStatus.BirdNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The bird was not found.",
                type: "https://httpstatuses.com/404"),
            UpdateBirdGenealogyStatus.ParentNotFound => ValidationProblemResult(
                new Dictionary<string, string[]>
                {
                    ["parent"] = ["A linked parent must belong to the selected breeding farm."]
                }),
            UpdateBirdGenealogyStatus.ParentSexInvalid => ValidationProblemResult(
                new Dictionary<string, string[]>
                {
                    ["parent"] = ["The father must be male and the mother must be female."]
                }),
            UpdateBirdGenealogyStatus.DuplicateParent => ValidationProblemResult(
                new Dictionary<string, string[]>
                {
                    ["parent"] = ["The same bird cannot be both parents."]
                }),
            UpdateBirdGenealogyStatus.CycleDetected => ValidationProblemResult(
                new Dictionary<string, string[]>
                {
                    ["parent"] = ["The selected parent would create a genealogy cycle."]
                }),
            UpdateBirdGenealogyStatus.TransferPending => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Genealogy changes are unavailable while a transfer is pending.",
                type: "https://httpstatuses.com/409"),
            UpdateBirdGenealogyStatus.InvalidData => ValidationProblemResult(
                new Dictionary<string, string[]>
                {
                    ["request"] = ["The bird genealogy data is invalid."]
                },
                "Bird genealogy data is invalid."),
            _ => throw new InvalidOperationException("The bird genealogy result is not supported.")
        };
    }

    [HttpPatch("{birdId:guid}/status", Name = "ChangeBirdStatus")]
    [ProducesResponseType(typeof(BirdResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeStatusAsync(
        Guid birdId,
        [FromBody] ChangeBirdStatusRequest? request,
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
            return ValidationProblemResult(errors, "Bird status change data is invalid.");
        }

        var result = await commandExecutor.Execute<ChangeBirdStatusCommand, ChangeBirdStatusResult>(
            new ChangeBirdStatusCommand(
                userId,
                birdId,
                status!.Value,
                request.Confirmed,
                request.DeathDate,
                request.Notes),
            cancellationToken);

        return result.Status switch
        {
            ChangeBirdStatusStatus.Updated => Ok(ToResponse(result.Bird!)),
            ChangeBirdStatusStatus.UserNotFound => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401"),
            ChangeBirdStatusStatus.BreedingFarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A breeding farm must be selected before changing a bird status.",
                type: "https://httpstatuses.com/409"),
            ChangeBirdStatusStatus.BreedingFarmNotFound or ChangeBirdStatusStatus.BirdNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The bird was not found.",
                type: "https://httpstatuses.com/404"),
            ChangeBirdStatusStatus.ConfirmationRequired => ValidationProblemResult(
                new Dictionary<string, string[]>
                {
                    [nameof(request.Confirmed)] = ["Explicit confirmation is required."]
                },
                "Bird status change confirmation is required."),
            ChangeBirdStatusStatus.InvalidStatus => ValidationProblemResult(
                new Dictionary<string, string[]>
                {
                    [nameof(request.Status)] = ["Only Archived, Deceased, or Escaped can be applied manually."]
                },
                "Bird status change data is invalid."),
            ChangeBirdStatusStatus.InvalidData => ValidationProblemResult(
                new Dictionary<string, string[]>
                {
                    ["request"] = ["The bird status change data is invalid."]
                },
                "Bird status change data is invalid."),
            ChangeBirdStatusStatus.StatusChangeNotAllowed => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The bird status cannot be changed from its current state.",
                type: "https://httpstatuses.com/409"),
            ChangeBirdStatusStatus.TransferPending => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Bird status changes are unavailable while a transfer is pending.",
                type: "https://httpstatuses.com/409"),
            _ => throw new InvalidOperationException("The bird status change result is not supported.")
        };
    }

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

        var errors = Validate(
            request,
            out var sex,
            out var externalFatherSex,
            out var externalMotherSex);
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
                    request.Notes,
                    externalFatherSex,
                    externalMotherSex),
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
                CreateBirdStatus.DuplicateRingNumber => DuplicateRingNumberConflict(),
                _ => throw new InvalidOperationException("The bird creation result is not supported.")
            };
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return DuplicateRingNumberConflict();
        }
    }

    private IActionResult ValidationProblemResult(
        IReadOnlyDictionary<string, string[]> errors,
        string title = "Bird data is invalid.")
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

    private static Dictionary<string, string[]> ValidateListRequest(
        string? search,
        string? sex,
        Guid? speciesId,
        string? status,
        string? identificationPending,
        string? sortBy,
        string? sortDirection,
        int page,
        int pageSize,
        out BirdSex? parsedSex,
        out BirdStatus? parsedStatus,
        out bool? parsedIdentificationPending,
        out BirdSortField parsedSortBy,
        out BirdSortDirection parsedSortDirection)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        parsedSex = null;
        parsedStatus = null;
        parsedIdentificationPending = null;
        parsedSortBy = BirdSortField.Name;
        parsedSortDirection = BirdSortDirection.Ascending;

        if (search?.Trim().Length > 100)
        {
            errors[nameof(search)] = ["The bird search cannot exceed 100 characters."];
        }

        if (!string.IsNullOrWhiteSpace(sex))
        {
            if (!TryParseEnumName(sex, out BirdSex value))
            {
                errors[nameof(sex)] = ["The bird sex is invalid."];
            }
            else
            {
                parsedSex = value;
            }
        }

        if (speciesId == Guid.Empty)
        {
            errors[nameof(speciesId)] = ["The species identifier cannot be empty."];
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TryParseEnumName(status, out BirdStatus value))
            {
                errors[nameof(status)] = ["The bird status is invalid."];
            }
            else
            {
                parsedStatus = value;
            }
        }

        if (!string.IsNullOrWhiteSpace(identificationPending))
        {
            if (!bool.TryParse(identificationPending.Trim(), out var value))
            {
                errors[nameof(identificationPending)] = ["The identificationPending filter must be true or false."];
            }
            else
            {
                parsedIdentificationPending = value;
            }
        }

        if (!string.IsNullOrWhiteSpace(sortBy) && !TryParseSortField(sortBy, out parsedSortBy))
        {
            errors[nameof(sortBy)] = ["The sortBy value is invalid."];
        }

        if (!string.IsNullOrWhiteSpace(sortDirection) && !TryParseSortDirection(sortDirection, out parsedSortDirection))
        {
            errors[nameof(sortDirection)] = ["The sortDirection value must be asc or desc."];
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

    private static bool TryParseSortField(string value, out BirdSortField parsed)
    {
        parsed = value.Trim().ToLowerInvariant() switch
        {
            "name" => BirdSortField.Name,
            "ringnumber" => BirdSortField.RingNumber,
            "birthdate" => BirdSortField.BirthDate,
            "species" => BirdSortField.Species,
            "sex" => BirdSortField.Sex,
            "status" => BirdSortField.Status,
            "createdat" or "createdatutc" => BirdSortField.CreatedAt,
            _ => default
        };

        return value.Trim().Equals("name", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("ringNumber", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("birthDate", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("species", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("sex", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("status", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("createdAt", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("createdAtUtc", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseSortDirection(string value, out BirdSortDirection parsed)
    {
        var normalized = value.Trim();
        if (normalized.Equals("asc", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("ascending", StringComparison.OrdinalIgnoreCase))
        {
            parsed = BirdSortDirection.Ascending;
            return true;
        }

        if (normalized.Equals("desc", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("descending", StringComparison.OrdinalIgnoreCase))
        {
            parsed = BirdSortDirection.Descending;
            return true;
        }

        parsed = BirdSortDirection.Ascending;
        return false;
    }

    private static Dictionary<string, string[]> Validate(
        CreateBirdRequest request,
        out BirdSex? sex,
        out BirdSex? externalFatherSex,
        out BirdSex? externalMotherSex)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        sex = null;
        externalFatherSex = null;
        externalMotherSex = null;
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
        externalFatherSex = ValidateExternalParent(
            errors,
            request.ExternalFatherName,
            request.ExternalFatherSex,
            request.FatherBirdId,
            BirdSex.Male,
            nameof(request.ExternalFatherName),
            nameof(request.ExternalFatherSex),
            "father");
        externalMotherSex = ValidateExternalParent(
            errors,
            request.ExternalMotherName,
            request.ExternalMotherSex,
            request.MotherBirdId,
            BirdSex.Female,
            nameof(request.ExternalMotherName),
            nameof(request.ExternalMotherSex),
            "mother");
        AddMaxLengthError(errors, nameof(request.Notes), request.Notes, 2000);
        if (request.FatherBirdId == Guid.Empty)
        {
            errors[nameof(request.FatherBirdId)] = ["The father bird identifier cannot be empty."];
        }

        if (request.MotherBirdId == Guid.Empty)
        {
            errors[nameof(request.MotherBirdId)] = ["The mother bird identifier cannot be empty."];
        }

        if (request.FatherBirdId is not null && request.FatherBirdId == request.MotherBirdId)
        {
            errors["parent"] = ["The same bird cannot be both parents."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateUpdate(
        UpdateBirdRequest request,
        out BirdSex? sex)
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

        AddMaxLengthError(errors, nameof(request.Notes), request.Notes, 2000);
        return errors;
    }

    private static Dictionary<string, string[]> ValidateParentOptionsRequest(
        string? search,
        string? sex,
        int limit,
        out BirdSex? parsedSex)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        parsedSex = null;

        if (search?.Trim().Length > 100)
        {
            errors[nameof(search)] = ["The bird parent search cannot exceed 100 characters."];
        }

        if (!string.IsNullOrWhiteSpace(sex))
        {
            if (!TryParseEnumName(sex, out BirdSex value) || value == BirdSex.Unknown)
            {
                errors[nameof(sex)] = ["The parent sex must be Male or Female."];
            }
            else
            {
                parsedSex = value;
            }
        }

        if (limit is < 1 or > 20)
        {
            errors[nameof(limit)] = ["The parent option limit must be between 1 and 20."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateGenealogy(
        UpdateBirdGenealogyRequest request,
        out BirdSex? externalFatherSex,
        out BirdSex? externalMotherSex)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        externalFatherSex = ValidateExternalParent(
            errors,
            request.ExternalFatherName,
            request.ExternalFatherSex,
            request.FatherBirdId,
            BirdSex.Male,
            nameof(request.ExternalFatherName),
            nameof(request.ExternalFatherSex),
            "father");
        externalMotherSex = ValidateExternalParent(
            errors,
            request.ExternalMotherName,
            request.ExternalMotherSex,
            request.MotherBirdId,
            BirdSex.Female,
            nameof(request.ExternalMotherName),
            nameof(request.ExternalMotherSex),
            "mother");

        AddMaxLengthError(errors, nameof(request.ExternalFatherName), request.ExternalFatherName, 200);
        AddMaxLengthError(errors, nameof(request.ExternalMotherName), request.ExternalMotherName, 200);
        if (request.FatherBirdId == Guid.Empty)
        {
            errors[nameof(request.FatherBirdId)] = ["The father bird identifier cannot be empty."];
        }

        if (request.MotherBirdId == Guid.Empty)
        {
            errors[nameof(request.MotherBirdId)] = ["The mother bird identifier cannot be empty."];
        }

        if (request.FatherBirdId is not null && request.FatherBirdId == request.MotherBirdId)
        {
            errors["parent"] = ["The same bird cannot be both parents."];
        }

        return errors;
    }

    private static BirdSex? ValidateExternalParent(
        IDictionary<string, string[]> errors,
        string? name,
        string? sex,
        Guid? linkedBirdId,
        BirdSex expectedSex,
        string nameKey,
        string sexKey,
        string parentKey)
    {
        var hasName = !string.IsNullOrWhiteSpace(name);
        var hasSex = !string.IsNullOrWhiteSpace(sex);
        BirdSex? parsedSex = null;

        if (hasSex)
        {
            if (!TryParseEnumName(sex!, out BirdSex value) ||
                value == BirdSex.Unknown ||
                value != expectedSex)
            {
                errors[sexKey] = [$"The external {parentKey} sex must be {expectedSex}."];
            }
            else
            {
                parsedSex = value;
            }
        }

        if (hasName && !hasSex)
        {
            errors[sexKey] = [$"The external {parentKey} sex is required when a name is provided."];
        }
        else if (!hasName && parsedSex is not null)
        {
            errors[nameKey] = [$"The external {parentKey} name is required when a sex is provided."];
        }

        if (linkedBirdId is not null && (hasName || parsedSex is not null))
        {
            errors[parentKey] = [$"The {parentKey} must be linked to a bird or represented by external data, not both."];
        }

        return parsedSex;
    }

    private static Dictionary<string, string[]> ValidateStatusChange(
        ChangeBirdStatusRequest request,
        out BirdStatus? status)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        status = null;

        if (!TryParseEnumName(request.Status ?? string.Empty, out BirdStatus parsedStatus) ||
            parsedStatus is not (BirdStatus.Archived or BirdStatus.Deceased or BirdStatus.Escaped))
        {
            errors[nameof(request.Status)] = ["Only Archived, Deceased, or Escaped can be applied manually."];
        }
        else
        {
            status = parsedStatus;
        }

        if (!request.Confirmed)
        {
            errors[nameof(request.Confirmed)] = ["Explicit confirmation is required."];
        }

        if (status == BirdStatus.Deceased && request.DeathDate is null)
        {
            errors[nameof(request.DeathDate)] = ["A death date is required for a deceased bird."];
        }

        if (status is not null and not BirdStatus.Deceased && request.DeathDate is not null)
        {
            errors[nameof(request.DeathDate)] = ["A death date is only valid for a deceased bird."];
        }

        if (status is not null and not BirdStatus.Deceased && request.Notes is not null)
        {
            errors[nameof(request.Notes)] = ["Status observations are only valid for a deceased bird."];
        }

        AddMaxLengthError(errors, nameof(request.Notes), request.Notes, 2000);
        return errors;
    }

    private static IActionResult DuplicateRingNumberConflict() =>
        new ConflictObjectResult(
            new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The ring number is already in use.",
                Type = "https://httpstatuses.com/409"
            });

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
            result.ExternalFatherSex?.ToString(),
            result.MotherBirdId,
            result.ExternalMotherName,
            result.ExternalMotherSex?.ToString(),
            result.Notes,
            result.Status.ToString(),
            result.IdentificationPending,
            result.AgeInYears,
            result.CreatedAtUtc,
            result.UpdatedAtUtc);

    private static BirdListResponse ToResponse(ListBirdsResult result) =>
        new(
            result.BreedingFarmId!.Value,
            result.Items
                .Select(item => new BirdListItemResponse(
                    item.BirdId,
                    item.Name,
                    item.SpeciesId,
                    item.SpeciesScientificName,
                    item.SpeciesPopularName,
                    item.Sex.ToString(),
                    item.BirthDate,
                    item.RingNumber,
                    item.Status.ToString(),
                    item.IdentificationPending,
                    item.AgeInYears,
                    item.CreatedAtUtc))
                .ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount,
            result.TotalCount == 0
                ? 0
                : (int)Math.Ceiling(result.TotalCount / (double)result.PageSize));

    private static BirdDetailsResponse ToResponse(BirdDetailsResult result) =>
        new(
            result.BirdId,
            result.GenealogyRootId,
            result.BreedingFarmId,
            result.Name,
            result.SpeciesId,
            result.SpeciesScientificName,
            result.SpeciesPopularName,
            result.Sex.ToString(),
            result.BirthDate,
            result.DeathDate,
            result.RingNumber,
            result.FatherBirdId,
            result.Father is null ? null : ToResponse(result.Father),
            result.ExternalFatherName,
            result.ExternalFatherSex?.ToString(),
            result.MotherBirdId,
            result.Mother is null ? null : ToResponse(result.Mother),
            result.ExternalMotherName,
            result.ExternalMotherSex?.ToString(),
            result.Notes,
            result.Status.ToString(),
            result.IdentificationPending,
            result.AgeInYears,
            result.CreatedAtUtc,
            result.UpdatedAtUtc);

    private static BirdEligibilityResponse ToResponse(BirdEligibilityResult result) =>
        new(
            result.BirdId,
            result.IsEligible,
            result.IdentificationPending,
            result.Issues
                .Select(issue => new BirdEligibilityIssueResponse(
                    issue.ToString(),
                    GetEligibilityIssueMessage(issue)))
                .ToArray());

    private static BirdParentOptionsResponse ToResponse(SearchBirdParentOptionsResult result) =>
        new(
            result.BreedingFarmId!.Value,
            result.Items
                .Select(item => new BirdParentOptionResponse(
                    item.BirdId,
                    item.Name,
                    item.Sex.ToString(),
                    item.BirthDate,
                    item.RingNumber))
                .ToArray());

    private static string GetEligibilityIssueMessage(BirdEligibilityIssueCode issue) =>
        issue switch
        {
            BirdEligibilityIssueCode.MissingRingNumber =>
                "A valid six-digit ring number is required for this action.",
            BirdEligibilityIssueCode.InactiveStatus =>
                "Only active birds are eligible for this action.",
            _ => throw new InvalidOperationException("The bird eligibility issue is not supported.")
        };

    private static BirdParentResponse ToResponse(BirdParentResult result) =>
        new(
            result.BirdId,
            result.Name,
            result.Sex.ToString(),
            result.BirthDate,
            result.RingNumber,
            result.Status.ToString());

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
    string? ExternalFatherSex,
    Guid? MotherBirdId,
    string? ExternalMotherName,
    string? ExternalMotherSex,
    string? Notes);

public sealed record UpdateBirdRequest(
    string? Name,
    string? Sex,
    Guid? SpeciesId,
    DateOnly? BirthDate,
    string? RingNumber,
    string? Notes);

public sealed record UpdateBirdGenealogyRequest(
    Guid? FatherBirdId,
    string? ExternalFatherName,
    string? ExternalFatherSex,
    Guid? MotherBirdId,
    string? ExternalMotherName,
    string? ExternalMotherSex);

public sealed record ChangeBirdStatusRequest(
    string? Status,
    bool Confirmed,
    DateOnly? DeathDate,
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
    string? ExternalFatherSex,
    Guid? MotherBirdId,
    string? ExternalMotherName,
    string? ExternalMotherSex,
    string? Notes,
    string Status,
    bool IdentificationPending,
    int? AgeInYears,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record BirdListResponse(
    Guid BreedingFarmId,
    IReadOnlyCollection<BirdListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record BirdListItemResponse(
    Guid BirdId,
    string Name,
    Guid SpeciesId,
    string SpeciesScientificName,
    string SpeciesPopularName,
    string Sex,
    DateOnly? BirthDate,
    string? RingNumber,
    string Status,
    bool IdentificationPending,
    int? AgeInYears,
    DateTimeOffset CreatedAtUtc);

public sealed record BirdDetailsResponse(
    Guid BirdId,
    Guid? GenealogyRootId,
    Guid BreedingFarmId,
    string Name,
    Guid SpeciesId,
    string SpeciesScientificName,
    string SpeciesPopularName,
    string Sex,
    DateOnly? BirthDate,
    DateOnly? DeathDate,
    string? RingNumber,
    Guid? FatherBirdId,
    BirdParentResponse? Father,
    string? ExternalFatherName,
    string? ExternalFatherSex,
    Guid? MotherBirdId,
    BirdParentResponse? Mother,
    string? ExternalMotherName,
    string? ExternalMotherSex,
    string? Notes,
    string Status,
    bool IdentificationPending,
    int? AgeInYears,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record BirdParentResponse(
    Guid BirdId,
    string Name,
    string Sex,
    DateOnly? BirthDate,
    string? RingNumber,
    string Status);

public sealed record BirdEligibilityResponse(
    Guid BirdId,
    bool IsEligible,
    bool IdentificationPending,
    IReadOnlyCollection<BirdEligibilityIssueResponse> Issues);

public sealed record BirdEligibilityIssueResponse(
    string Code,
    string Message);

public sealed record BirdParentOptionsResponse(
    Guid BreedingFarmId,
    IReadOnlyCollection<BirdParentOptionResponse> Items);

public sealed record BirdParentOptionResponse(
    Guid BirdId,
    string Name,
    string Sex,
    DateOnly? BirthDate,
    string? RingNumber);
