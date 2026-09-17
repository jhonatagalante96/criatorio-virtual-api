using System.Globalization;
using System.Text.Json;
using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Billing;

public sealed class ReceiveAsaasWebhookCommandHandler(
    CriatorioVirtualDbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<ReceiveAsaasWebhookCommand, ReceiveAsaasWebhookResult>,
      ICommandHandler<ProcessAsaasWebhookEventCommand, ProcessAsaasWebhookEventResult>
{
    private static readonly HashSet<string> PaymentEventTypes = new(StringComparer.Ordinal)
    {
        "PAYMENT_CREATED",
        "PAYMENT_CONFIRMED",
        "PAYMENT_RECEIVED",
        "PAYMENT_OVERDUE",
        "PAYMENT_CREDIT_CARD_CAPTURE_REFUSED"
    };

    private static readonly HashSet<string> SubscriptionEventTypes = new(StringComparer.Ordinal)
    {
        "SUBSCRIPTION_CREATED",
        "SUBSCRIPTION_INACTIVATED",
        "SUBSCRIPTION_DELETED"
    };

    private static readonly HashSet<string> CheckoutEventTypes = new(StringComparer.Ordinal)
    {
        "CHECKOUT_CREATED",
        "CHECKOUT_PAID",
        "CHECKOUT_CANCELED",
        "CHECKOUT_EXPIRED"
    };

    public async Task<ReceiveAsaasWebhookResult> Handle(
        ReceiveAsaasWebhookCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!TryReadEnvelope(command.Payload, out var providerEventId, out var eventType))
        {
            return new ReceiveAsaasWebhookResult(ReceiveAsaasWebhookStatus.InvalidPayload);
        }

        if (!IsEventPayloadValid(command.Payload, eventType))
        {
            return new ReceiveAsaasWebhookResult(ReceiveAsaasWebhookStatus.InvalidPayload);
        }

        var payload = command.Payload.GetRawText();
        var receivedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO app.asaas_webhook_events
                ("Id", "ProviderEventId", "EventType", "Payload", "ReceivedAtUtc", "ProcessingAttempts", "NextAttemptAtUtc")
            VALUES
                ({Guid.NewGuid()}, {providerEventId}, {eventType}, CAST({payload} AS jsonb), {receivedAtUtc}, 0, {receivedAtUtc})
            ON CONFLICT ("ProviderEventId") DO NOTHING
            """,
            cancellationToken);

        var eventRecord = await dbContext.AsaasWebhookEvents
            .SingleAsync(candidate => candidate.ProviderEventId == providerEventId, cancellationToken);
        return eventRecord.ProcessedAtUtc is not null
            ? new ReceiveAsaasWebhookResult(ReceiveAsaasWebhookStatus.AlreadyProcessed, eventRecord.Id)
            : new ReceiveAsaasWebhookResult(ReceiveAsaasWebhookStatus.Queued, eventRecord.Id);
    }

    public async Task<ProcessAsaasWebhookEventResult> Handle(
        ProcessAsaasWebhookEventCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var eventRecord = await dbContext.AsaasWebhookEvents
            .FromSqlInterpolated($"SELECT * FROM app.asaas_webhook_events WHERE \"Id\" = {command.EventRecordId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (eventRecord is null)
        {
            return new ProcessAsaasWebhookEventResult(ProcessAsaasWebhookEventStatus.NotFound);
        }

        if (eventRecord.ProcessedAtUtc is not null)
        {
            return new ProcessAsaasWebhookEventResult(ProcessAsaasWebhookEventStatus.AlreadyProcessed);
        }

        var now = timeProvider.GetUtcNow().ToUniversalTime();
        if (!command.IgnoreRetryDelay && eventRecord.NextAttemptAtUtc > now)
        {
            return new ProcessAsaasWebhookEventResult(ProcessAsaasWebhookEventStatus.NotDue);
        }

        using var payload = JsonDocument.Parse(eventRecord.Payload);
        await ApplyEventAsync(payload.RootElement, eventRecord.EventType, cancellationToken);
        eventRecord.MarkProcessed(now);
        return new ProcessAsaasWebhookEventResult(ProcessAsaasWebhookEventStatus.Processed);
    }

    private static bool IsEventPayloadValid(JsonElement payload, string eventType)
    {
        if (PaymentEventTypes.Contains(eventType))
        {
            return TryReadPaymentEvent(payload, out _);
        }

        if (CheckoutEventTypes.Contains(eventType))
        {
            return TryReadCheckoutEvent(payload);
        }

        return !SubscriptionEventTypes.Contains(eventType) || TryReadSubscriptionEvent(payload, out _);
    }

    private async Task ApplyEventAsync(
        JsonElement payload,
        string eventType,
        CancellationToken cancellationToken)
    {
        if (PaymentEventTypes.Contains(eventType))
        {
            _ = TryReadPaymentEvent(payload, out var paymentEvent);
            await ApplyPaymentEventAsync(eventType, paymentEvent, cancellationToken);
            return;
        }

        if (SubscriptionEventTypes.Contains(eventType))
        {
            _ = TryReadSubscriptionEvent(payload, out var subscriptionEvent);
            await ApplySubscriptionEventAsync(eventType, subscriptionEvent, cancellationToken);
            return;
        }

        if (CheckoutEventTypes.Contains(eventType))
        {
            // This V0 billing flow creates subscriptions directly and has no local
            // Checkout reference to map safely to a tenant. Persist Checkout events,
            // but never grant access from a Checkout callback or an uncorrelated ID.
            return;
        }

        // Unknown Asaas events are durably acknowledged and can be handled when the
        // product adds support for them. They must not prevent delivery of known events.
    }

    private async Task ApplySubscriptionEventAsync(
        string eventType,
        AsaasSubscriptionEvent subscriptionEvent,
        CancellationToken cancellationToken)
    {
        if (eventType == "SUBSCRIPTION_CREATED")
        {
            if (subscriptionEvent.BillingType != "CREDIT_CARD" ||
                subscriptionEvent.Status != "ACTIVE" ||
                !Guid.TryParse(subscriptionEvent.ExternalReference, out var localSubscriptionId))
            {
                return;
            }

            var key = await dbContext.Subscriptions
                .AsNoTracking()
                .Where(subscription => subscription.Id == localSubscriptionId)
                .Select(subscription => new { subscription.Id, subscription.BreedingFarmId })
                .SingleOrDefaultAsync(cancellationToken);
            if (key is null)
            {
                return;
            }

            var subscription = await LockSubscriptionAsync(key.Id, key.BreedingFarmId, cancellationToken);
            if (subscription is null ||
                subscription.Status != SubscriptionStatus.PendingSubscription ||
                subscriptionEvent.OccurredAtUtc < subscription.CreatedAtUtc)
            {
                return;
            }

            subscription.ConfirmRecurringSubscription(
                subscriptionEvent.CustomerId,
                subscriptionEvent.GatewaySubscriptionId,
                subscriptionEvent.OccurredAtUtc);
            return;
        }

        var inactivatedSubscription = await LockSubscriptionByGatewayAsync(
            subscriptionEvent.GatewaySubscriptionId,
            subscriptionEvent.CustomerId,
            subscriptionEvent.ExternalReference,
            cancellationToken);
        if (inactivatedSubscription is null ||
            subscriptionEvent.OccurredAtUtc < inactivatedSubscription.UpdatedAtUtc ||
            inactivatedSubscription.Status is not (SubscriptionStatus.Trial or SubscriptionStatus.Active or SubscriptionStatus.GracePeriod))
        {
            return;
        }

        inactivatedSubscription.Cancel(subscriptionEvent.OccurredAtUtc);
    }

    private async Task ApplyPaymentEventAsync(
        string eventType,
        AsaasPaymentEvent paymentEvent,
        CancellationToken cancellationToken)
    {
        var subscription = await LockSubscriptionByGatewayAsync(
            paymentEvent.GatewaySubscriptionId,
            paymentEvent.CustomerId,
            externalReference: null,
            cancellationToken);
        if (subscription is null ||
            subscription.AgreedAmount is not { } agreedAmount ||
            agreedAmount != paymentEvent.Amount)
        {
            return;
        }

        var payment = await dbContext.Payments
            .SingleOrDefaultAsync(
                candidate => candidate.GatewayPaymentId == paymentEvent.GatewayPaymentId,
                cancellationToken);
        if (payment is not null &&
            (payment.BreedingFarmId != subscription.BreedingFarmId ||
             payment.SubscriptionId != subscription.Id ||
             payment.Amount != paymentEvent.Amount ||
             payment.DueAtUtc != paymentEvent.DueAtUtc))
        {
            return;
        }

        if (payment is null)
        {
            payment = new Payment(
                Guid.NewGuid(),
                subscription.BreedingFarmId,
                subscription.Id,
                paymentEvent.GatewayPaymentId,
                paymentEvent.Amount,
                "BRL",
                paymentEvent.DueAtUtc,
                paymentEvent.OccurredAtUtc);
            dbContext.Payments.Add(payment);
        }

        if (paymentEvent.OccurredAtUtc < payment.UpdatedAtUtc)
        {
            return;
        }

        if (eventType is "PAYMENT_CONFIRMED" or "PAYMENT_RECEIVED")
        {
            if (payment.Status != PaymentStatus.Confirmed)
            {
                payment.Confirm(paymentEvent.OccurredAtUtc);
            }

            if ((subscription.Status is SubscriptionStatus.Trial or SubscriptionStatus.GracePeriod) &&
                subscription.TrialEndsAtUtc is { } trialEndsAtUtc &&
                paymentEvent.OccurredAtUtc >= trialEndsAtUtc)
            {
                subscription.ConfirmFirstPayment(paymentEvent.OccurredAtUtc);
            }

            return;
        }

        if (eventType is "PAYMENT_OVERDUE" or "PAYMENT_CREDIT_CARD_CAPTURE_REFUSED")
        {
            if (payment.Status == PaymentStatus.Pending)
            {
                payment.Fail(paymentEvent.OccurredAtUtc);
            }

            if (subscription.Status == SubscriptionStatus.Trial &&
                subscription.TrialEndsAtUtc is { } trialEndsAtUtc &&
                paymentEvent.OccurredAtUtc >= trialEndsAtUtc)
            {
                subscription.FailFirstPayment(paymentEvent.OccurredAtUtc);
            }
        }
    }

    private async Task<Subscription?> LockSubscriptionByGatewayAsync(
        string gatewaySubscriptionId,
        string customerId,
        string? externalReference,
        CancellationToken cancellationToken)
    {
        var key = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(subscription => subscription.GatewaySubscriptionId == gatewaySubscriptionId &&
                                  subscription.GatewayCustomerId == customerId)
            .Select(subscription => new { subscription.Id, subscription.BreedingFarmId })
            .SingleOrDefaultAsync(cancellationToken);
        if (key is null ||
            (externalReference is not null &&
             (!Guid.TryParse(externalReference, out var externalSubscriptionId) || externalSubscriptionId != key.Id)))
        {
            return null;
        }

        var subscription = await LockSubscriptionAsync(key.Id, key.BreedingFarmId, cancellationToken);
        return subscription is not null &&
               subscription.GatewaySubscriptionId == gatewaySubscriptionId &&
               subscription.GatewayCustomerId == customerId
            ? subscription
            : null;
    }

    private async Task<Subscription?> LockSubscriptionAsync(
        Guid subscriptionId,
        Guid breedingFarmId,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM app.subscriptions WHERE \"Id\" = {subscriptionId} AND \"BreedingFarmId\" = {breedingFarmId} FOR UPDATE",
            cancellationToken);
        return await dbContext.Subscriptions.SingleOrDefaultAsync(
            subscription => subscription.Id == subscriptionId && subscription.BreedingFarmId == breedingFarmId,
            cancellationToken);
    }

    private static bool TryReadEnvelope(JsonElement payload, out string eventId, out string eventType)
    {
        eventId = string.Empty;
        eventType = string.Empty;

        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("id", out var idElement) ||
            idElement.ValueKind != JsonValueKind.String ||
            !payload.TryGetProperty("event", out var eventElement) ||
            eventElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        eventId = idElement.GetString() ?? string.Empty;
        eventType = eventElement.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(eventId) &&
               eventId.Length <= AsaasWebhookEventRecord.ProviderEventIdMaxLength &&
               string.Equals(eventId, eventId.Trim(), StringComparison.Ordinal) &&
               !string.IsNullOrWhiteSpace(eventType) &&
               eventType.Length <= AsaasWebhookEventRecord.EventTypeMaxLength &&
               string.Equals(eventType, eventType.Trim(), StringComparison.Ordinal);
    }

    private static bool TryReadPaymentEvent(JsonElement payload, out AsaasPaymentEvent paymentEvent)
    {
        paymentEvent = default;
        if (!TryReadOccurredAt(payload, out var occurredAtUtc) ||
            !payload.TryGetProperty("payment", out var payment) ||
            payment.ValueKind != JsonValueKind.Object ||
            !TryReadString(payment, "id", Subscription.GatewayIdMaxLength, out var paymentId) ||
            !TryReadString(payment, "customer", Subscription.GatewayIdMaxLength, out var customerId) ||
            !TryReadString(payment, "subscription", Subscription.GatewayIdMaxLength, out var subscriptionId) ||
            !payment.TryGetProperty("value", out var amountElement) ||
            !amountElement.TryGetDecimal(out var amount) ||
            amount <= 0 ||
            !TryReadString(payment, "dueDate", 10, out var dueDateText) ||
            !DateOnly.TryParseExact(dueDateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dueDate))
        {
            return false;
        }

        paymentEvent = new AsaasPaymentEvent(
            paymentId,
            customerId,
            subscriptionId,
            amount,
            new DateTimeOffset(dueDate.Year, dueDate.Month, dueDate.Day, 0, 0, 0, TimeSpan.Zero),
            occurredAtUtc);
        return true;
    }

    private static bool TryReadSubscriptionEvent(JsonElement payload, out AsaasSubscriptionEvent subscriptionEvent)
    {
        subscriptionEvent = default;
        if (!TryReadOccurredAt(payload, out var occurredAtUtc) ||
            !payload.TryGetProperty("subscription", out var subscription) ||
            subscription.ValueKind != JsonValueKind.Object ||
            !TryReadString(subscription, "id", Subscription.GatewayIdMaxLength, out var subscriptionId) ||
            !TryReadString(subscription, "customer", Subscription.GatewayIdMaxLength, out var customerId) ||
            !TryReadString(subscription, "status", 32, out var status))
        {
            return false;
        }

        var billingType = TryReadString(subscription, "billingType", 32, out var parsedBillingType)
            ? parsedBillingType
            : string.Empty;
        var externalReference = TryReadString(subscription, "externalReference", 64, out var parsedExternalReference)
            ? parsedExternalReference
            : null;

        subscriptionEvent = new AsaasSubscriptionEvent(
            subscriptionId,
            customerId,
            status,
            billingType,
            externalReference,
            occurredAtUtc);
        return true;
    }

    private static bool TryReadCheckoutEvent(JsonElement payload) =>
        TryReadOccurredAt(payload, out _) &&
        payload.TryGetProperty("checkout", out var checkout) &&
        checkout.ValueKind == JsonValueKind.Object &&
        TryReadString(checkout, "id", 128, out _) &&
        TryReadString(checkout, "status", 32, out _);

    private static bool TryReadOccurredAt(JsonElement payload, out DateTimeOffset occurredAtUtc)
    {
        occurredAtUtc = default;
        return TryReadString(payload, "dateCreated", 64, out var occurredAtText) &&
               DateTimeOffset.TryParse(
                   occurredAtText,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                   out occurredAtUtc);
    }

    private static bool TryReadString(JsonElement value, string propertyName, int maxLength, out string result)
    {
        result = string.Empty;
        if (!value.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        result = element.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(result) &&
               result.Length <= maxLength &&
               string.Equals(result, result.Trim(), StringComparison.Ordinal);
    }

    private readonly record struct AsaasPaymentEvent(
        string GatewayPaymentId,
        string CustomerId,
        string GatewaySubscriptionId,
        decimal Amount,
        DateTimeOffset DueAtUtc,
        DateTimeOffset OccurredAtUtc);

    private readonly record struct AsaasSubscriptionEvent(
        string GatewaySubscriptionId,
        string CustomerId,
        string Status,
        string BillingType,
        string? ExternalReference,
        DateTimeOffset OccurredAtUtc);
}
