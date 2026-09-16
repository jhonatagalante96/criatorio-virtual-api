using System.Net;
using CriatorioVirtual.Domain.Billing;

namespace CriatorioVirtual.Application.Billing;

public sealed record BillingGatewayCustomerRequest(
    Guid BreedingFarmId,
    string Name,
    string TaxIdentifier,
    string? Email,
    string? MobilePhone)
{
    public override string ToString() =>
        $"{nameof(BillingGatewayCustomerRequest)} {{ BreedingFarmId = {BreedingFarmId}, " +
        "Name = [redacted], TaxIdentifier = [redacted], Email = [redacted], MobilePhone = [redacted] }";
}

public sealed record BillingGatewayCustomer(string Id);

public sealed class BillingGatewaySubscriptionRequest
{
    public BillingGatewaySubscriptionRequest(
        Guid subscriptionId,
        string customerId,
        BillingCycle billingCycle,
        decimal amount,
        DateOnly firstChargeDate,
        string description,
        string cardToken,
        IPAddress remoteIp)
    {
        if (subscriptionId == Guid.Empty)
        {
            throw new ArgumentException("A local subscription identifier is required.", nameof(subscriptionId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        if (!Enum.IsDefined(billingCycle))
        {
            throw new ArgumentOutOfRangeException(nameof(billingCycle), billingCycle, "The billing cycle is not supported.");
        }

        if (amount <= 0 || decimal.Round(amount, 2, MidpointRounding.ToEven) != amount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "The amount must be positive and use at most two decimal places.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (description.Trim().Length > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(description), "The description must not exceed 500 characters.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(cardToken);
        ArgumentNullException.ThrowIfNull(remoteIp);

        SubscriptionId = subscriptionId;
        CustomerId = customerId.Trim();
        BillingCycle = billingCycle;
        Amount = amount;
        FirstChargeDate = firstChargeDate;
        Description = description.Trim();
        CardToken = cardToken.Trim();
        RemoteIp = remoteIp;
    }

    public Guid SubscriptionId { get; }

    public string CustomerId { get; }

    public BillingCycle BillingCycle { get; }

    public decimal Amount { get; }

    public DateOnly FirstChargeDate { get; }

    public string Description { get; }

    public string CardToken { get; }

    public IPAddress RemoteIp { get; }

    public override string ToString() =>
        $"{nameof(BillingGatewaySubscriptionRequest)} {{ SubscriptionId = {SubscriptionId}, CustomerId = {CustomerId}, " +
        $"BillingCycle = {BillingCycle}, Amount = {Amount}, FirstChargeDate = {FirstChargeDate}, " +
        "Description = [redacted], CardToken = [redacted], RemoteIp = [redacted] }";
}

public sealed record BillingGatewaySubscription(
    string Id,
    string CustomerId,
    string ExternalReference,
    string Status,
    BillingCycle? BillingCycle,
    decimal? Amount,
    DateOnly? FirstChargeDate);

public interface IBillingGateway
{
    Task<BillingGatewayCustomer> GetOrCreateCustomerAsync(
        BillingGatewayCustomerRequest request,
        CancellationToken cancellationToken = default);

    Task<BillingGatewaySubscription> GetOrCreateSubscriptionAsync(
        BillingGatewaySubscriptionRequest request,
        CancellationToken cancellationToken = default);

    Task<BillingGatewaySubscription?> FindSubscriptionAsync(
        Guid subscriptionId,
        CancellationToken cancellationToken = default);

    Task<BillingGatewaySubscription?> GetSubscriptionAsync(
        string gatewaySubscriptionId,
        CancellationToken cancellationToken = default);
}

public class BillingGatewayException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class BillingGatewayOperationOutcomeUnknownException(
    string externalReference,
    Exception? innerException = null)
    : BillingGatewayException(
        $"The billing gateway outcome is unknown for external reference '{externalReference}'. Reconcile it before retrying.",
        innerException);

public sealed class BillingGatewayIdempotencyConflictException(string externalReference)
    : BillingGatewayException(
        $"The billing gateway returned conflicting data for external reference '{externalReference}'.");
