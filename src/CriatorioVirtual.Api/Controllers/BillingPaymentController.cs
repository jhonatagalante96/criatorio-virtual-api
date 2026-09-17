using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Application.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/billing/payments")]
[Authorize]
public sealed class BillingPaymentController(ICommandExecutor commandExecutor) : ControllerBase
{
    [HttpPost("{paymentId:guid}/attempts", Name = "RegularizeBillingPayment")]
    [ProducesResponseType(typeof(RegularizeBillingPaymentResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> AttemptAsync(
        Guid paymentId,
        [FromBody] RegularizeBillingPaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401");
        }

        if (!Guid.TryParse(idempotencyKey, out var parsedIdempotencyKey) || parsedIdempotencyKey == Guid.Empty)
        {
            ModelState.AddModelError("Idempotency-Key", "A non-empty GUID Idempotency-Key header is required.");
            return ValidationProblem(ModelState);
        }

        var result = await commandExecutor.Execute<RegularizeBillingPaymentCommand, RegularizeBillingPaymentResult>(
            new RegularizeBillingPaymentCommand(
                userId,
                paymentId,
                parsedIdempotencyKey,
                request.CardToken!),
            cancellationToken);

        return result.Status switch
        {
            RegularizeBillingPaymentStatus.AwaitingConfirmation => Accepted(ToResponse(result)),
            RegularizeBillingPaymentStatus.PaymentNotFound or
                RegularizeBillingPaymentStatus.NotFarmOwner => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The payment was not found for the selected breeding farm.",
                type: "https://httpstatuses.com/404"),
            RegularizeBillingPaymentStatus.UserNotFound => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401"),
            RegularizeBillingPaymentStatus.BreedingFarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Select a breeding farm before paying this charge.",
                type: "https://httpstatuses.com/409"),
            RegularizeBillingPaymentStatus.SubscriptionNotRecoverable or
                RegularizeBillingPaymentStatus.PaymentNotCurrent or
                RegularizeBillingPaymentStatus.PaymentAlreadyConfirmed or
                RegularizeBillingPaymentStatus.IdempotencyConflict or
                RegularizeBillingPaymentStatus.GatewayPaymentMismatch or
                RegularizeBillingPaymentStatus.AnotherAttemptInProgress => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The charge cannot be attempted in its current state.",
                type: "https://httpstatuses.com/409"),
            RegularizeBillingPaymentStatus.GatewayDeclined => Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "The card payment could not be completed. Check the card details and try again.",
                type: "https://httpstatuses.com/422"),
            RegularizeBillingPaymentStatus.GatewayUnavailable => Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The payment provider could not confirm the charge attempt.",
                type: "https://httpstatuses.com/502"),
            _ => throw new InvalidOperationException("The billing payment result is not supported.")
        };
    }

    private static RegularizeBillingPaymentResponse ToResponse(RegularizeBillingPaymentResult result) =>
        new(result.PaymentId!.Value, "awaitingConfirmation", result.PaymentStatus!.Value.ToString());
}

public sealed record RegularizeBillingPaymentRequest
{
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string? CardToken { get; init; }
}

public sealed record RegularizeBillingPaymentResponse(
    Guid PaymentId,
    string AttemptStatus,
    string PaymentStatus);
