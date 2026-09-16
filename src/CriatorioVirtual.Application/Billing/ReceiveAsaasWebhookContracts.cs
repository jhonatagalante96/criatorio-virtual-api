using System.Text.Json;
using CriatorioVirtual.Application.Messaging;

namespace CriatorioVirtual.Application.Billing;

public sealed record ReceiveAsaasWebhookCommand(JsonElement Payload)
    : ICommand<ReceiveAsaasWebhookResult>;

public sealed record ReceiveAsaasWebhookResult(ReceiveAsaasWebhookStatus Status);

public enum ReceiveAsaasWebhookStatus
{
    InvalidPayload,
    Received,
    Duplicate
}
