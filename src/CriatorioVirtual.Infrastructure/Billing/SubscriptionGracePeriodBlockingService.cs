using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Billing;

public interface ISubscriptionGracePeriodBlockingService
{
    Task<int> ProcessExpiredGracePeriodsAsync(CancellationToken cancellationToken);
}

public sealed class SubscriptionGracePeriodBlockingService(
    CriatorioVirtualDbContext dbContext,
    TimeProvider timeProvider) : ISubscriptionGracePeriodBlockingService
{
    private const int BatchSize = 100;

    public async Task<int> ProcessExpiredGracePeriodsAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().ToUniversalTime();
        var processed = 0;

        while (true)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var expiredSubscriptions = await dbContext.Subscriptions
                .FromSqlInterpolated($"""
                    SELECT *, xmin
                    FROM app.subscriptions
                    WHERE "Status" = {(int)SubscriptionStatus.GracePeriod}
                      AND "GracePeriodEndsAtUtc" <= {now}
                    ORDER BY "GracePeriodEndsAtUtc", "Id"
                    LIMIT {BatchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken);

            var blockedCount = 0;
            foreach (var subscription in expiredSubscriptions)
            {
                if (subscription.TryBlockAfterGracePeriodExpiration(now))
                {
                    blockedCount++;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            processed += blockedCount;

            if (expiredSubscriptions.Count < BatchSize || blockedCount == 0)
            {
                return processed;
            }
        }
    }
}
