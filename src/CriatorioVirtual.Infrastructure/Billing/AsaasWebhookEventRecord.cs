namespace CriatorioVirtual.Infrastructure.Billing;

public sealed class AsaasWebhookEventRecord
{
    public const int ProviderEventIdMaxLength = 256;
    public const int EventTypeMaxLength = 128;

    private AsaasWebhookEventRecord()
    {
    }

    public Guid Id { get; private set; }

    public string ProviderEventId { get; private set; } = null!;

    public string EventType { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public DateTimeOffset ReceivedAtUtc { get; private set; }
}
