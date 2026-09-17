using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CriatorioVirtual.Infrastructure.Billing;

public sealed class SubscriptionGracePeriodBlockingWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<SubscriptionGracePeriodBlockingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan FailureRetryInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextUtcMidnight(timeProvider.GetUtcNow());
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<ISubscriptionGracePeriodBlockingService>();
                var processed = await service.ProcessExpiredGracePeriodsAsync(stoppingToken);
                if (processed > 0)
                {
                    logger.LogInformation("Blocked {SubscriptionCount} subscriptions after their grace periods expired.", processed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The subscription grace-period blocking job failed.");
                delay = FailureRetryInterval;
            }

            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static TimeSpan TimeUntilNextUtcMidnight(DateTimeOffset nowUtc)
    {
        var nextMidnight = new DateTimeOffset(nowUtc.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        return nextMidnight - nowUtc;
    }
}
