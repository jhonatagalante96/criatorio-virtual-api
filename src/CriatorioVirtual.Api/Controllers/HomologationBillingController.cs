using System.Security.Claims;
using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Infrastructure.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/homologation/billing")]
[Authorize]
public sealed class HomologationBillingController(
    ICommandExecutor commandExecutor,
    IHostEnvironment environment) : ControllerBase
{
    [HttpPost("simulation")]
    [ProducesResponseType(typeof(SimulateHomologationSubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SimulateAsync(
        [FromBody] SimulateHomologationSubscriptionRequest? request,
        CancellationToken cancellationToken)
    {
        if (!environment.IsEnvironment(AsaasOptions.HomologationEnvironmentName) &&
            !environment.IsEnvironment("Testing"))
        {
            return NotFound();
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401");
        }

        var stateString = request?.State?.Trim();
        var simulatedState = ParseSimulatedState(stateString);
        if (simulatedState is null)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid simulation state.",
                detail: "Valid states are: none, pendingSubscription, trial, active, gracePeriod, blocked, cancelled.",
                type: "https://httpstatuses.com/400");
        }

        BillingCycle? billingCycle = null;
        if (!string.IsNullOrWhiteSpace(request?.BillingCycle))
        {
            billingCycle = ParseBillingCycle(request.BillingCycle);
            if (billingCycle is null)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid billing cycle.",
                    detail: "Valid billing cycles are: Monthly, Annual.",
                    type: "https://httpstatuses.com/400");
            }
        }

        var command = new SimulateHomologationSubscriptionCommand(
            userId,
            request?.BreedingFarmId,
            simulatedState.Value,
            request?.PlanCode,
            billingCycle);

        var result = await commandExecutor.Execute<
            SimulateHomologationSubscriptionCommand,
            SimulateHomologationSubscriptionResult>(command, cancellationToken);

        return result.Status switch
        {
            SimulateHomologationSubscriptionStatus.Success => Ok(new SimulateHomologationSubscriptionResponse(
                result.BreedingFarmId!.Value,
                result.SubscriptionId,
                result.State.ToString().ToLowerInvariant(),
                result.SubscriptionStatus?.ToString(),
                result.BillingCycle?.ToString(),
                result.AgreedAmount,
                result.TrialStartedAtUtc,
                result.TrialEndsAtUtc,
                result.NextChargeDueAtUtc,
                result.GracePeriodStartedAtUtc,
                result.GracePeriodEndsAtUtc)),

            SimulateHomologationSubscriptionStatus.FarmNotSelected => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "A breeding farm must be selected.",
                type: "https://httpstatuses.com/400"),

            SimulateHomologationSubscriptionStatus.FarmNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The breeding farm could not be found.",
                type: "https://httpstatuses.com/404"),

            SimulateHomologationSubscriptionStatus.NotFarmOwner => Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "The current account does not have permission to manage billing for this breeding farm.",
                type: "https://httpstatuses.com/403"),

            _ => throw new InvalidOperationException($"The result status '{result.Status}' is not supported.")
        };
    }

    private static SimulatedSubscriptionState? ParseSimulatedState(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return SimulatedSubscriptionState.Active;
        }

        return value.ToLowerInvariant() switch
        {
            "none" or "nosubscription" => SimulatedSubscriptionState.None,
            "pendingsubscription" or "pending" => SimulatedSubscriptionState.PendingSubscription,
            "trial" => SimulatedSubscriptionState.Trial,
            "active" => SimulatedSubscriptionState.Active,
            "graceperiod" or "grace" => SimulatedSubscriptionState.GracePeriod,
            "blocked" => SimulatedSubscriptionState.Blocked,
            "cancelled" or "canceled" => SimulatedSubscriptionState.Cancelled,
            _ => null
        };
    }

    private static BillingCycle? ParseBillingCycle(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "monthly" => BillingCycle.Monthly,
            "annual" => BillingCycle.Annual,
            _ => null
        };
    }
}

public sealed record SimulateHomologationSubscriptionRequest(
    string? State,
    Guid? BreedingFarmId,
    string? PlanCode,
    string? BillingCycle);

public sealed record SimulateHomologationSubscriptionResponse(
    Guid BreedingFarmId,
    Guid? SubscriptionId,
    string State,
    string? SubscriptionStatus,
    string? BillingCycle,
    decimal? AgreedAmount,
    DateTimeOffset? TrialStartedAtUtc,
    DateTimeOffset? TrialEndsAtUtc,
    DateTimeOffset? NextChargeDueAtUtc,
    DateTimeOffset? GracePeriodStartedAtUtc,
    DateTimeOffset? GracePeriodEndsAtUtc);
