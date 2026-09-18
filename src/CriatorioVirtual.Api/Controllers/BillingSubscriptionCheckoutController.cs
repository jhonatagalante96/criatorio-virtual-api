using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/billing/subscription-checkouts")]
[Authorize]
public sealed class BillingSubscriptionCheckoutController(ICommandExecutor commandExecutor) : ControllerBase
{
    [HttpPost(Name = "CreateBillingSubscriptionCheckout")]
    [ProducesResponseType(typeof(CreateBillingSubscriptionCheckoutResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateBillingSubscriptionCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401");
        }

        var billingCycle = request.BillingCycle?.Trim().ToLowerInvariant() switch
        {
            "monthly" => BillingCycle.Monthly,
            "annual" => BillingCycle.Annual,
            _ => (BillingCycle?)null
        };
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

        var result = await commandExecutor.Execute<
            CreateBillingSubscriptionCheckoutCommand,
            CreateBillingSubscriptionCheckoutResult>(
            new CreateBillingSubscriptionCheckoutCommand(userId, billingCycle.Value, taxIdentifier),
            cancellationToken);

        return result.Status switch
        {
            CreateBillingSubscriptionCheckoutStatus.PendingCheckout or
                CreateBillingSubscriptionCheckoutStatus.CheckoutAlreadyExists =>
                result.SubscriptionId is { } subscriptionId &&
                result.CheckoutId is { } checkoutId &&
                result.CheckoutUrl is { } checkoutUrl
                    ? Ok(new CreateBillingSubscriptionCheckoutResponse(
                        subscriptionId,
                        checkoutId,
                        checkoutUrl,
                        "pendingCheckout",
                        result.ExpiresAtUtc))
                    : Problem(
                        statusCode: StatusCodes.Status502BadGateway,
                        title: "The billing gateway did not return a usable subscription checkout.",
                        type: "https://httpstatuses.com/502"),
            CreateBillingSubscriptionCheckoutStatus.SubscriptionAlreadyExists => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The selected breeding farm already has an active subscription.",
                type: "https://httpstatuses.com/409"),
            CreateBillingSubscriptionCheckoutStatus.FarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Select a breeding farm before starting a subscription.",
                type: "https://httpstatuses.com/409"),
            CreateBillingSubscriptionCheckoutStatus.FarmNotFound or
                CreateBillingSubscriptionCheckoutStatus.NotFarmOwner => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The selected breeding farm was not found.",
                type: "https://httpstatuses.com/404"),
            CreateBillingSubscriptionCheckoutStatus.BillingCycleConflict => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The selected breeding farm already has a subscription in progress or a different billing cycle.",
                type: "https://httpstatuses.com/409"),
            CreateBillingSubscriptionCheckoutStatus.PlanNotConfigured => Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The subscription plan is not configured.",
                type: "https://httpstatuses.com/503"),
            CreateBillingSubscriptionCheckoutStatus.GatewayUnavailable => Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The billing gateway could not complete the subscription checkout request.",
                type: "https://httpstatuses.com/502"),
            _ => throw new InvalidOperationException("The subscription checkout result is not supported.")
        };
    }

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
}

public sealed record CreateBillingSubscriptionCheckoutRequest
{
    [Required]
    [StringLength(10)]
    public string? BillingCycle { get; init; }

    [Required]
    [StringLength(32)]
    public string? CustomerTaxIdentifier { get; init; }
}

public sealed record CreateBillingSubscriptionCheckoutResponse(
    Guid SubscriptionId,
    string CheckoutId,
    string CheckoutUrl,
    string Status,
    DateTimeOffset? ExpiresAtUtc);
