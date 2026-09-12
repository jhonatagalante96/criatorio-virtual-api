using System.Security.Claims;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.Transfers;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Transfers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/internal-transfers")]
[Authorize]
public sealed class InternalTransferController(
    ICommandExecutor commandExecutor,
    IQueryExecutor queryExecutor) : ControllerBase
{
    [HttpGet("destinations", Name = "SearchInternalTransferDestinations")]
    [ProducesResponseType(typeof(InternalTransferDestinationListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SearchDestinationsAsync(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var errors = ValidateSearchRequest(search, page, pageSize);
        if (errors.Count > 0)
        {
            return ValidationProblemResult(errors, "Internal transfer destination search parameters are invalid.");
        }

        var result = await queryExecutor.Execute<
            SearchInternalTransferDestinationsQuery,
            SearchInternalTransferDestinationsResult>(
            new SearchInternalTransferDestinationsQuery(
                userId,
                string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                page,
                pageSize),
            cancellationToken);

        return result.Status switch
        {
            SearchInternalTransferDestinationsStatus.Success => Ok(ToResponse(result)),
            SearchInternalTransferDestinationsStatus.UserNotFound => AuthenticationRequired(),
            SearchInternalTransferDestinationsStatus.BreedingFarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A breeding farm must be selected before searching transfer destinations.",
                type: "https://httpstatuses.com/409"),
            SearchInternalTransferDestinationsStatus.BreedingFarmNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The selected breeding farm was not found.",
                type: "https://httpstatuses.com/404"),
            _ => throw new InvalidOperationException("The internal transfer destination search result is not supported.")
        };
    }

    [HttpPost(Name = "RequestInternalTransfer")]
    [ProducesResponseType(typeof(InternalTransferRequestResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestAsync(
        [FromBody] RequestInternalTransferRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        if (request is null)
        {
            return ValidationProblemResult(new Dictionary<string, string[]>
            {
                ["request"] = ["The request body is required."]
            });
        }

        var requestErrors = ValidateRequest(request);
        if (requestErrors.Count > 0)
        {
            return ValidationProblemResult(requestErrors, "Internal transfer request data is invalid.");
        }

        try
        {
            var result = await commandExecutor.Execute<RequestInternalTransferCommand, RequestInternalTransferResult>(
                new RequestInternalTransferCommand(
                    userId,
                    request.BirdId,
                    request.DestinationBreedingFarmId,
                    request.Confirmed),
                cancellationToken);

            return result.Status switch
            {
                RequestInternalTransferStatus.Created => Created(
                    $"/api/internal-transfers/{result.TransferRequest!.TransferRequestId}",
                    ToResponse(result.TransferRequest)),
                RequestInternalTransferStatus.UserNotFound => AuthenticationRequired(),
                RequestInternalTransferStatus.BreedingFarmNotSelected => Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "A breeding farm must be selected before requesting an internal transfer.",
                    type: "https://httpstatuses.com/409"),
                RequestInternalTransferStatus.BreedingFarmNotFound or
                RequestInternalTransferStatus.BirdNotFound => Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "The bird was not found.",
                    type: "https://httpstatuses.com/404"),
                RequestInternalTransferStatus.DestinationBreedingFarmNotFound => Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "The destination breeding farm was not found.",
                    type: "https://httpstatuses.com/404"),
                RequestInternalTransferStatus.SameBreedingFarm => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        [nameof(request.DestinationBreedingFarmId)] =
                        ["The destination breeding farm must be different from the source breeding farm."]
                    }),
                RequestInternalTransferStatus.ConfirmationRequired => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        [nameof(request.Confirmed)] = ["Explicit confirmation is required."]
                    },
                    "Internal transfer request confirmation is required."),
                RequestInternalTransferStatus.BirdNotEligible => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        [nameof(request.BirdId)] =
                        ["The bird must be active and have a valid six-digit ring number to be transferred."]
                    }),
                RequestInternalTransferStatus.TransferPending => Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "The bird already has a pending internal transfer.",
                    type: "https://httpstatuses.com/409"),
                RequestInternalTransferStatus.InvalidData => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        ["request"] = ["The internal transfer request data is invalid."]
                    }),
                _ => throw new InvalidOperationException("The internal transfer request result is not supported.")
            };
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The bird or transfer request was changed by another request. Reload and try again.",
                Type = "https://httpstatuses.com/409"
            });
        }
        catch (DbUpdateException exception) when (IsPendingRequestUniqueViolation(exception))
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The bird already has a pending internal transfer.",
                Type = "https://httpstatuses.com/409"
            });
        }
    }

    [HttpPost("{transferRequestId:guid}/accept", Name = "AcceptInternalTransfer")]
    [ProducesResponseType(typeof(InternalTransferRequestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AcceptAsync(
        Guid transferRequestId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        try
        {
            var result = await commandExecutor.Execute<AcceptInternalTransferCommand, AcceptInternalTransferResult>(
                new AcceptInternalTransferCommand(userId, transferRequestId),
                cancellationToken);

            return result.Status switch
            {
                AcceptInternalTransferStatus.Accepted => Ok(ToResponse(result.TransferRequest!)),
                AcceptInternalTransferStatus.UserNotFound => AuthenticationRequired(),
                AcceptInternalTransferStatus.BreedingFarmNotSelected => Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "A breeding farm must be selected before accepting an internal transfer.",
                    type: "https://httpstatuses.com/409"),
                AcceptInternalTransferStatus.BreedingFarmNotFound or
                AcceptInternalTransferStatus.TransferRequestNotFound => Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "The internal transfer was not found.",
                    type: "https://httpstatuses.com/404"),
                AcceptInternalTransferStatus.TransferNotPending => Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "The internal transfer is no longer pending.",
                    type: "https://httpstatuses.com/409"),
                AcceptInternalTransferStatus.BirdNotFound or
                AcceptInternalTransferStatus.InvalidState => Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "The internal transfer cannot be accepted in its current state.",
                    type: "https://httpstatuses.com/409"),
                _ => throw new InvalidOperationException("The internal transfer acceptance result is not supported.")
            };
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The bird or transfer request was changed by another request. Reload and try again.",
                Type = "https://httpstatuses.com/409"
            });
        }
        catch (DbUpdateException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The internal transfer could not be completed because related data changed. Reload and try again.",
                Type = "https://httpstatuses.com/409"
            });
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The internal transfer cannot be completed while related records still depend on the source farm.",
                Type = "https://httpstatuses.com/409"
            });
        }
    }

    [HttpGet("sent", Name = "ListSentInternalTransfers")]
    [ProducesResponseType(typeof(InternalTransferListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> ListSentAsync(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        ListAsync(InternalTransferDirection.Sent, status, page, pageSize, cancellationToken);

    [HttpGet("received", Name = "ListReceivedInternalTransfers")]
    [ProducesResponseType(typeof(InternalTransferListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> ListReceivedAsync(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        ListAsync(InternalTransferDirection.Received, status, page, pageSize, cancellationToken);

    [HttpGet("{transferRequestId:guid}", Name = "GetInternalTransfer")]
    [ProducesResponseType(typeof(InternalTransferDetailsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetDetailsAsync(
        Guid transferRequestId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var result = await queryExecutor.Execute<GetInternalTransferRequestQuery, GetInternalTransferRequestResult>(
            new GetInternalTransferRequestQuery(userId, transferRequestId),
            cancellationToken);

        return result.Status switch
        {
            GetInternalTransferRequestStatus.Success => Ok(ToResponse(result.TransferRequest!)),
            GetInternalTransferRequestStatus.UserNotFound => AuthenticationRequired(),
            GetInternalTransferRequestStatus.BreedingFarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A breeding farm must be selected before consulting a transfer.",
                type: "https://httpstatuses.com/409"),
            GetInternalTransferRequestStatus.BreedingFarmNotFound or
            GetInternalTransferRequestStatus.TransferRequestNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The internal transfer was not found.",
                type: "https://httpstatuses.com/404"),
            _ => throw new InvalidOperationException("The internal transfer detail result is not supported.")
        };
    }

    private async Task<IActionResult> ListAsync(
        InternalTransferDirection direction,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var errors = ValidateListRequest(status, page, pageSize, out var parsedStatus);
        if (errors.Count > 0)
        {
            return ValidationProblemResult(errors, "Internal transfer listing parameters are invalid.");
        }

        var result = await queryExecutor.Execute<
            ListInternalTransferRequestsQuery,
            ListInternalTransferRequestsResult>(
            new ListInternalTransferRequestsQuery(userId, direction, parsedStatus, page, pageSize),
            cancellationToken);

        return result.Status switch
        {
            ListInternalTransferRequestsStatus.Success => Ok(ToResponse(result)),
            ListInternalTransferRequestsStatus.UserNotFound => AuthenticationRequired(),
            ListInternalTransferRequestsStatus.BreedingFarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A breeding farm must be selected before listing transfers.",
                type: "https://httpstatuses.com/409"),
            ListInternalTransferRequestsStatus.BreedingFarmNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The selected breeding farm was not found.",
                type: "https://httpstatuses.com/404"),
            _ => throw new InvalidOperationException("The internal transfer listing result is not supported.")
        };
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult AuthenticationRequired() =>
        Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Authentication is required.",
            type: "https://httpstatuses.com/401");

    private IActionResult ValidationProblemResult(
        IReadOnlyDictionary<string, string[]> errors,
        string title = "Internal transfer request data is invalid.")
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

    private static Dictionary<string, string[]> ValidateSearchRequest(
        string? search,
        int page,
        int pageSize)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (search?.Trim().Length > 100)
        {
            errors[nameof(search)] = ["The destination search cannot exceed 100 characters."];
        }

        if (page < 1)
        {
            errors[nameof(page)] = ["The page must be at least 1."];
        }

        if (pageSize is < 1 or > 50)
        {
            errors[nameof(pageSize)] = ["The pageSize must be between 1 and 50."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateRequest(RequestInternalTransferRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (request.BirdId is null || request.BirdId == Guid.Empty)
        {
            errors[nameof(request.BirdId)] = ["A bird identifier is required."];
        }

        if (request.DestinationBreedingFarmId is null || request.DestinationBreedingFarmId == Guid.Empty)
        {
            errors[nameof(request.DestinationBreedingFarmId)] = ["A destination breeding farm identifier is required."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateListRequest(
        string? status,
        int page,
        int pageSize,
        out InternalTransferRequestStatus? parsedStatus)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        parsedStatus = null;

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TryParseEnumName(status, out InternalTransferRequestStatus value))
            {
                errors[nameof(status)] = ["The internal transfer status is invalid."];
            }
            else
            {
                parsedStatus = value;
            }
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

    private static InternalTransferDestinationListResponse ToResponse(
        SearchInternalTransferDestinationsResult result) =>
        new(
            result.SourceBreedingFarmId!.Value,
            result.Items.Select(item => new InternalTransferDestinationResponse(
                item.BreedingFarmId,
                item.Name,
                item.ResponsibleName)).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount,
            CalculateTotalPages(result.TotalCount, result.PageSize));

    private static InternalTransferRequestResponse ToResponse(InternalTransferRequestResult result) =>
        new(
            result.TransferRequestId,
            result.BirdId,
            result.SourceBreedingFarmId,
            result.DestinationBreedingFarmId,
            result.Status,
            result.CreatedAtUtc,
            result.UpdatedAtUtc);

    private static InternalTransferListResponse ToResponse(ListInternalTransferRequestsResult result) =>
        new(
            result.Direction.ToString(),
            result.BreedingFarmId!.Value,
            result.Items.Select(item => new InternalTransferListItemResponse(
                item.TransferRequestId,
                item.BirdId,
                item.BirdName,
                item.RingNumber,
                item.SourceBreedingFarmId,
                item.SourceBreedingFarmName,
                item.DestinationBreedingFarmId,
                item.DestinationBreedingFarmName,
                item.Status.ToString(),
                item.CreatedAtUtc,
                item.UpdatedAtUtc)).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount,
            CalculateTotalPages(result.TotalCount, result.PageSize));

    private static InternalTransferDetailsResponse ToResponse(InternalTransferDetailsResult result) =>
        new(
            result.TransferRequestId,
            new InternalTransferBirdResponse(
                result.BirdId,
                result.BirdName,
                result.BirdSex.ToString(),
                result.RingNumber,
                result.BirdStatus.ToString()),
            result.SourceBreedingFarmId,
            result.SourceBreedingFarmName,
            result.DestinationBreedingFarmId,
            result.DestinationBreedingFarmName,
            result.Status.ToString(),
            result.CreatedAtUtc,
            result.UpdatedAtUtc);

    private static bool TryParseEnumName<TEnum>(string value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        var normalized = value.Trim();
        return Enum.TryParse(normalized, ignoreCase: true, out parsed) &&
            Enum.IsDefined(parsed) &&
            !normalized.All(char.IsAsciiDigit);
    }

    private static int CalculateTotalPages(int totalCount, int pageSize) =>
        (int)Math.Ceiling(totalCount / (double)pageSize);

    private static bool IsPendingRequestUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_internal_transfer_requests_bird_pending"
        } ||
        exception.GetBaseException() is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_internal_transfer_requests_bird_pending"
        };
}

public sealed record RequestInternalTransferRequest(
    Guid? BirdId,
    Guid? DestinationBreedingFarmId,
    bool Confirmed);

public sealed record InternalTransferDestinationListResponse(
    Guid SourceBreedingFarmId,
    IReadOnlyCollection<InternalTransferDestinationResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record InternalTransferDestinationResponse(
    Guid BreedingFarmId,
    string Name,
    string ResponsibleName);

public sealed record InternalTransferRequestResponse(
    Guid TransferRequestId,
    Guid BirdId,
    Guid SourceBreedingFarmId,
    Guid DestinationBreedingFarmId,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record InternalTransferListResponse(
    string Direction,
    Guid BreedingFarmId,
    IReadOnlyCollection<InternalTransferListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record InternalTransferListItemResponse(
    Guid TransferRequestId,
    Guid BirdId,
    string BirdName,
    string? RingNumber,
    Guid SourceBreedingFarmId,
    string SourceBreedingFarmName,
    Guid DestinationBreedingFarmId,
    string DestinationBreedingFarmName,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record InternalTransferDetailsResponse(
    Guid TransferRequestId,
    InternalTransferBirdResponse Bird,
    Guid SourceBreedingFarmId,
    string SourceBreedingFarmName,
    Guid DestinationBreedingFarmId,
    string DestinationBreedingFarmName,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record InternalTransferBirdResponse(
    Guid BirdId,
    string Name,
    string Sex,
    string? RingNumber,
    string Status);
