using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CriatorioVirtual.Infrastructure.Billing;

public sealed class CreateBillingSubscriptionPostProcessor(
    IServiceScopeFactory scopeFactory,
    IBillingGateway billingGateway,
    IAsaasOperationCoordinator operationCoordinator,
    TimeProvider timeProvider,
    ILogger<CreateBillingSubscriptionPostProcessor> logger)
    : ICommandPostProcessor<CreateBillingSubscriptionCommand, CreateBillingSubscriptionResult>
{
    public async Task<CreateBillingSubscriptionResult> Process(
        CreateBillingSubscriptionCommand command,
        CreateBillingSubscriptionResult result,
        CancellationToken cancellationToken)
    {
        if (result.Status != CreateBillingSubscriptionStatus.PendingConfirmation ||
            result.BreedingFarmId is not { } farmId ||
            result.SubscriptionId is not { } subscriptionId ||
            result.BillingCycle is not { } billingCycle ||
            result.Amount is not { } amount)
        {
            return result;
        }

        await using var operation = await operationCoordinator.AcquireAsync(
            $"subscription-create:{subscriptionId:D}",
            cancellationToken);
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var pendingSubscription = await dbContext.Subscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == subscriptionId &&
                             candidate.BreedingFarmId == farmId,
                cancellationToken);
        if (pendingSubscription?.HostedCheckoutRequestedAtUtc is not null)
        {
            return result with { Status = CreateBillingSubscriptionStatus.BillingCycleConflict };
        }

        if (pendingSubscription is null ||
            (pendingSubscription.Status != SubscriptionStatus.PendingSubscription &&
             (pendingSubscription.Status != SubscriptionStatus.Trial ||
              pendingSubscription.GatewayCustomerId is null ||
              pendingSubscription.GatewaySubscriptionId is null)))
        {
            return result with { Status = CreateBillingSubscriptionStatus.BillingCycleConflict };
        }

        var farm = await dbContext.BreedingFarms
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == farmId, cancellationToken);
        if (farm is null)
        {
            logger.LogWarning("A billing subscription request references a missing farm {BreedingFarmId}.", farmId);
            return result with { Status = CreateBillingSubscriptionStatus.GatewayUnavailable };
        }

        try
        {
            var customer = await billingGateway.GetOrCreateCustomerAsync(
                new BillingGatewayCustomerRequest(
                    farm.Id,
                    farm.ResponsibleName,
                    command.CustomerTaxIdentifier,
                    farm.ContactEmail,
                    farm.ContactPhone),
                cancellationToken);

            var createdAtUtc = await GetCreatedAtUtc(dbContext, farmId, subscriptionId, cancellationToken);
            var firstChargeDate = DateOnly.FromDateTime(createdAtUtc.UtcDateTime)
                .AddDays(Subscription.TrialDurationDays);
            var gatewaySubscription = await billingGateway.GetOrCreateSubscriptionAsync(
                new BillingGatewaySubscriptionRequest(
                    subscriptionId,
                    customer.Id,
                    billingCycle,
                    amount,
                    firstChargeDate,
                    $"Criatório Virtual - {result.PlanCode} ({billingCycle})",
                    command.CardToken,
                    command.RemoteIp),
                cancellationToken);

            if (!string.Equals(gatewaySubscription.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                return result;
            }

            return await ConfirmTrialAsync(
                dbContext,
                result,
                customer.Id,
                gatewaySubscription,
                cancellationToken);
        }
        catch (BillingGatewayOperationOutcomeUnknownException)
        {
            logger.LogWarning(
                "The billing gateway outcome is unknown for subscription {SubscriptionId}; the pending operation can be retried.",
                subscriptionId);
            return result;
        }
        catch (BillingGatewayException)
        {
            logger.LogWarning(
                "The billing gateway could not complete subscription {SubscriptionId}.",
                subscriptionId);
            return result with { Status = CreateBillingSubscriptionStatus.GatewayUnavailable };
        }
    }

    private async Task<DateTimeOffset> GetCreatedAtUtc(
        CriatorioVirtualDbContext dbContext,
        Guid breedingFarmId,
        Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        var createdAtUtc = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(candidate => candidate.Id == subscriptionId && candidate.BreedingFarmId == breedingFarmId)
            .Select(candidate => candidate.CreatedAtUtc)
            .SingleOrDefaultAsync(cancellationToken);
        if (createdAtUtc == default)
        {
            throw new InvalidOperationException("The pending billing subscription was not found.");
        }

        return createdAtUtc;
    }

    private async Task<CreateBillingSubscriptionResult> ConfirmTrialAsync(
        CriatorioVirtualDbContext dbContext,
        CreateBillingSubscriptionResult result,
        string gatewayCustomerId,
        BillingGatewaySubscription gatewaySubscription,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM app.subscriptions WHERE \"Id\" = {result.SubscriptionId!.Value} AND \"BreedingFarmId\" = {result.BreedingFarmId!.Value} FOR UPDATE",
            cancellationToken);
        var subscription = await dbContext.Subscriptions
            .SingleOrDefaultAsync(
                candidate => candidate.Id == result.SubscriptionId.Value &&
                              candidate.BreedingFarmId == result.BreedingFarmId.Value,
                cancellationToken);
        if (subscription is null)
        {
            throw new InvalidOperationException("The pending billing subscription was not found.");
        }

        if (subscription.Status == SubscriptionStatus.PendingSubscription)
        {
            subscription.ConfirmRecurringSubscription(
                gatewayCustomerId,
                gatewaySubscription.Id,
                timeProvider.GetUtcNow().ToUniversalTime());
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (subscription.Status != SubscriptionStatus.Trial ||
                 subscription.GatewayCustomerId != gatewayCustomerId ||
                 subscription.GatewaySubscriptionId != gatewaySubscription.Id)
        {
            throw new BillingGatewayIdempotencyConflictException(subscription.Id.ToString("D"));
        }

        await transaction.CommitAsync(cancellationToken);
        return CreateBillingSubscriptionResult.ForSubscription(
            CreateBillingSubscriptionStatus.TrialStarted,
            subscription);
    }
}
