using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Claims;
using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/billing/subscriptions")]
[Authorize]
public sealed class BillingSubscriptionController(ICommandExecutor commandExecutor) : ControllerBase
{
    [HttpPost(Name = "CreateBillingSubscription")]
    [ProducesResponseType(typeof(CreateBillingSubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CreateBillingSubscriptionResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateBillingSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401");
        }

        var billingCycle = ParseBillingCycle(request.BillingCycle);
        if (billingCycle is null)
        {
            ModelState.AddModelError(nameof(request.BillingCycle), "Choose either monthly or annual billing.");
            return ValidationProblem(ModelState);
        }

        var taxIdentifier = NormalizeTaxIdentifier(request.CustomerTaxIdentifier);
        if (taxIdentifier is null)
        {
            ModelState.AddModelError(nameof(request.CustomerTaxIdentifier), "Enter a valid CPF or CNPJ format.");
            return ValidationProblem(ModelState);
        }

        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        if (remoteIp is null)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The request could not be associated with a client IP address.",
                type: "https://httpstatuses.com/400");
        }

        var result = await commandExecutor.Execute<CreateBillingSubscriptionCommand, CreateBillingSubscriptionResult>(
            new CreateBillingSubscriptionCommand(
                userId,
                billingCycle.Value,
                taxIdentifier,
                request.CardToken!.Trim(),
                remoteIp),
            cancellationToken);

        return result.Status switch
        {
            CreateBillingSubscriptionStatus.PendingConfirmation => Accepted(ToResponse(result)),
            CreateBillingSubscriptionStatus.TrialStarted or
                CreateBillingSubscriptionStatus.SubscriptionAlreadyExists => Ok(ToResponse(result)),
            CreateBillingSubscriptionStatus.FarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Select a breeding farm before starting a subscription.",
                type: "https://httpstatuses.com/409"),
            CreateBillingSubscriptionStatus.FarmNotFound or
                CreateBillingSubscriptionStatus.NotFarmOwner => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The selected breeding farm was not found.",
                type: "https://httpstatuses.com/404"),
            CreateBillingSubscriptionStatus.BillingCycleConflict => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The selected breeding farm already has a subscription in progress or a different billing cycle.",
                type: "https://httpstatuses.com/409"),
            CreateBillingSubscriptionStatus.PlanNotConfigured => Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The subscription plan is not configured.",
                type: "https://httpstatuses.com/503"),
            CreateBillingSubscriptionStatus.GatewayUnavailable => Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The billing gateway could not complete the subscription request.",
                type: "https://httpstatuses.com/502"),
            _ => throw new InvalidOperationException("The billing subscription result is not supported.")
        };
    }

    [HttpDelete(Name = "CancelBillingSubscription")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> CancelAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401");
        }

        var result = await commandExecutor.Execute<CancelBillingSubscriptionCommand, CancelBillingSubscriptionResult>(
            new CancelBillingSubscriptionCommand(userId),
            cancellationToken);

        return result.Status switch
        {
            CancelBillingSubscriptionStatus.Success => NoContent(),
            CancelBillingSubscriptionStatus.UserNotFound => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401"),
            CancelBillingSubscriptionStatus.BreedingFarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Select a breeding farm before cancelling a subscription.",
                type: "https://httpstatuses.com/409"),
            CancelBillingSubscriptionStatus.BreedingFarmNotFound or
                CancelBillingSubscriptionStatus.NotFarmOwner => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The selected breeding farm was not found.",
                type: "https://httpstatuses.com/404"),
            CancelBillingSubscriptionStatus.SubscriptionNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The selected breeding farm has no subscription.",
                type: "https://httpstatuses.com/404"),
            CancelBillingSubscriptionStatus.SubscriptionNotCancelable => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The subscription is not ready to be cancelled.",
                type: "https://httpstatuses.com/409"),
            CancelBillingSubscriptionStatus.GatewayUnavailable => Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The billing gateway could not cancel the subscription.",
                type: "https://httpstatuses.com/502"),
            _ => throw new InvalidOperationException("The subscription cancellation result is not supported.")
        };
    }

    private static BillingCycle? ParseBillingCycle(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "monthly" => BillingCycle.Monthly,
        "annual" => BillingCycle.Annual,
        _ => null
    };

    private static string? NormalizeTaxIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                !char.IsWhiteSpace(character) &&
                character is not '.' and not '-' and not '/'))
        {
            return null;
        }

        var normalized = new string(value.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        var isCpf = normalized.Length == 11 && normalized.All(char.IsAsciiDigit);
        var isCnpj = normalized.Length == 14 && normalized.All(char.IsAsciiLetterOrDigit);
        return isCpf || isCnpj ? normalized : null;
    }

    private static CreateBillingSubscriptionResponse ToResponse(CreateBillingSubscriptionResult result) =>
        new(
            result.SubscriptionId!.Value,
            result.PlanCode!,
            ToCycle(result.BillingCycle!.Value),
            result.Amount,
            "BRL",
            result.Status == CreateBillingSubscriptionStatus.SubscriptionAlreadyExists
                ? "alreadyExists"
                : result.Status == CreateBillingSubscriptionStatus.PendingConfirmation
                    ? "pendingConfirmation"
                    : "trialStarted",
            result.SubscriptionStatus?.ToString(),
            result.TrialStartedAtUtc,
            result.TrialEndsAtUtc,
            result.NextChargeDueAtUtc);

    private static string ToCycle(BillingCycle billingCycle) => billingCycle switch
    {
        BillingCycle.Monthly => "monthly",
        BillingCycle.Annual => "annual",
        _ => throw new ArgumentOutOfRangeException(nameof(billingCycle), billingCycle, "The billing cycle is not supported.")
    };
}

public sealed record CreateBillingSubscriptionRequest
{
    [Required]
    [StringLength(10)]
    public string? BillingCycle { get; init; }

    [Required]
    [StringLength(32)]
    public string? CustomerTaxIdentifier { get; init; }

    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string? CardToken { get; init; }
}

public sealed record CreateBillingSubscriptionResponse(
    Guid SubscriptionId,
    string PlanCode,
    string BillingCycle,
    decimal? Amount,
    string CurrencyCode,
    string PurchaseOutcome,
    string? Status,
    DateTimeOffset? TrialStartedAtUtc,
    DateTimeOffset? TrialEndsAtUtc,
    DateTimeOffset? FirstChargeDueAtUtc);
