using System.Text.Json;
using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Billing;

public sealed class ReceiveAsaasWebhookCommandHandler(
    CriatorioVirtualDbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<ReceiveAsaasWebhookCommand, ReceiveAsaasWebhookResult>
{
    public async Task<ReceiveAsaasWebhookResult> Handle(
        ReceiveAsaasWebhookCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!TryReadEnvelope(command.Payload, out var providerEventId, out var eventType))
        {
            return new ReceiveAsaasWebhookResult(ReceiveAsaasWebhookStatus.InvalidPayload);
        }

        var payload = command.Payload.GetRawText();
        var receivedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        var insertedRows = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO app.asaas_webhook_events
                ("Id", "ProviderEventId", "EventType", "Payload", "ReceivedAtUtc")
            VALUES
                ({Guid.NewGuid()}, {providerEventId}, {eventType}, CAST({payload} AS jsonb), {receivedAtUtc})
            ON CONFLICT ("ProviderEventId") DO NOTHING
            """,
            cancellationToken);

        return new ReceiveAsaasWebhookResult(
            insertedRows == 1 ? ReceiveAsaasWebhookStatus.Received : ReceiveAsaasWebhookStatus.Duplicate);
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
}
