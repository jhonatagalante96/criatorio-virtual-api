using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CriatorioVirtual.Infrastructure.Billing;

public sealed class CreateBillingSubscriptionCheckoutPostProcessor(
    IServiceScopeFactory scopeFactory,
    IBillingGateway billingGateway,
    IAsaasOperationCoordinator operationCoordinator,
    IOptions<AsaasOptions> asaasOptions,
    TimeProvider timeProvider,
    ILogger<CreateBillingSubscriptionCheckoutPostProcessor> logger)
    : ICommandPostProcessor<CreateBillingSubscriptionCheckoutCommand, CreateBillingSubscriptionCheckoutResult>
{
    public async Task<CreateBillingSubscriptionCheckoutResult> Process(
        CreateBillingSubscriptionCheckoutCommand command,
        CreateBillingSubscriptionCheckoutResult result,
        CancellationToken cancellationToken)
    {
        if (result.Status != CreateBillingSubscriptionCheckoutStatus.PendingCheckout ||
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

        var subscription = await LoadAndLockSubscriptionAsync(dbContext, farmId, subscriptionId, cancellationToken);
        if (subscription is null || subscription.Status != SubscriptionStatus.PendingSubscription)
        {
            return result with { Status = CreateBillingSubscriptionCheckoutStatus.GatewayUnavailable };
        }

        var now = timeProvider.GetUtcNow().ToUniversalTime();
        if (subscription.GatewayCheckoutId is not null &&
            subscription.GatewayCheckoutUrl is not null &&
            (subscription.GatewayCheckoutStatus == "PAID" ||
             (subscription.GatewayCheckoutStatus is not ("CANCELED" or "EXPIRED") &&
              (subscription.GatewayCheckoutExpiresAtUtc is null || subscription.GatewayCheckoutExpiresAtUtc > now))))
        {
            return ToResult(result, subscription, CreateBillingSubscriptionCheckoutStatus.CheckoutAlreadyExists);
        }

        if (subscription.GatewayCheckoutId is not null && subscription.GatewayCheckoutUrl is null)
        {
            return result with { Status = CreateBillingSubscriptionCheckoutStatus.GatewayUnavailable };
        }

        if (subscription.GatewayCheckoutCreationStartedAtUtc is not null && subscription.GatewayCheckoutId is null)
        {
            logger.LogWarning(
                "Checkout creation outcome is unknown for subscription {SubscriptionId}; retry was stopped to prevent duplicate checkouts.",
                subscriptionId);
            return result with { Status = CreateBillingSubscriptionCheckoutStatus.GatewayUnavailable };
        }

        var farm = await dbContext.BreedingFarms
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == farmId, cancellationToken);
        if (farm is null)
        {
            logger.LogWarning("A subscription checkout request references a missing farm {BreedingFarmId}.", farmId);
            return result with { Status = CreateBillingSubscriptionCheckoutStatus.GatewayUnavailable };
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

            if (await billingGateway.FindSubscriptionAsync(subscriptionId, cancellationToken) is not null)
            {
                logger.LogWarning(
                    "An Asaas subscription already exists for pending hosted checkout {SubscriptionId}; no checkout was created.",
                    subscriptionId);
                return result with { Status = CreateBillingSubscriptionCheckoutStatus.GatewayUnavailable };
            }

            await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
            {
                subscription = await LoadAndLockSubscriptionAsync(dbContext, farmId, subscriptionId, cancellationToken);
                if (subscription is null || subscription.Status != SubscriptionStatus.PendingSubscription)
                {
                    return result with { Status = CreateBillingSubscriptionCheckoutStatus.GatewayUnavailable };
                }

                if (subscription.GatewayCheckoutId is not null &&
                    subscription.GatewayCheckoutUrl is not null &&
                    (subscription.GatewayCheckoutStatus == "PAID" ||
                     (subscription.GatewayCheckoutStatus is not ("CANCELED" or "EXPIRED") &&
                      (subscription.GatewayCheckoutExpiresAtUtc is null || subscription.GatewayCheckoutExpiresAtUtc > now))))
                {
                    await transaction.CommitAsync(cancellationToken);
                    return ToResult(result, subscription, CreateBillingSubscriptionCheckoutStatus.CheckoutAlreadyExists);
                }

                if (subscription.GatewayCheckoutId is not null && subscription.GatewayCheckoutUrl is null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return result with { Status = CreateBillingSubscriptionCheckoutStatus.GatewayUnavailable };
                }

                if (subscription.GatewayCheckoutCreationStartedAtUtc is not null && subscription.GatewayCheckoutId is null)
                {
                    return result with { Status = CreateBillingSubscriptionCheckoutStatus.GatewayUnavailable };
                }

                subscription.BeginHostedCheckoutCreation(customer.Id, now);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            var checkout = await billingGateway.CreateSubscriptionCheckoutAsync(
                new BillingGatewayCheckoutRequest(
                    subscriptionId,
                    customer.Id,
                    billingCycle,
                    amount,
                    DateOnly.FromDateTime(subscription.CreatedAtUtc.UtcDateTime)
                        .AddDays(Subscription.TrialDurationDays),
                    now.AddMinutes(asaasOptions.Value.CheckoutMinutesToExpire),
                    $"Plano {result.BillingCycle}"),
                cancellationToken);

            await using var finalizeTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            subscription = await LoadAndLockSubscriptionAsync(dbContext, farmId, subscriptionId, cancellationToken);
            if (subscription is null)
            {
                throw new InvalidOperationException("The pending billing subscription was not found.");
            }

            subscription.SetHostedCheckout(
                checkout.Id,
                checkout.Url,
                checkout.ExpiresAtUtc,
                now);
            await dbContext.SaveChangesAsync(cancellationToken);
            await finalizeTransaction.CommitAsync(cancellationToken);
            return ToResult(result, subscription, CreateBillingSubscriptionCheckoutStatus.PendingCheckout);
        }
        catch (BillingGatewayOperationOutcomeUnknownException)
        {
            logger.LogWarning(
                "The billing gateway outcome is unknown for checkout {SubscriptionId}; further creates are blocked to avoid duplication.",
                subscriptionId);
            return result with { Status = CreateBillingSubscriptionCheckoutStatus.GatewayUnavailable };
        }
        catch (BillingGatewayIdempotencyConflictException exception)
        {
            logger.LogWarning(
                exception,
                "Asaas returned a conflicting checkout for subscription {SubscriptionId}; further creates are blocked.",
                subscriptionId);
            return result with { Status = CreateBillingSubscriptionCheckoutStatus.GatewayUnavailable };
        }
        catch (BillingGatewayException exception)
        {
            logger.LogWarning(exception, "The billing gateway could not create checkout for subscription {SubscriptionId}.", subscriptionId);
            await ClearFailedAttemptAsync(dbContext, farmId, subscriptionId, cancellationToken);
            return result with { Status = CreateBillingSubscriptionCheckoutStatus.GatewayUnavailable };
        }
    }

    private async Task<Subscription?> LoadAndLockSubscriptionAsync(
        CriatorioVirtualDbContext dbContext,
        Guid farmId,
        Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM app.subscriptions WHERE \"Id\" = {subscriptionId} AND \"BreedingFarmId\" = {farmId} FOR UPDATE",
            cancellationToken);
        return await dbContext.Subscriptions.SingleOrDefaultAsync(
            candidate => candidate.Id == subscriptionId && candidate.BreedingFarmId == farmId,
            cancellationToken);
    }

    private async Task ClearFailedAttemptAsync(
        CriatorioVirtualDbContext dbContext,
        Guid farmId,
        Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var subscription = await LoadAndLockSubscriptionAsync(dbContext, farmId, subscriptionId, cancellationToken);
        if (subscription?.Status == SubscriptionStatus.PendingSubscription)
        {
            subscription.MarkHostedCheckoutCreationFailed(timeProvider.GetUtcNow().ToUniversalTime());
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static CreateBillingSubscriptionCheckoutResult ToResult(
        CreateBillingSubscriptionCheckoutResult result,
        Subscription subscription,
        CreateBillingSubscriptionCheckoutStatus status) =>
        result with
        {
            Status = status,
            SubscriptionId = subscription.Id,
            CheckoutId = subscription.GatewayCheckoutId,
            CheckoutUrl = subscription.GatewayCheckoutUrl,
            ExpiresAtUtc = subscription.GatewayCheckoutExpiresAtUtc
        };
}
