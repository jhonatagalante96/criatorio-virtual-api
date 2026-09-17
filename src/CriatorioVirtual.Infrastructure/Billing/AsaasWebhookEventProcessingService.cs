using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CriatorioVirtual.Infrastructure.Billing;

public interface IAsaasWebhookEventProcessingService
{
    Task<AsaasWebhookEventProcessingOutcome> ProcessAsync(
        Guid eventRecordId,
        bool ignoreRetryDelay,
        CancellationToken cancellationToken);

    Task<int> ProcessDueEventsAsync(CancellationToken cancellationToken);
}

public enum AsaasWebhookEventProcessingOutcome
{
    Processed,
    AlreadyProcessed,
    RetryScheduled,
    NotDue,
    NotFound
}

public sealed class AsaasWebhookEventProcessingService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<AsaasWebhookEventProcessingService> logger) : IAsaasWebhookEventProcessingService
{
    private const int BatchSize = 25;

    public async Task<AsaasWebhookEventProcessingOutcome> ProcessAsync(
        Guid eventRecordId,
        bool ignoreRetryDelay,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var executor = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            var result = await executor.Execute<ProcessAsaasWebhookEventCommand, ProcessAsaasWebhookEventResult>(
                new ProcessAsaasWebhookEventCommand(eventRecordId, ignoreRetryDelay),
                cancellationToken);

            return result.Status switch
            {
                ProcessAsaasWebhookEventStatus.Processed => AsaasWebhookEventProcessingOutcome.Processed,
                ProcessAsaasWebhookEventStatus.AlreadyProcessed => AsaasWebhookEventProcessingOutcome.AlreadyProcessed,
                ProcessAsaasWebhookEventStatus.NotDue => AsaasWebhookEventProcessingOutcome.NotDue,
                ProcessAsaasWebhookEventStatus.NotFound => AsaasWebhookEventProcessingOutcome.NotFound,
                _ => throw new InvalidOperationException("The Asaas webhook processing result is not supported.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await ScheduleRetryAsync(eventRecordId, cancellationToken);
            logger.LogWarning(
                exception,
                "Processing Asaas webhook event {EventRecordId} failed; the event remains eligible for retry.",
                eventRecordId);
            return AsaasWebhookEventProcessingOutcome.RetryScheduled;
        }
    }

    public async Task<int> ProcessDueEventsAsync(CancellationToken cancellationToken)
    {
        Guid[] eventRecordIds;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var now = timeProvider.GetUtcNow().ToUniversalTime();
            eventRecordIds = await dbContext.AsaasWebhookEvents
                .AsNoTracking()
                .Where(candidate => candidate.ProcessedAtUtc == null && candidate.NextAttemptAtUtc <= now)
                .OrderBy(candidate => candidate.ReceivedAtUtc)
                .Select(candidate => candidate.Id)
                .Take(BatchSize)
                .ToArrayAsync(cancellationToken);
        }

        foreach (var eventRecordId in eventRecordIds)
        {
            await ProcessAsync(eventRecordId, ignoreRetryDelay: false, cancellationToken);
        }

        return eventRecordIds.Length;
    }

    private async Task ScheduleRetryAsync(Guid eventRecordId, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var failedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE app.asaas_webhook_events
                SET "ProcessingAttempts" = "ProcessingAttempts" + 1,
                    "NextAttemptAtUtc" = {failedAtUtc} + make_interval(
                        secs => LEAST(5 * power(2, LEAST("ProcessingAttempts", 10)), 3600)::double precision)
                WHERE "Id" = {eventRecordId} AND "ProcessedAtUtc" IS NULL
                """,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception retryException)
        {
            logger.LogError(
                retryException,
                "Could not update the retry schedule for Asaas webhook event {EventRecordId}.",
                eventRecordId);
        }
    }
}
