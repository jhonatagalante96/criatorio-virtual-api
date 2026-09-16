using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Application.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/billing")]
[Authorize]
public sealed class BillingQueryController(IQueryExecutor queryExecutor) : ControllerBase
{
    [HttpGet("subscription", Name = "GetBillingSubscription")]
    [ProducesResponseType(typeof(BillingSubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetSubscriptionAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return AuthenticationRequired();
        }

        var result = await queryExecutor.Execute<GetSubscriptionQuery, GetSubscriptionResult>(
            new GetSubscriptionQuery(userId),
            cancellationToken);

        return result.Status switch
        {
            BillingQueryStatus.Success => Ok(ToResponse(result.Subscription!)),
            BillingQueryStatus.UserNotFound => AuthenticationRequired(),
            BillingQueryStatus.BreedingFarmNotSelected => BreedingFarmNotSelected(),
            BillingQueryStatus.BreedingFarmNotFound => BreedingFarmNotFound(),
            BillingQueryStatus.SubscriptionNotFound => SubscriptionNotFound(),
            _ => throw new InvalidOperationException("The subscription query result is not supported.")
        };
    }

    [HttpGet("payments", Name = "ListBillingPayments")]
    [ProducesResponseType(typeof(BillingPaymentsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListPaymentsAsync(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, BillingQueryLimits.MaxPageSize)] int pageSize = BillingQueryLimits.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return AuthenticationRequired();
        }

        var result = await queryExecutor.Execute<ListPaymentsQuery, ListPaymentsResult>(
            new ListPaymentsQuery(userId, page, pageSize),
            cancellationToken);

        return result.Status switch
        {
            BillingQueryStatus.Success => Ok(ToResponse(result)),
            BillingQueryStatus.UserNotFound => AuthenticationRequired(),
            BillingQueryStatus.BreedingFarmNotSelected => BreedingFarmNotSelected(),
            BillingQueryStatus.BreedingFarmNotFound => BreedingFarmNotFound(),
            _ => throw new InvalidOperationException("The payment query result is not supported.")
        };
    }

    private IActionResult AuthenticationRequired() => Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Authentication is required.",
        type: "https://httpstatuses.com/401");

    private IActionResult BreedingFarmNotSelected() => Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "A breeding farm must be selected before consulting billing.",
        type: "https://httpstatuses.com/409");

    private IActionResult BreedingFarmNotFound() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "The selected breeding farm was not found.",
        type: "https://httpstatuses.com/404");

    private IActionResult SubscriptionNotFound() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "The selected breeding farm has no subscription.",
        type: "https://httpstatuses.com/404");

    private static BillingSubscriptionResponse ToResponse(SubscriptionQueryResult subscription) =>
        new(
            subscription.BreedingFarmId,
            subscription.PlanCode,
            subscription.BillingCycle.ToString(),
            subscription.Status.ToString(),
            subscription.TrialStartedAtUtc,
            subscription.TrialEndsAtUtc,
            subscription.FirstChargeDueAtUtc,
            subscription.NextChargeDueAtUtc,
            subscription.GracePeriodStartedAtUtc,
            subscription.GracePeriodEndsAtUtc,
            subscription.GracePeriodDaysRemaining,
            subscription.CreatedAtUtc,
            subscription.UpdatedAtUtc);

    private static BillingPaymentsResponse ToResponse(ListPaymentsResult result) =>
        new(
            result.BreedingFarmId!.Value,
            result.Items
                .Select(payment => new BillingPaymentResponse(
                    payment.PaymentId,
                    payment.Amount,
                    payment.CurrencyCode,
                    payment.DueAtUtc,
                    payment.Status.ToString(),
                    payment.PaidAtUtc,
                    payment.CreatedAtUtc))
                .ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount);
}

public sealed record BillingSubscriptionResponse(
    Guid BreedingFarmId,
    string PlanCode,
    string BillingCycle,
    string Status,
    DateTimeOffset? TrialStartedAtUtc,
    DateTimeOffset? TrialEndsAtUtc,
    DateTimeOffset? FirstChargeDueAtUtc,
    DateTimeOffset? NextChargeDueAtUtc,
    DateTimeOffset? GracePeriodStartedAtUtc,
    DateTimeOffset? GracePeriodEndsAtUtc,
    int? GracePeriodDaysRemaining,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record BillingPaymentsResponse(
    Guid BreedingFarmId,
    IReadOnlyCollection<BillingPaymentResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record BillingPaymentResponse(
    Guid PaymentId,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset DueAtUtc,
    string Status,
    DateTimeOffset? PaidAtUtc,
    DateTimeOffset CreatedAtUtc);
