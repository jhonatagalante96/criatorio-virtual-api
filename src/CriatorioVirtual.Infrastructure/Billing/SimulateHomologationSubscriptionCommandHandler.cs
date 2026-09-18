using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Billing;

public sealed class SimulateHomologationSubscriptionCommandHandler(
    CriatorioVirtualDbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<SimulateHomologationSubscriptionCommand, SimulateHomologationSubscriptionResult>
{
    public async Task<SimulateHomologationSubscriptionResult> Handle(
        SimulateHomologationSubscriptionCommand command,
        CancellationToken cancellationToken)
    {
        var farmId = command.BreedingFarmId;
        if (farmId is null || farmId == Guid.Empty)
        {
            var selected = await dbContext.Users
                .Where(user => user.Id == command.UserId)
                .Select(user => user.SelectedBreedingFarmId)
                .SingleOrDefaultAsync(cancellationToken);

            if (selected is null)
            {
                return SimulateHomologationSubscriptionResult.Failure(SimulateHomologationSubscriptionStatus.FarmNotSelected);
            }

            farmId = selected.Value;
        }

        var farmExists = await dbContext.BreedingFarms
            .AnyAsync(farm => farm.Id == farmId.Value, cancellationToken);
        if (!farmExists)
        {
            return SimulateHomologationSubscriptionResult.Failure(SimulateHomologationSubscriptionStatus.FarmNotFound);
        }

        var isOwner = await dbContext.BreedingFarmUsers.AnyAsync(
            membership => membership.BreedingFarmId == farmId.Value &&
                          membership.UserId == command.UserId &&
                          membership.Role == BreedingFarmRole.Owner &&
                          membership.IsActive,
            cancellationToken);
        if (!isOwner)
        {
            return SimulateHomologationSubscriptionResult.Failure(SimulateHomologationSubscriptionStatus.NotFarmOwner);
        }

        var existingPayments = await dbContext.Payments
            .Where(p => p.BreedingFarmId == farmId.Value)
            .ToListAsync(cancellationToken);

        if (existingPayments.Count > 0)
        {
            var paymentIds = existingPayments.Select(p => p.Id).ToList();
            var attempts = await dbContext.PaymentAttempts
                .Where(a => paymentIds.Contains(a.PaymentId))
                .ToListAsync(cancellationToken);

            dbContext.PaymentAttempts.RemoveRange(attempts);
            dbContext.Payments.RemoveRange(existingPayments);
        }

        var existingSubscriptions = await dbContext.Subscriptions
            .Where(s => s.BreedingFarmId == farmId.Value)
            .ToListAsync(cancellationToken);

        if (existingSubscriptions.Count > 0)
        {
            dbContext.Subscriptions.RemoveRange(existingSubscriptions);
        }

        if (command.State == SimulatedSubscriptionState.None)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new SimulateHomologationSubscriptionResult(
                SimulateHomologationSubscriptionStatus.Success,
                farmId.Value,
                null,
                SimulatedSubscriptionState.None,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        var now = timeProvider.GetUtcNow();
        var planCode = command.PlanCode ?? StandardSubscriptionPlanOptions.PlanCode;
        var billingCycle = command.BillingCycle ?? BillingCycle.Monthly;
        var agreedAmount = 19.90m;
        var subscriptionId = Guid.NewGuid();
        var customerId = $"sim_cus_{farmId.Value:N}";
        var gatewaySubId = $"sim_sub_{Guid.NewGuid():N}";

        Subscription subscription;

        switch (command.State)
        {
            case SimulatedSubscriptionState.PendingSubscription:
                subscription = new Subscription(
                    subscriptionId,
                    farmId.Value,
                    planCode,
                    billingCycle,
                    now,
                    agreedAmount);
                break;

            case SimulatedSubscriptionState.Trial:
                subscription = new Subscription(
                    subscriptionId,
                    farmId.Value,
                    planCode,
                    billingCycle,
                    now,
                    agreedAmount);
                subscription.ConfirmRecurringSubscription(customerId, gatewaySubId, now);
                break;

            case SimulatedSubscriptionState.Active:
                var activeCreatedAt = now.AddDays(-10);
                subscription = new Subscription(
                    subscriptionId,
                    farmId.Value,
                    planCode,
                    billingCycle,
                    activeCreatedAt,
                    agreedAmount);
                subscription.ConfirmRecurringSubscription(customerId, gatewaySubId, activeCreatedAt);
                var paymentDueAt = subscription.NextChargeDueAtUtc!.Value;
                subscription.ConfirmPayment(paymentDueAt);
                break;

            case SimulatedSubscriptionState.GracePeriod:
                var graceCreatedAt = now.AddDays(-8);
                subscription = new Subscription(
                    subscriptionId,
                    farmId.Value,
                    planCode,
                    billingCycle,
                    graceCreatedAt,
                    agreedAmount);
                subscription.ConfirmRecurringSubscription(customerId, gatewaySubId, graceCreatedAt);
                var graceDueAt = subscription.NextChargeDueAtUtc!.Value;
                subscription.StartGracePeriod(graceDueAt);
                break;

            case SimulatedSubscriptionState.Blocked:
                var blockedCreatedAt = now.AddDays(-20);
                subscription = new Subscription(
                    subscriptionId,
                    farmId.Value,
                    planCode,
                    billingCycle,
                    blockedCreatedAt,
                    agreedAmount);
                subscription.ConfirmRecurringSubscription(customerId, gatewaySubId, blockedCreatedAt);
                var blockedDueAt = subscription.NextChargeDueAtUtc!.Value;
                subscription.StartGracePeriod(blockedDueAt);
                subscription.TryBlockAfterGracePeriodExpiration(blockedDueAt.AddDays(7));
                break;

            case SimulatedSubscriptionState.Cancelled:
                var cancelledCreatedAt = now.AddDays(-5);
                subscription = new Subscription(
                    subscriptionId,
                    farmId.Value,
                    planCode,
                    billingCycle,
                    cancelledCreatedAt,
                    agreedAmount);
                subscription.ConfirmRecurringSubscription(customerId, gatewaySubId, cancelledCreatedAt);
                subscription.Cancel(now);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(command.State), command.State, "Unsupported simulation state.");
        }

        dbContext.Subscriptions.Add(subscription);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new SimulateHomologationSubscriptionResult(
            SimulateHomologationSubscriptionStatus.Success,
            farmId.Value,
            subscription.Id,
            command.State,
            subscription.Status,
            subscription.TrialStartedAtUtc,
            subscription.TrialEndsAtUtc,
            subscription.NextChargeDueAtUtc,
            subscription.GracePeriodStartedAtUtc,
            subscription.GracePeriodEndsAtUtc);
    }
}
