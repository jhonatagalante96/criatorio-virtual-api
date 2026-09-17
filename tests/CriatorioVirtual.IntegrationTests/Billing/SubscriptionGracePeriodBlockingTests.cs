using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Billing;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Billing;

public sealed class SubscriptionGracePeriodBlockingTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProcessExpiredGracePeriods_BlocksOnlyExpiredGracePeriodsAndIsIdempotent()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        var options = CreateOptions(database.GetConnectionString());
        var expiredGraceId = Guid.NewGuid();
        var currentGraceId = Guid.NewGuid();
        var expiredTrialId = Guid.NewGuid();

        await using (var setup = new CriatorioVirtualDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.BreedingFarms.AddRange(
                CreateFarm(Guid.NewGuid(), "Expired grace farm"),
                CreateFarm(Guid.NewGuid(), "Current grace farm"),
                CreateFarm(Guid.NewGuid(), "Expired trial farm"));
            var farmIds = setup.ChangeTracker.Entries<BreedingFarm>()
                .Select(entry => entry.Entity.Id)
                .ToArray();

            var expiredGrace = CreateSubscription(expiredGraceId, farmIds[0], "expired-grace");
            expiredGrace.StartGracePeriod(NowUtc.AddDays(-7));
            var currentGrace = CreateSubscription(currentGraceId, farmIds[1], "current-grace");
            currentGrace.StartGracePeriod(NowUtc.AddDays(-6));
            var expiredTrial = new Subscription(
                expiredTrialId,
                farmIds[2],
                "standard",
                BillingCycle.Monthly,
                NowUtc.AddDays(-30),
                19.90m);
            expiredTrial.ConfirmRecurringSubscription("customer-expired-trial", "subscription-expired-trial", NowUtc.AddDays(-15));

            setup.Subscriptions.AddRange(expiredGrace, currentGrace, expiredTrial);
            await setup.SaveChangesAsync();
        }

        await using (var processingContext = new CriatorioVirtualDbContext(options))
        {
            var service = new SubscriptionGracePeriodBlockingService(processingContext, new FixedTimeProvider(NowUtc));
            Assert.Equal(1, await service.ProcessExpiredGracePeriodsAsync(CancellationToken.None));
        }

        await using (var verification = new CriatorioVirtualDbContext(options))
        {
            Assert.Equal(SubscriptionStatus.Blocked,
                (await verification.Subscriptions.SingleAsync(item => item.Id == expiredGraceId)).Status);
            Assert.Equal(SubscriptionStatus.GracePeriod,
                (await verification.Subscriptions.SingleAsync(item => item.Id == currentGraceId)).Status);
            Assert.Equal(SubscriptionStatus.Trial,
                (await verification.Subscriptions.SingleAsync(item => item.Id == expiredTrialId)).Status);
        }

        await using var rerunContext = new CriatorioVirtualDbContext(options);
        var rerun = new SubscriptionGracePeriodBlockingService(rerunContext, new FixedTimeProvider(NowUtc));
        Assert.Equal(0, await rerun.ProcessExpiredGracePeriodsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ProcessExpiredGracePeriods_SkipsSubscriptionLockedByConcurrentPayment()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        var options = CreateOptions(database.GetConnectionString());
        var farmId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();

        await using (var setup = new CriatorioVirtualDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.BreedingFarms.Add(CreateFarm(farmId, "Concurrent payment farm"));
            var subscription = CreateSubscription(subscriptionId, farmId, "concurrent-payment");
            subscription.StartGracePeriod(NowUtc.AddDays(-7));
            setup.Subscriptions.Add(subscription);
            await setup.SaveChangesAsync();
        }

        await using (var paymentContext = new CriatorioVirtualDbContext(options))
        await using (var paymentTransaction = await paymentContext.Database.BeginTransactionAsync())
        {
            await paymentContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM app.subscriptions WHERE \"Id\" = {subscriptionId} AND \"BreedingFarmId\" = {farmId} FOR UPDATE");
            var subscription = await paymentContext.Subscriptions.SingleAsync(item => item.Id == subscriptionId);

            await using var blockingContext = new CriatorioVirtualDbContext(options);
            var blockingService = new SubscriptionGracePeriodBlockingService(
                blockingContext,
                new FixedTimeProvider(NowUtc));
            Assert.Equal(0, await blockingService.ProcessExpiredGracePeriodsAsync(CancellationToken.None));

            subscription.ConfirmPayment(NowUtc.AddDays(-1));
            await paymentContext.SaveChangesAsync();
            await paymentTransaction.CommitAsync();
        }

        await using var verification = new CriatorioVirtualDbContext(options);
        var persisted = await verification.Subscriptions.SingleAsync(item => item.Id == subscriptionId);
        Assert.Equal(SubscriptionStatus.Active, persisted.Status);
        Assert.Null(persisted.GracePeriodEndsAtUtc);
    }

    private static DbContextOptions<CriatorioVirtualDbContext> CreateOptions(string connectionString) =>
        new DbContextOptionsBuilder<CriatorioVirtualDbContext>()
            .UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    CriatorioVirtualDbContext.DefaultSchema))
            .Options;

    private static Subscription CreateSubscription(Guid id, Guid farmId, string label)
    {
        var subscription = new Subscription(
            id,
            farmId,
            "standard",
            BillingCycle.Monthly,
            NowUtc.AddDays(-30),
            19.90m);
        subscription.ConfirmRecurringSubscription($"customer-{label}", $"subscription-{label}", NowUtc.AddDays(-15));
        return subscription;
    }

    private static BreedingFarm CreateFarm(Guid id, string name) => new(
        id,
        NowUtc.AddDays(-30),
        name,
        "Owner",
        $"{name.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant()}@example.com",
        null,
        null,
        new BreedingFarmAddress(null, null, null, null, null, null, null));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
