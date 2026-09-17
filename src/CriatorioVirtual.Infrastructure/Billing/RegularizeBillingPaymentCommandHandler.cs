using System.Security.Cryptography;
using System.Text;
using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Billing;

public sealed class RegularizeBillingPaymentCommandHandler(
    CriatorioVirtualDbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<RegularizeBillingPaymentCommand, RegularizeBillingPaymentResult>
{
    public async Task<RegularizeBillingPaymentResult> Handle(
        RegularizeBillingPaymentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var access = await ResolveOwnerFarmAsync(command.UserId, cancellationToken);
        if (access.FailureStatus is { } failureStatus)
        {
            return RegularizeBillingPaymentResult.Failed(failureStatus);
        }

        if (access.BreedingFarmId is not { } breedingFarmId)
        {
            return RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.BreedingFarmNotSelected);
        }

        var paymentKey = await dbContext.Payments.AsNoTracking()
            .Where(candidate => candidate.Id == command.PaymentId && candidate.BreedingFarmId == breedingFarmId)
            .Select(candidate => new { candidate.Id, candidate.SubscriptionId })
            .SingleOrDefaultAsync(cancellationToken);
        if (paymentKey is null)
        {
            return RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.PaymentNotFound);
        }

        // Match webhook lock order so a payment event and a user attempt cannot
        // make decisions from different versions of the subscription/payment pair.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM app.subscriptions WHERE \"Id\" = {paymentKey.SubscriptionId} AND \"BreedingFarmId\" = {breedingFarmId} FOR UPDATE",
            cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM app.payments WHERE \"Id\" = {paymentKey.Id} AND \"BreedingFarmId\" = {breedingFarmId} FOR UPDATE",
            cancellationToken);

        var payment = await dbContext.Payments.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == paymentKey.Id && candidate.BreedingFarmId == breedingFarmId,
            cancellationToken);
        var subscription = await dbContext.Subscriptions.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == paymentKey.SubscriptionId && candidate.BreedingFarmId == breedingFarmId,
            cancellationToken);
        if (payment is null || subscription is null)
        {
            return RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.PaymentNotFound);
        }

        var fingerprint = Fingerprint(command.CardToken);
        var sameKeyAttempt = await dbContext.PaymentAttempts.SingleOrDefaultAsync(
            candidate => candidate.PaymentId == payment.Id && candidate.IdempotencyKey == command.IdempotencyKey,
            cancellationToken);
        if (sameKeyAttempt is not null)
        {
            if (!string.Equals(sameKeyAttempt.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.IdempotencyConflict);
            }

            return sameKeyAttempt.Status switch
            {
                PaymentAttemptStatus.Processing or PaymentAttemptStatus.OutcomeUnknown =>
                    RegularizeBillingPaymentResult.Awaiting(payment.Id, payment.Status, sameKeyAttempt.Id),
                PaymentAttemptStatus.AwaitingConfirmation =>
                    RegularizeBillingPaymentResult.Awaiting(payment.Id, payment.Status),
                PaymentAttemptStatus.Failed =>
                    RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.GatewayDeclined),
                _ => throw new InvalidOperationException("The billing payment attempt status is not supported.")
            };
        }

        if (payment.Status == PaymentStatus.Confirmed)
        {
            return RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.PaymentAlreadyConfirmed);
        }

        if (subscription.Status is not (SubscriptionStatus.GracePeriod or SubscriptionStatus.Blocked))
        {
            return RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.SubscriptionNotRecoverable);
        }

        if (subscription.NextChargeDueAtUtc is not { } currentDueAtUtc ||
            DateOnly.FromDateTime(currentDueAtUtc.UtcDateTime) != DateOnly.FromDateTime(payment.DueAtUtc.UtcDateTime) ||
            payment.Status is not (PaymentStatus.Pending or PaymentStatus.Failed))
        {
            return RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.PaymentNotCurrent);
        }

        if (string.IsNullOrWhiteSpace(subscription.GatewayCustomerId) ||
            string.IsNullOrWhiteSpace(subscription.GatewaySubscriptionId))
        {
            return RegularizeBillingPaymentResult.Failed(RegularizeBillingPaymentStatus.GatewayPaymentMismatch);
        }

        var priorAttempts = await dbContext.PaymentAttempts
            .Where(candidate => candidate.PaymentId == payment.Id)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);
        var unresolvedAttempt = priorAttempts.FirstOrDefault(candidate =>
            candidate.Status is PaymentAttemptStatus.Processing or
                PaymentAttemptStatus.AwaitingConfirmation or
                PaymentAttemptStatus.OutcomeUnknown);
        if (unresolvedAttempt is not null)
        {
            return unresolvedAttempt.Status is PaymentAttemptStatus.Processing or PaymentAttemptStatus.OutcomeUnknown
                ? RegularizeBillingPaymentResult.Awaiting(payment.Id, payment.Status, unresolvedAttempt.Id)
                : RegularizeBillingPaymentResult.Awaiting(payment.Id, payment.Status);
        }

        var now = timeProvider.GetUtcNow().ToUniversalTime();
        var attempt = new PaymentAttempt(
            Guid.NewGuid(),
            payment.Id,
            command.IdempotencyKey,
            fingerprint,
            now);
        dbContext.PaymentAttempts.Add(attempt);
        return RegularizeBillingPaymentResult.Awaiting(payment.Id, payment.Status, attempt.Id, startPaymentAttempt: true);
    }

    private async Task<OwnerFarmAccess> ResolveOwnerFarmAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null)
        {
            return new OwnerFarmAccess(null, RegularizeBillingPaymentStatus.UserNotFound);
        }

        if (user.SelectedBreedingFarmId is not { } breedingFarmId)
        {
            return new OwnerFarmAccess(null, RegularizeBillingPaymentStatus.BreedingFarmNotSelected);
        }

        var isOwner = await dbContext.BreedingFarmUsers.AsNoTracking().AnyAsync(
            membership => membership.BreedingFarmId == breedingFarmId &&
                          membership.UserId == userId &&
                          membership.IsActive &&
                          membership.Role == BreedingFarmRole.Owner,
            cancellationToken);
        return isOwner
            ? new OwnerFarmAccess(breedingFarmId, null)
            : new OwnerFarmAccess(null, RegularizeBillingPaymentStatus.NotFarmOwner);
    }

    private static string Fingerprint(string cardToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cardToken)));

    private sealed record OwnerFarmAccess(Guid? BreedingFarmId, RegularizeBillingPaymentStatus? FailureStatus);
}
