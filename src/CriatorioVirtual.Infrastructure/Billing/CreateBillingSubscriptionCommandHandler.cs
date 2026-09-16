using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CriatorioVirtual.Infrastructure.Billing;

public sealed class CreateBillingSubscriptionCommandHandler(
    CriatorioVirtualDbContext dbContext,
    IOptions<StandardSubscriptionPlanOptions> planOptions,
    TimeProvider timeProvider)
    : ICommandHandler<CreateBillingSubscriptionCommand, CreateBillingSubscriptionResult>
{
    public async Task<CreateBillingSubscriptionResult> Handle(
        CreateBillingSubscriptionCommand command,
        CancellationToken cancellationToken)
    {
        var selectedFarmId = await dbContext.Users
            .Where(user => user.Id == command.UserId)
            .Select(user => user.SelectedBreedingFarmId)
            .SingleOrDefaultAsync(cancellationToken);
        if (selectedFarmId is null)
        {
            return CreateBillingSubscriptionResult.ForStatus(CreateBillingSubscriptionStatus.FarmNotSelected);
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM app.breeding_farms WHERE \"Id\" = {selectedFarmId.Value} FOR UPDATE",
            cancellationToken);
        var farm = await dbContext.BreedingFarms
            .SingleOrDefaultAsync(candidate => candidate.Id == selectedFarmId.Value, cancellationToken);
        if (farm is null)
        {
            return CreateBillingSubscriptionResult.ForStatus(CreateBillingSubscriptionStatus.FarmNotFound);
        }

        var isOwner = await dbContext.BreedingFarmUsers.AnyAsync(
            membership => membership.BreedingFarmId == farm.Id &&
                          membership.UserId == command.UserId &&
                          membership.Role == BreedingFarmRole.Owner &&
                          membership.IsActive,
            cancellationToken);
        if (!isOwner)
        {
            return CreateBillingSubscriptionResult.ForStatus(CreateBillingSubscriptionStatus.NotFarmOwner);
        }

        var establishedSubscription = await dbContext.Subscriptions
            .SingleOrDefaultAsync(
                subscription => subscription.BreedingFarmId == farm.Id && subscription.TrialStartedAtUtc != null,
                cancellationToken);
        if (establishedSubscription is not null)
        {
            return establishedSubscription.BillingCycle == command.BillingCycle &&
                   establishedSubscription.PlanCode == StandardSubscriptionPlanOptions.PlanCode
                ? CreateBillingSubscriptionResult.ForSubscription(
                    CreateBillingSubscriptionStatus.SubscriptionAlreadyExists,
                    establishedSubscription)
                : CreateBillingSubscriptionResult.ForStatus(CreateBillingSubscriptionStatus.BillingCycleConflict);
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
                return CreateBillingSubscriptionResult.ForStatus(CreateBillingSubscriptionStatus.BillingCycleConflict);
            }

            return CreateBillingSubscriptionResult.ForSubscription(
                CreateBillingSubscriptionStatus.PendingConfirmation,
                pendingSubscription);
        }

        var amount = planOptions.Value.GetAmount(command.BillingCycle);
        if (amount is null)
        {
            return CreateBillingSubscriptionResult.ForStatus(CreateBillingSubscriptionStatus.PlanNotConfigured);
        }

        var now = timeProvider.GetUtcNow().ToUniversalTime();
        var subscription = new Subscription(
            Guid.NewGuid(),
            farm.Id,
            StandardSubscriptionPlanOptions.PlanCode,
            command.BillingCycle,
            now,
            amount.Value);
        dbContext.Subscriptions.Add(subscription);

        return CreateBillingSubscriptionResult.ForSubscription(
            CreateBillingSubscriptionStatus.PendingConfirmation,
            subscription);
    }
}
