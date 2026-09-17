using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CriatorioVirtual.Infrastructure.Billing;

public sealed class AsaasWebhookEventRetryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<AsaasWebhookEventRetryWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IAsaasWebhookEventProcessingService>();
                await processor.ProcessDueEventsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The Asaas webhook retry worker could not process its pending batch.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}
