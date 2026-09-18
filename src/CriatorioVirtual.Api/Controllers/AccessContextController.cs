using System.Security.Claims;
using CriatorioVirtual.Application.Identity;
using CriatorioVirtual.Application.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/me/access-context")]
public sealed class AccessContextController(IQueryExecutor queryExecutor) : ControllerBase
{
    [HttpGet(Name = "GetAccessContext")]
    [ProducesResponseType(typeof(AccessContextResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401");
        }

        var avatarUrl = Url.RouteUrl("GetCurrentUserAvatar") ?? "/api/me/avatar";
        var result = await queryExecutor.Execute<GetAccessContextQuery, AccessContextResult?>(
            new GetAccessContextQuery(userId, avatarUrl),
            cancellationToken);

        if (result is null)
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401");
        }

        return Ok(ToResponse(result));
    }

    private static AccessContextResponse ToResponse(AccessContextResult result) =>
        new(
            new UserProfileResponse(
                result.User.Id,
                result.User.Name,
                result.User.Email,
                result.User.AvatarUrl),
            result.BreedingFarm is null
                ? null
                : new BreedingFarmAccessResponse(
                    result.BreedingFarm.Id,
                    result.BreedingFarm.Name,
                    result.BreedingFarm.Role),
            new OnboardingResponse(
                result.Onboarding.Status.ToString(),
                result.Onboarding.NextStep),
            new AccessDetailsResponse(
                result.Access.Status.ToString(),
                result.Access.CanAccessApp,
                result.Access.BlockedReason?.ToString(),
                result.Access.RequiredAction.ToString(),
                result.Access.TrialEndsAt,
                result.Access.GracePeriodEndsAt),
            result.Subscription is null
                ? null
                : new SubscriptionSummaryResponse(
                    result.Subscription.Plan,
                    result.Subscription.Cycle,
                    result.Subscription.Status));
}

public sealed record AccessContextResponse(
    UserProfileResponse User,
    BreedingFarmAccessResponse? BreedingFarm,
    OnboardingResponse Onboarding,
    AccessDetailsResponse Access,
    SubscriptionSummaryResponse? Subscription);

public sealed record UserProfileResponse(
    Guid Id,
    string Name,
    string Email,
    string? AvatarUrl);

public sealed record BreedingFarmAccessResponse(
    Guid Id,
    string Name,
    string Role);

public sealed record OnboardingResponse(
    string Status,
    string? NextStep);

public sealed record AccessDetailsResponse(
    string Status,
    bool CanAccessApp,
    string? BlockedReason,
    string RequiredAction,
    DateTimeOffset? TrialEndsAt,
    DateTimeOffset? GracePeriodEndsAt);

public sealed record SubscriptionSummaryResponse(
    string Plan,
    string Cycle,
    string Status);
