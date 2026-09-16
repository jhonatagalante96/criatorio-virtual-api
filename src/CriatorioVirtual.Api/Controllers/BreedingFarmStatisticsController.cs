using System.Security.Claims;
using CriatorioVirtual.Application.BreedingFarmStatistics;
using CriatorioVirtual.Application.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

/// <summary>
/// Returns pre-aggregated statistics for the authenticated owner's selected breeding farm.
/// Date ranges are inclusive UTC calendar dates; omitted dates default to the last 30 UTC days.
/// </summary>
[ApiController]
[Route("api/breeding-farms/current/statistics")]
[Authorize]
public sealed class BreedingFarmStatisticsController(IQueryExecutor queryExecutor) : ControllerBase
{
    public const int MaximumRangeDays = 366;

    [HttpGet(Name = "GetBreedingFarmStatistics")]
    [ProducesResponseType(typeof(BreedingFarmStatisticsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetAsync(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401");
        }

        var utcToday = DateOnly.FromDateTime(DateTime.UtcNow);
        var endDate = to ?? utcToday;
        var startDate = from ?? (endDate.DayNumber < 29
            ? DateOnly.MinValue
            : endDate.AddDays(-29));

        if (startDate > endDate)
        {
            return InvalidDateRange("The start date must be on or before the end date.");
        }

        if (endDate > utcToday)
        {
            return InvalidDateRange("The end date cannot be in the future (UTC).");
        }

        if (endDate.DayNumber - startDate.DayNumber + 1 > MaximumRangeDays)
        {
            return InvalidDateRange($"The date range cannot exceed {MaximumRangeDays} days.");
        }

        var result = await queryExecutor.Execute<GetBreedingFarmStatisticsQuery, GetBreedingFarmStatisticsResult>(
            new GetBreedingFarmStatisticsQuery(userId, startDate, endDate),
            cancellationToken);

        return result.Status switch
        {
            GetBreedingFarmStatisticsStatus.Success => Ok(ToResponse(result.Statistics!)),
            GetBreedingFarmStatisticsStatus.UserNotFound => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401"),
            GetBreedingFarmStatisticsStatus.BreedingFarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A breeding farm must be selected before consulting statistics.",
                type: "https://httpstatuses.com/409"),
            GetBreedingFarmStatisticsStatus.BreedingFarmNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The selected breeding farm was not found.",
                type: "https://httpstatuses.com/404"),
            _ => throw new InvalidOperationException("The breeding farm statistics result is not supported.")
        };
    }

    private IActionResult InvalidDateRange(string detail) => Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "The statistics date range is invalid.",
        detail: detail,
        type: "https://httpstatuses.com/400");

    private static BreedingFarmStatisticsResponse ToResponse(BreedingFarmStatisticsData statistics) =>
        new(
            statistics.BreedingFarmId,
            statistics.From,
            statistics.To,
            statistics.BirdsByStatus
                .Select(item => new BirdCountByStatusResponse(item.Status, item.Count))
                .ToArray(),
            statistics.BirdsBySex
                .Select(item => new BirdCountBySexResponse(item.Sex, item.Count))
                .ToArray(),
            statistics.BirdsBySpecies
                .Select(item => new BirdCountBySpeciesResponse(
                    item.SpeciesId,
                    item.PopularName,
                    item.ScientificName,
                    item.Count))
                .ToArray(),
            statistics.Daily
                .Select(item => new DailyBreedingFarmStatisticsResponse(
                    item.Date,
                    item.BirdsRegisteredCount,
                    item.BirthsRecordedCount,
                    item.ReproductionsStartedCount,
                    item.ReproductionsCompletedCount,
                    item.InternalTransfersInCount,
                    item.InternalTransfersOutCount,
                    item.ExternalTransfersOutCount))
                .ToArray(),
            new BreedingFarmTransferStatisticsResponse(
                statistics.Transfers.InternalTransfersInCount,
                statistics.Transfers.InternalTransfersOutCount,
                statistics.Transfers.ExternalTransfersOutCount,
                ToResponse(statistics.Transfers.CurrentIncomingRequestsByStatus),
                ToResponse(statistics.Transfers.CurrentOutgoingRequestsByStatus)));

    private static IReadOnlyCollection<TransferRequestStatusCountResponse> ToResponse(
        IReadOnlyCollection<TransferRequestStatusCount> counts) =>
        counts.Select(item => new TransferRequestStatusCountResponse(item.Status, item.Count)).ToArray();
}

public sealed record BreedingFarmStatisticsResponse(
    Guid BreedingFarmId,
    DateOnly From,
    DateOnly To,
    IReadOnlyCollection<BirdCountByStatusResponse> BirdsByStatus,
    IReadOnlyCollection<BirdCountBySexResponse> BirdsBySex,
    IReadOnlyCollection<BirdCountBySpeciesResponse> BirdsBySpecies,
    IReadOnlyCollection<DailyBreedingFarmStatisticsResponse> Daily,
    BreedingFarmTransferStatisticsResponse Transfers);

public sealed record BirdCountByStatusResponse(string Status, int Count);

public sealed record BirdCountBySexResponse(string Sex, int Count);

public sealed record BirdCountBySpeciesResponse(Guid SpeciesId, string PopularName, string ScientificName, int Count);

public sealed record DailyBreedingFarmStatisticsResponse(
    DateOnly Date,
    int BirdsRegisteredCount,
    int BirthsRecordedCount,
    int ReproductionsStartedCount,
    int ReproductionsCompletedCount,
    int InternalTransfersInCount,
    int InternalTransfersOutCount,
    int ExternalTransfersOutCount);

public sealed record BreedingFarmTransferStatisticsResponse(
    int InternalTransfersInCount,
    int InternalTransfersOutCount,
    int ExternalTransfersOutCount,
    IReadOnlyCollection<TransferRequestStatusCountResponse> CurrentIncomingRequestsByStatus,
    IReadOnlyCollection<TransferRequestStatusCountResponse> CurrentOutgoingRequestsByStatus);

public sealed record TransferRequestStatusCountResponse(string Status, int Count);
