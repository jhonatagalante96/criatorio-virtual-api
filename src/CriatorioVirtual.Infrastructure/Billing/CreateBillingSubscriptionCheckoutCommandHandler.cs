using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CriatorioVirtual.Infrastructure.Billing;

public sealed class CreateBillingSubscriptionCheckoutCommandHandler(
    CriatorioVirtualDbContext dbContext,
    IOptions<StandardSubscriptionPlanOptions> planOptions,
    TimeProvider timeProvider)
    : ICommandHandler<CreateBillingSubscriptionCheckoutCommand, CreateBillingSubscriptionCheckoutResult>
{
    public async Task<CreateBillingSubscriptionCheckoutResult> Handle(
        CreateBillingSubscriptionCheckoutCommand command,
        CancellationToken cancellationToken)
    {
        var selectedFarmId = await dbContext.Users
            .Where(user => user.Id == command.UserId)
            .Select(user => user.SelectedBreedingFarmId)
            .SingleOrDefaultAsync(cancellationToken);
        if (selectedFarmId is null)
        {
            return CreateBillingSubscriptionCheckoutResult.ForStatus(CreateBillingSubscriptionCheckoutStatus.FarmNotSelected);
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM app.breeding_farms WHERE \"Id\" = {selectedFarmId.Value} FOR UPDATE",
            cancellationToken);
        var farm = await dbContext.BreedingFarms
            .SingleOrDefaultAsync(candidate => candidate.Id == selectedFarmId.Value, cancellationToken);
        if (farm is null)
        {
            return CreateBillingSubscriptionCheckoutResult.ForStatus(CreateBillingSubscriptionCheckoutStatus.FarmNotFound);
        }

        var isOwner = await dbContext.BreedingFarmUsers.AnyAsync(
            membership => membership.BreedingFarmId == farm.Id &&
                          membership.UserId == command.UserId &&
                          membership.Role == BreedingFarmRole.Owner &&
                          membership.IsActive,
            cancellationToken);
        if (!isOwner)
        {
            return CreateBillingSubscriptionCheckoutResult.ForStatus(CreateBillingSubscriptionCheckoutStatus.NotFarmOwner);
        }

        var establishedSubscription = await dbContext.Subscriptions
            .SingleOrDefaultAsync(
                subscription => subscription.BreedingFarmId == farm.Id &&
                                subscription.TrialStartedAtUtc != null &&
                                subscription.Status != SubscriptionStatus.Cancelled,
                cancellationToken);
        if (establishedSubscription is not null)
        {
            return establishedSubscription.BillingCycle == command.BillingCycle &&
                   establishedSubscription.PlanCode == StandardSubscriptionPlanOptions.PlanCode
                ? CreateBillingSubscriptionCheckoutResult.ForSubscription(
                    CreateBillingSubscriptionCheckoutStatus.SubscriptionAlreadyExists,
                    establishedSubscription)
                : CreateBillingSubscriptionCheckoutResult.ForStatus(CreateBillingSubscriptionCheckoutStatus.BillingCycleConflict);
        }

        var pendingSubscription = await dbContext.Subscriptions
            .SingleOrDefaultAsync(
                subscription => subscription.BreedingFarmId == farm.Id &&
                                subscription.Status == SubscriptionStatus.PendingSubscription,
                cancellationToken);
        if (pendingSubscription is not null)
        {
            if (pendingSubscription.BillingCycle != command.BillingCycle ||
                pendingSubscription.PlanCode != StandardSubscriptionPlanOptions.PlanCode ||
                pendingSubscription.AgreedAmount is null)
            {
                return CreateBillingSubscriptionCheckoutResult.ForStatus(CreateBillingSubscriptionCheckoutStatus.BillingCycleConflict);
            }

            pendingSubscription.RequestHostedCheckout(timeProvider.GetUtcNow().ToUniversalTime());

            return CreateBillingSubscriptionCheckoutResult.ForSubscription(
                pendingSubscription.GatewayCheckoutId is not null &&
                pendingSubscription.GatewayCheckoutUrl is not null &&
                (pendingSubscription.GatewayCheckoutStatus == "PAID" ||
                 (pendingSubscription.GatewayCheckoutStatus is not ("CANCELED" or "EXPIRED") &&
                  (pendingSubscription.GatewayCheckoutExpiresAtUtc is null ||
                   pendingSubscription.GatewayCheckoutExpiresAtUtc > timeProvider.GetUtcNow().ToUniversalTime())))
                    ? CreateBillingSubscriptionCheckoutStatus.CheckoutAlreadyExists
                    : CreateBillingSubscriptionCheckoutStatus.PendingCheckout,
                pendingSubscription);
        }

        var amount = planOptions.Value.GetAmount(command.BillingCycle);
        if (amount is null)
        {
            return CreateBillingSubscriptionCheckoutResult.ForStatus(CreateBillingSubscriptionCheckoutStatus.PlanNotConfigured);
        }

        var requestedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        var subscription = new Subscription(
            Guid.NewGuid(),
            farm.Id,
            StandardSubscriptionPlanOptions.PlanCode,
            command.BillingCycle,
            requestedAtUtc,
            amount.Value);
        subscription.RequestHostedCheckout(requestedAtUtc);
        dbContext.Subscriptions.Add(subscription);

        return CreateBillingSubscriptionCheckoutResult.ForSubscription(
            CreateBillingSubscriptionCheckoutStatus.PendingCheckout,
            subscription);
    }
}
