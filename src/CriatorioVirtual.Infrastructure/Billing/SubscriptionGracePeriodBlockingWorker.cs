using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CriatorioVirtual.Infrastructure.Billing;

public sealed class SubscriptionGracePeriodBlockingWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<SubscriptionGracePeriodBlockingWorker> logger) : BackgroundService
{
    private static readonly TimeZoneInfo SaoPauloTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly TimeSpan FailureRetryInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<ISubscriptionGracePeriodBlockingService>();
                var processed = await service.ProcessExpiredGracePeriodsAsync(stoppingToken);
                if (processed > 0)
                {
                    logger.LogInformation("Blocked {SubscriptionCount} subscriptions after their grace periods expired.", processed);
                }

                delay = TimeUntilNextSaoPauloMidnight(timeProvider.GetUtcNow());
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

    internal static TimeSpan TimeUntilNextSaoPauloMidnight(DateTimeOffset nowUtc)
    {
        var utcNow = nowUtc.ToUniversalTime();
        var saoPauloNow = TimeZoneInfo.ConvertTime(utcNow, SaoPauloTimeZone);
        var nextMidnightInSaoPaulo = DateTime.SpecifyKind(
            saoPauloNow.Date.AddDays(1),
            DateTimeKind.Unspecified);
        var nextMidnightUtc = TimeZoneInfo.ConvertTimeToUtc(nextMidnightInSaoPaulo, SaoPauloTimeZone);
        return new DateTimeOffset(nextMidnightUtc, TimeSpan.Zero) - utcNow;
    }
}
