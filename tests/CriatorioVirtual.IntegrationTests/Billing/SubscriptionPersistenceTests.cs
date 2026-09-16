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
    public async Task BillingPersistence_EnforcesTenantScopeTrialUniquenessAndBoundedPaymentQueries()
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
            setup.Subscriptions.AddRange(
                CreateSubscription(sameSubscriptionId, firstFarmId),
                CreateSubscription(competingSubscriptionIds[0], secondFarmId),
                CreateSubscription(competingSubscriptionIds[1], secondFarmId));
            await setup.SaveChangesAsync();
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
            competingSubscriptionIds[0],
            SubscriptionStatus.Trial,
            CreatedAtUtc,
            CreatedAtUtc.AddDays(6),
            CreatedAtUtc.AddDays(6));
        await AssertRejectedSubscriptionState(
            options,
            competingSubscriptionIds[0],
            SubscriptionStatus.Trial,
            CreatedAtUtc,
            CreatedAtUtc.AddDays(7),
            CreatedAtUtc.AddDays(8));
        await AssertRejectedSubscriptionState(
            options,
            competingSubscriptionIds[0],
            SubscriptionStatus.GracePeriod,
            CreatedAtUtc,
            CreatedAtUtc.AddDays(7),
            CreatedAtUtc.AddDays(7),
            CreatedAtUtc.AddDays(7),
            CreatedAtUtc.AddDays(13));
        await AssertRejectedSubscriptionState(
            options,
            competingSubscriptionIds[0],
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

        await using var firstTrialContext = new CriatorioVirtualDbContext(options);
        await using var secondTrialContext = new CriatorioVirtualDbContext(options);
        var firstCompeting = await firstTrialContext.Subscriptions.SingleAsync(item => item.Id == competingSubscriptionIds[0]);
        var secondCompeting = await secondTrialContext.Subscriptions.SingleAsync(item => item.Id == competingSubscriptionIds[1]);
        firstCompeting.ConfirmRecurringSubscription("customer-competing-1", "subscription-competing-1", CreatedAtUtc.AddDays(1));
        secondCompeting.ConfirmRecurringSubscription("customer-competing-2", "subscription-competing-2", CreatedAtUtc.AddDays(1));

        var distinctRowsRace = await Task.WhenAll(TrySave(firstTrialContext), TrySave(secondTrialContext));
        Assert.Single(distinctRowsRace, wasSaved => wasSaved);

        var trialSubscriptionId = await GetSuccessfulSubscriptionId(options, competingSubscriptionIds);
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

    private static async Task<Guid> GetSuccessfulSubscriptionId(
        DbContextOptions<CriatorioVirtualDbContext> options,
        IReadOnlyCollection<Guid> subscriptionIds)
    {
        await using var context = new CriatorioVirtualDbContext(options);
        return await context.Subscriptions
            .Where(item => subscriptionIds.Contains(item.Id) && item.Status == SubscriptionStatus.Trial)
            .Select(item => item.Id)
            .SingleAsync();
    }
}
