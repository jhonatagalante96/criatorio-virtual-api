using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CriatorioVirtual.Infrastructure.Billing;

public sealed class CancelBillingSubscriptionPostProcessor(
    IServiceScopeFactory scopeFactory,
    IBillingGateway billingGateway,
    TimeProvider timeProvider,
    ILogger<CancelBillingSubscriptionPostProcessor> logger)
    : ICommandPostProcessor<CancelBillingSubscriptionCommand, CancelBillingSubscriptionResult>
{
    public async Task<CancelBillingSubscriptionResult> Process(
        CancelBillingSubscriptionCommand command,
        CancelBillingSubscriptionResult result,
        CancellationToken cancellationToken)
    {
        if (result.Status != CancelBillingSubscriptionStatus.CancellationRequested ||
            result.BreedingFarmId is not { } breedingFarmId ||
            result.SubscriptionId is not { } subscriptionId ||
            result.GatewaySubscriptionId is not { } gatewaySubscriptionId)
        {
            return result;
        }

        try
        {
            await billingGateway.CancelSubscriptionAsync(
                subscriptionId,
                gatewaySubscriptionId,
                cancellationToken);
        }
        catch (BillingGatewayException)
        {
            logger.LogWarning(
                "The billing gateway could not cancel subscription {SubscriptionId}.",
                subscriptionId);
            return result with { Status = CancelBillingSubscriptionStatus.GatewayUnavailable };
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM app.subscriptions WHERE \"Id\" = {subscriptionId} AND \"BreedingFarmId\" = {breedingFarmId} FOR UPDATE",
            cancellationToken);
        var subscription = await dbContext.Subscriptions
            .SingleOrDefaultAsync(
                candidate => candidate.Id == subscriptionId && candidate.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (subscription is null)
        {
            logger.LogError(
                "The local subscription {SubscriptionId} disappeared after cancellation at the billing gateway.",
                subscriptionId);
            throw new InvalidOperationException("The subscription could not be found after gateway cancellation.");
        }

        if (subscription.Status != SubscriptionStatus.Cancelled)
        {
            subscription.Cancel(timeProvider.GetUtcNow().ToUniversalTime());
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return CancelBillingSubscriptionResult.Succeeded(breedingFarmId, subscriptionId);
    }
}
