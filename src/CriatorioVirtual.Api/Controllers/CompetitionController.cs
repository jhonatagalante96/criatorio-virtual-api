using System.Security.Claims;
using CriatorioVirtual.Application.Competitions;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Competitions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/competitions")]
[Authorize]
public sealed class CompetitionController(IQueryExecutor queryExecutor) : ControllerBase
{
    [HttpGet(Name = "ListCompetitions")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(CompetitionListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] Guid? birdId,
        [FromQuery] string? category,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] string? search,
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

        var errors = ValidateListRequest(birdId, category, fromDate, toDate, search, page, pageSize);
        if (errors.Count > 0)
        {
            return ValidationProblemResult(errors, "Competition listing parameters are invalid.");
        }

        var result = await queryExecutor.Execute<
            ListCompetitionsQuery,
            ListCompetitionsResult>(
            new ListCompetitionsQuery(
                userId,
                birdId,
                category,
                fromDate,
                toDate,
                search,
                page,
                pageSize),
            cancellationToken);

        return result.Status switch
        {
            ListCompetitionsStatus.Success => Ok(ToResponse(result)),
            ListCompetitionsStatus.UserNotFound => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401"),
            ListCompetitionsStatus.BreedingFarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A breeding farm must be selected before listing competitions.",
                type: "https://httpstatuses.com/409"),
            ListCompetitionsStatus.BreedingFarmNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The selected breeding farm was not found.",
                type: "https://httpstatuses.com/404"),
            _ => throw new InvalidOperationException("The competition listing result is not supported.")
        };
    }

    private IActionResult ValidationProblemResult(
        IReadOnlyDictionary<string, string[]> errors,
        string title = "Competition listing parameters are invalid.")
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
        Guid? birdId,
        string? category,
        DateOnly? fromDate,
        DateOnly? toDate,
        string? search,
        int page,
        int pageSize)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (birdId == Guid.Empty)
        {
            errors[nameof(birdId)] = ["The bird identifier cannot be empty."];
        }

        if (category?.Trim().Length > BirdCompetition.CategoryMaxLength)
        {
            errors[nameof(category)] = ["The competition category cannot exceed 200 characters."];
        }

        if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
        {
            errors[nameof(toDate)] = ["The toDate must be on or after fromDate."];
        }

        if (search?.Trim().Length > 100)
        {
            errors[nameof(search)] = ["The competition search cannot exceed 100 characters."];
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

    private static CompetitionListResponse ToResponse(ListCompetitionsResult result) =>
        new(
            result.BreedingFarmId!.Value,
            result.Items.Select(ToResponse).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount,
            result.TotalCount == 0
                ? 0
                : (int)Math.Ceiling(result.TotalCount / (double)result.PageSize));

    private static CompetitionListItemResponse ToResponse(CompetitionListItemResult result) =>
        new(
            result.CompetitionId,
            result.BreedingFarmId,
            new CompetitionBirdResponse(
                result.Bird.BirdId,
                result.Bird.Name,
                result.Bird.RingNumber),
            result.Name,
            result.CompetitionDate,
            result.Category,
            result.Placement,
            result.Location,
            result.Notes,
            result.CreatedAtUtc,
            result.UpdatedAtUtc);
}

public sealed record CompetitionBirdResponse(
    Guid BirdId,
    string Name,
    string? RingNumber);

public sealed record CompetitionListItemResponse(
    Guid CompetitionId,
    Guid BreedingFarmId,
    CompetitionBirdResponse Bird,
    string Name,
    DateOnly? Date,
    string? Category,
    int? Placement,
    string? Location,
    string? Notes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CompetitionListResponse(
    Guid BreedingFarmId,
    IReadOnlyCollection<CompetitionListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
