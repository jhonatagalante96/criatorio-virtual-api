using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Billing;

public sealed class RegularizeBillingPaymentPostProcessor(
    CriatorioVirtualDbContext dbContext,
    IBillingGateway billingGateway,
    IAsaasOperationCoordinator operationCoordinator,
    TimeProvider timeProvider)
    : ICommandPostProcessor<RegularizeBillingPaymentCommand, RegularizeBillingPaymentResult>
{
    private static readonly TimeSpan ProcessingAttemptLease = TimeSpan.FromMinutes(2);

    public async Task<RegularizeBillingPaymentResult> Process(
        RegularizeBillingPaymentCommand command,
        RegularizeBillingPaymentResult result,
        CancellationToken cancellationToken)
    {
        if (result.PaymentAttemptId is not { } attemptId || result.PaymentId is not { } paymentId)
        {
            return result;
        }

        await using var operation = await operationCoordinator.AcquireAsync(
            $"payment-attempt:{paymentId:D}",
            cancellationToken);

        var attempt = await dbContext.PaymentAttempts.SingleOrDefaultAsync(
            candidate => candidate.Id == attemptId && candidate.PaymentId == paymentId,
            cancellationToken);
        if (attempt is null)
        {
            return RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.GatewayUnavailable);
        }

        await dbContext.Entry(attempt).ReloadAsync(cancellationToken);
        if (attempt.Status == PaymentAttemptStatus.AwaitingConfirmation)
        {
            return RegularizeBillingPaymentResult.Awaiting(paymentId, result.PaymentStatus ?? PaymentStatus.Pending);
        }

        if (attempt.Status == PaymentAttemptStatus.Failed)
        {
            return RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.GatewayDeclined);
        }

        if (!result.StartPaymentAttempt &&
            attempt.Status == PaymentAttemptStatus.Processing &&
            timeProvider.GetUtcNow().ToUniversalTime() - attempt.CreatedAtUtc < ProcessingAttemptLease)
        {
            return RegularizeBillingPaymentResult.Awaiting(paymentId, result.PaymentStatus ?? PaymentStatus.Pending);
        }

        var payment = await dbContext.Payments.SingleOrDefaultAsync(
            candidate => candidate.Id == paymentId,
            cancellationToken);
        var subscription = payment is null
            ? null
            : await dbContext.Subscriptions.SingleOrDefaultAsync(
                candidate => candidate.Id == payment.SubscriptionId &&
                             candidate.BreedingFarmId == payment.BreedingFarmId,
                cancellationToken);
        if (payment is null || subscription is null)
        {
            attempt.MarkFailed(timeProvider.GetUtcNow().ToUniversalTime());
            await dbContext.SaveChangesAsync(cancellationToken);
            return RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.PaymentNotFound);
        }

        if (payment.Status == PaymentStatus.Confirmed)
        {
            attempt.MarkAwaitingConfirmation(timeProvider.GetUtcNow().ToUniversalTime());
            await dbContext.SaveChangesAsync(cancellationToken);
            return RegularizeBillingPaymentResult.Awaiting(payment.Id, payment.Status);
        }

        if (subscription.Status is not (SubscriptionStatus.GracePeriod or SubscriptionStatus.Blocked) ||
            subscription.NextChargeDueAtUtc is not { } dueAtUtc ||
            DateOnly.FromDateTime(dueAtUtc.UtcDateTime) != DateOnly.FromDateTime(payment.DueAtUtc.UtcDateTime) ||
            payment.Status is not (PaymentStatus.Pending or PaymentStatus.Failed))
        {
            attempt.MarkFailed(timeProvider.GetUtcNow().ToUniversalTime());
            await dbContext.SaveChangesAsync(cancellationToken);
            return RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.SubscriptionNotRecoverable);
        }

        if (string.IsNullOrWhiteSpace(subscription.GatewayCustomerId) ||
            string.IsNullOrWhiteSpace(subscription.GatewaySubscriptionId))
        {
            attempt.MarkFailed(timeProvider.GetUtcNow().ToUniversalTime());
            await dbContext.SaveChangesAsync(cancellationToken);
            return RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.GatewayPaymentMismatch);
        }

        var stillSelectedOwner = await dbContext.Users.AsNoTracking().AnyAsync(
            user => user.Id == command.UserId && user.SelectedBreedingFarmId == payment.BreedingFarmId,
            cancellationToken) &&
            await dbContext.BreedingFarmUsers.AsNoTracking().AnyAsync(
                membership => membership.BreedingFarmId == payment.BreedingFarmId &&
                              membership.UserId == command.UserId &&
                              membership.IsActive &&
                              membership.Role == BreedingFarmRole.Owner,
                cancellationToken);
        if (!stillSelectedOwner)
        {
            if (result.StartPaymentAttempt)
            {
                attempt.MarkFailed(timeProvider.GetUtcNow().ToUniversalTime());
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.NotFarmOwner);
        }

        BillingGatewayPayment? gatewayPayment;
        // Asaas does not make this charge operation idempotent. The durable attempt
        // plus this per-payment lock prevents our retries from issuing a second POST.
        try
        {
            gatewayPayment = await billingGateway.GetPaymentAsync(payment.GatewayPaymentId, cancellationToken);
        }
        catch (Exception exception) when (exception is BillingGatewayException or HttpRequestException)
        {
            if (result.StartPaymentAttempt)
            {
                attempt.MarkFailed(timeProvider.GetUtcNow().ToUniversalTime());
            }
            else
            {
                attempt.MarkOutcomeUnknown(timeProvider.GetUtcNow().ToUniversalTime());
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.GatewayUnavailable);
        }

        if (gatewayPayment is null || !MatchesGatewayPayment(gatewayPayment, payment, subscription))
        {
            if (result.StartPaymentAttempt)
            {
                attempt.MarkFailed(timeProvider.GetUtcNow().ToUniversalTime());
            }
            else
            {
                attempt.MarkOutcomeUnknown(timeProvider.GetUtcNow().ToUniversalTime());
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.GatewayPaymentMismatch);
        }

        if (IsPaidStatus(gatewayPayment.Status))
        {
            attempt.MarkAwaitingConfirmation(timeProvider.GetUtcNow().ToUniversalTime());
            await dbContext.SaveChangesAsync(cancellationToken);
            return RegularizeBillingPaymentResult.Awaiting(payment.Id, payment.Status);
        }

        if (!IsUnpaidStatus(gatewayPayment.Status))
        {
            if (result.StartPaymentAttempt)
            {
                attempt.MarkFailed(timeProvider.GetUtcNow().ToUniversalTime());
            }
            else
            {
                attempt.MarkOutcomeUnknown(timeProvider.GetUtcNow().ToUniversalTime());
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.GatewayPaymentMismatch);
        }

        if (!result.StartPaymentAttempt)
        {
            attempt.MarkFailed(timeProvider.GetUtcNow().ToUniversalTime());
            await dbContext.SaveChangesAsync(cancellationToken);
            return RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.GatewayDeclined);
        }

        try
        {
            var paymentAfterAttempt = await billingGateway.PayPaymentWithCreditCardAsync(
                new BillingGatewayPaymentRequest(payment.GatewayPaymentId, command.CardToken),
                cancellationToken);
            if (!MatchesGatewayPayment(paymentAfterAttempt, payment, subscription))
            {
                attempt.MarkOutcomeUnknown(timeProvider.GetUtcNow().ToUniversalTime());
                await dbContext.SaveChangesAsync(cancellationToken);
                return RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.GatewayPaymentMismatch);
            }

            attempt.MarkAwaitingConfirmation(timeProvider.GetUtcNow().ToUniversalTime());
            await dbContext.SaveChangesAsync(cancellationToken);
            return RegularizeBillingPaymentResult.Awaiting(payment.Id, payment.Status);
        }
        catch (BillingGatewayException exception) when (
            exception is BillingGatewayPaymentDeclinedException or BillingGatewayPaymentNotChargedException)
        {
            attempt.MarkFailed(timeProvider.GetUtcNow().ToUniversalTime());
            await dbContext.SaveChangesAsync(cancellationToken);
            return RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.GatewayDeclined);
        }
        catch (BillingGatewayOperationOutcomeUnknownException)
        {
            attempt.MarkOutcomeUnknown(timeProvider.GetUtcNow().ToUniversalTime());
            await dbContext.SaveChangesAsync(cancellationToken);
            return RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.GatewayUnavailable);
        }
        catch (BillingGatewayException)
        {
            attempt.MarkFailed(timeProvider.GetUtcNow().ToUniversalTime());
            await dbContext.SaveChangesAsync(cancellationToken);
            return RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.GatewayUnavailable);
        }
    }

    private static bool MatchesGatewayPayment(
        BillingGatewayPayment gatewayPayment,
        Payment payment,
        Subscription subscription) =>
        string.Equals(gatewayPayment.Id, payment.GatewayPaymentId, StringComparison.Ordinal) &&
        string.Equals(gatewayPayment.CustomerId, subscription.GatewayCustomerId, StringComparison.Ordinal) &&
        string.Equals(gatewayPayment.SubscriptionId, subscription.GatewaySubscriptionId, StringComparison.Ordinal) &&
        gatewayPayment.Amount == payment.Amount &&
        gatewayPayment.DueDate == DateOnly.FromDateTime(payment.DueAtUtc.UtcDateTime);

    private static bool IsUnpaidStatus(string status) => status is "PENDING" or "OVERDUE";

    private static bool IsPaidStatus(string status) => status is "CONFIRMED" or "RECEIVED";
}
