using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Billing;

public sealed class SubscriptionPersistenceTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BillingPersistence_EnforcesSubscriptionUniquenessTenantScopeAndBoundedPaymentQueries()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();

        var options = new DbContextOptionsBuilder<CriatorioVirtualDbContext>()
            .UseNpgsql(
                database.GetConnectionString(),
                npgsqlOptions => npgsqlOptions.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    CriatorioVirtualDbContext.DefaultSchema))
            .Options;

        var firstFarmId = Guid.NewGuid();
        var secondFarmId = Guid.NewGuid();
        var sameSubscriptionId = Guid.NewGuid();
        var competingSubscriptionIds = new[] { Guid.NewGuid(), Guid.NewGuid() };

        await using (var setup = new CriatorioVirtualDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.BreedingFarms.AddRange(CreateFarm(firstFarmId, "First farm"), CreateFarm(secondFarmId, "Second farm"));
            setup.Subscriptions.Add(CreateSubscription(sameSubscriptionId, firstFarmId));
            await setup.SaveChangesAsync();

            var subscriptionIndexes = await setup.Database
                .SqlQueryRaw<string>("SELECT indexname AS \"Value\" FROM pg_indexes WHERE schemaname = 'app' AND tablename = 'subscriptions'")
                .ToListAsync();
            Assert.Contains("ux_subscriptions_active_per_breeding_farm", subscriptionIndexes);
            Assert.Contains("ux_subscriptions_pending_per_breeding_farm", subscriptionIndexes);
        }

        await using (var invalidTrialContext = new CriatorioVirtualDbContext(options))
        {
            var pendingSubscription = await invalidTrialContext.Subscriptions
                .SingleAsync(item => item.Id == sameSubscriptionId);
            var trialStartedAt = invalidTrialContext.Entry(pendingSubscription)
                .Property(item => item.TrialStartedAtUtc);
            trialStartedAt.CurrentValue = CreatedAtUtc;
            trialStartedAt.IsModified = true;

            await Assert.ThrowsAsync<DbUpdateException>(() => invalidTrialContext.SaveChangesAsync());
        }

        await AssertRejectedSubscriptionState(
            options,
            sameSubscriptionId,
            SubscriptionStatus.Trial,
            CreatedAtUtc,
            CreatedAtUtc.AddDays(6),
            CreatedAtUtc.AddDays(6));
        await AssertRejectedSubscriptionState(
            options,
            sameSubscriptionId,
            SubscriptionStatus.Trial,
            CreatedAtUtc,
            CreatedAtUtc.AddDays(7),
            CreatedAtUtc.AddDays(8));
        await AssertRejectedSubscriptionState(
            options,
            sameSubscriptionId,
            SubscriptionStatus.GracePeriod,
            CreatedAtUtc,
            CreatedAtUtc.AddDays(7),
            CreatedAtUtc.AddDays(7),
            CreatedAtUtc.AddDays(7),
            CreatedAtUtc.AddDays(13));
        await AssertRejectedSubscriptionState(
            options,
            sameSubscriptionId,
            SubscriptionStatus.GracePeriod,
            CreatedAtUtc,
            CreatedAtUtc.AddDays(7),
            CreatedAtUtc.AddDays(7),
            CreatedAtUtc.AddDays(7));

        await using var firstUpdate = new CriatorioVirtualDbContext(options);
        await using var secondUpdate = new CriatorioVirtualDbContext(options);
        var firstLoaded = await firstUpdate.Subscriptions.SingleAsync(item => item.Id == sameSubscriptionId);
        var secondLoaded = await secondUpdate.Subscriptions.SingleAsync(item => item.Id == sameSubscriptionId);
        firstLoaded.ConfirmRecurringSubscription("customer-first", "subscription-first", CreatedAtUtc.AddDays(1));
        secondLoaded.ConfirmRecurringSubscription("customer-second", "subscription-second", CreatedAtUtc.AddDays(1));

        var sameRowRace = await Task.WhenAll(TrySave(firstUpdate), TrySave(secondUpdate));
        Assert.Single(sameRowRace, wasSaved => wasSaved);

        await using var firstPendingContext = new CriatorioVirtualDbContext(options);
        await using var secondPendingContext = new CriatorioVirtualDbContext(options);
        firstPendingContext.Subscriptions.Add(CreateSubscription(competingSubscriptionIds[0], secondFarmId));
        secondPendingContext.Subscriptions.Add(CreateSubscription(competingSubscriptionIds[1], secondFarmId));

        var distinctRowsRace = await Task.WhenAll(TrySave(firstPendingContext), TrySave(secondPendingContext));
        Assert.Single(distinctRowsRace, wasSaved => wasSaved);

        Guid trialSubscriptionId;
        await using (var activationContext = new CriatorioVirtualDbContext(options))
        {
            var pending = await activationContext.Subscriptions
                .SingleAsync(item => item.BreedingFarmId == secondFarmId);
            trialSubscriptionId = pending.Id;
            pending.ConfirmRecurringSubscription("customer-second-farm", "subscription-second-farm", CreatedAtUtc.AddDays(1));
            await activationContext.SaveChangesAsync();
        }

        await using (var crossTenantContext = new CriatorioVirtualDbContext(options))
        {
            crossTenantContext.Payments.Add(new Payment(
                Guid.NewGuid(),
                firstFarmId,
                trialSubscriptionId,
                "cross-tenant-payment",
                49.90m,
                "BRL",
                CreatedAtUtc.AddDays(8),
                CreatedAtUtc.AddDays(1)));

            await Assert.ThrowsAsync<DbUpdateException>(() => crossTenantContext.SaveChangesAsync());
        }

        await using (var paymentContext = new CriatorioVirtualDbContext(options))
        {
            for (var index = 0; index < 60; index++)
            {
                var createdAt = CreatedAtUtc.AddMinutes(index);
                paymentContext.Payments.Add(new Payment(
                    Guid.NewGuid(),
                    secondFarmId,
                    trialSubscriptionId,
                    $"payment-{index:D3}",
                    49.90m,
                    "BRL",
                    createdAt.AddDays(7),
                    createdAt));
            }

            await paymentContext.SaveChangesAsync();
        }

        await using var queryContext = new CriatorioVirtualDbContext(options);
        var query = queryContext.Payments
            .AsNoTracking()
            .Where(payment => payment.BreedingFarmId == secondFarmId && payment.SubscriptionId == trialSubscriptionId)
            .OrderByDescending(payment => payment.CreatedAtUtc)
            .ThenByDescending(payment => payment.Id)
            .Take(10);

        var page = await query.ToListAsync();

        Assert.Equal(10, page.Count);
        Assert.All(page, payment => Assert.Equal(secondFarmId, payment.BreedingFarmId));
        Assert.Contains("LIMIT", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await queryContext.Payments
            .Where(payment => payment.BreedingFarmId == firstFarmId && payment.SubscriptionId == trialSubscriptionId)
            .ToListAsync());
    }

    private static BreedingFarm CreateFarm(Guid id, string name) => new(
        id,
        CreatedAtUtc,
        name,
        "Owner",
        $"{name.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant()}@example.com",
        null,
        null,
        new BreedingFarmAddress(null, null, null, null, null, null, null));

    private static Subscription CreateSubscription(Guid id, Guid breedingFarmId) =>
        new(id, breedingFarmId, "standard", BillingCycle.Monthly, CreatedAtUtc);

    private static async Task AssertRejectedSubscriptionState(
        DbContextOptions<CriatorioVirtualDbContext> options,
        Guid subscriptionId,
        SubscriptionStatus status,
        DateTimeOffset trialStartedAtUtc,
        DateTimeOffset trialEndsAtUtc,
        DateTimeOffset nextChargeDueAtUtc,
        DateTimeOffset? gracePeriodStartedAtUtc = null,
        DateTimeOffset? gracePeriodEndsAtUtc = null)
    {
        await using var context = new CriatorioVirtualDbContext(options);
        var subscription = await context.Subscriptions.SingleAsync(item => item.Id == subscriptionId);

        SetProperty(context, subscription, nameof(Subscription.Status), status);
        SetProperty(context, subscription, nameof(Subscription.GatewayCustomerId), $"customer-{Guid.NewGuid():N}");
        SetProperty(context, subscription, nameof(Subscription.GatewaySubscriptionId), $"subscription-{Guid.NewGuid():N}");
        SetProperty(context, subscription, nameof(Subscription.TrialStartedAtUtc), trialStartedAtUtc);
        SetProperty(context, subscription, nameof(Subscription.TrialEndsAtUtc), trialEndsAtUtc);
        SetProperty(context, subscription, nameof(Subscription.NextChargeDueAtUtc), nextChargeDueAtUtc);
        SetProperty(context, subscription, nameof(Subscription.GracePeriodStartedAtUtc), gracePeriodStartedAtUtc);
        SetProperty(context, subscription, nameof(Subscription.GracePeriodEndsAtUtc), gracePeriodEndsAtUtc);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private static void SetProperty<TValue>(
        CriatorioVirtualDbContext context,
        Subscription subscription,
        string propertyName,
        TValue value)
    {
        var property = context.Entry(subscription).Property(propertyName);
        property.CurrentValue = value;
        property.IsModified = true;
    }

    private static async Task<bool> TrySave(CriatorioVirtualDbContext context)
    {
        try
        {
            await context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

}
