using System.Text.Json;
using CriatorioVirtual.Application.Messaging;

namespace CriatorioVirtual.Application.Billing;

public sealed record ReceiveAsaasWebhookCommand(JsonElement Payload)
    : ICommand<ReceiveAsaasWebhookResult>;

public sealed record ReceiveAsaasWebhookResult(ReceiveAsaasWebhookStatus Status, Guid? EventRecordId = null);

public enum ReceiveAsaasWebhookStatus
{
    InvalidPayload,
    Queued,
    AlreadyProcessed
}

public sealed record ProcessAsaasWebhookEventCommand(Guid EventRecordId, bool IgnoreRetryDelay)
    : ICommand<ProcessAsaasWebhookEventResult>;

public sealed record ProcessAsaasWebhookEventResult(ProcessAsaasWebhookEventStatus Status);

public enum ProcessAsaasWebhookEventStatus
{
    Processed,
    AlreadyProcessed,
    NotDue,
    NotFound
}
