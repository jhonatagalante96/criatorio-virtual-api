using System.Security.Claims;
using CriatorioVirtual.Application.Dashboard;
using CriatorioVirtual.Application.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public sealed class DashboardController(IQueryExecutor queryExecutor) : ControllerBase
{
    [HttpGet(Name = "GetDashboard")]
    [ProducesResponseType(typeof(DashboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401");
        }

        var result = await queryExecutor.Execute<GetDashboardQuery, GetDashboardResult>(
            new GetDashboardQuery(userId),
            cancellationToken);

        return result.Status switch
        {
            GetDashboardStatus.Success => Ok(ToResponse(result.Dashboard!)),
            GetDashboardStatus.UserNotFound => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401"),
            GetDashboardStatus.BreedingFarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A breeding farm must be selected before consulting the dashboard.",
                type: "https://httpstatuses.com/409"),
            GetDashboardStatus.BreedingFarmNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The selected breeding farm was not found.",
                type: "https://httpstatuses.com/404"),
            _ => throw new InvalidOperationException("The dashboard result is not supported.")
        };
    }

    private static DashboardResponse ToResponse(DashboardResult result) =>
        new(
            result.BreedingFarmId,
            new DashboardIndicatorsResponse(
                result.Indicators.ActiveBirdCount,
                result.Indicators.PendingIdentificationCount,
                result.Indicators.ActiveReproductionCount),
            result.Pending
                .Select(pending => new DashboardPendingResponse(
                    pending.Code,
                    pending.ResourceType,
                    pending.Title,
                    pending.Count))
                .ToArray(),
            result.Activities
                .Select(activity => new DashboardActivityResponse(
                    activity.ActivityType,
                    activity.ResourceType,
                    activity.ResourceId,
                    activity.Title,
                    activity.OccurredAtUtc))
                .ToArray());
}

public sealed record DashboardResponse(
    Guid BreedingFarmId,
    DashboardIndicatorsResponse Indicators,
    IReadOnlyCollection<DashboardPendingResponse> Pending,
    IReadOnlyCollection<DashboardActivityResponse> Activities);

public sealed record DashboardIndicatorsResponse(
    int ActiveBirdCount,
    int PendingIdentificationCount,
    int ActiveReproductionCount);

public sealed record DashboardPendingResponse(
    string Code,
    string ResourceType,
    string Title,
    int Count);

public sealed record DashboardActivityResponse(
    string ActivityType,
    string ResourceType,
    Guid ResourceId,
    string Title,
    DateTimeOffset OccurredAtUtc);
