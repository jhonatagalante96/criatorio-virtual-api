using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Billing;

public sealed class GetSubscriptionQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<GetSubscriptionQuery, GetSubscriptionResult>
{
    public async Task<GetSubscriptionResult> Handle(
        GetSubscriptionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var access = await BillingQueryAccess.ResolveOwnerFarmAsync(dbContext, query.UserId, cancellationToken);
        if (access.Status != BillingQueryStatus.Success)
        {
            return GetSubscriptionResult.Failed(access.Status);
        }

        var subscription = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(candidate => candidate.BreedingFarmId == access.BreedingFarmId)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .ThenByDescending(candidate => candidate.Id)
            .Select(candidate => new SubscriptionQueryProjection(
                candidate.BreedingFarmId,
                candidate.PlanCode,
                candidate.BillingCycle,
                candidate.Status,
                candidate.TrialStartedAtUtc,
                candidate.TrialEndsAtUtc,
                candidate.NextChargeDueAtUtc,
                candidate.GracePeriodStartedAtUtc,
                candidate.GracePeriodEndsAtUtc,
                candidate.CreatedAtUtc,
                candidate.UpdatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is null)
        {
            return GetSubscriptionResult.Failed(BillingQueryStatus.SubscriptionNotFound);
        }

        int? daysRemaining = subscription.Status == SubscriptionStatus.GracePeriod &&
                             subscription.GracePeriodEndsAtUtc is { } gracePeriodEndsAtUtc
            ? Math.Max(0, (int)Math.Ceiling((gracePeriodEndsAtUtc - DateTimeOffset.UtcNow).TotalDays))
            : null;

        return GetSubscriptionResult.Succeeded(new SubscriptionQueryResult(
            subscription.BreedingFarmId,
            subscription.PlanCode,
            subscription.BillingCycle,
            subscription.Status,
            subscription.TrialStartedAtUtc,
            subscription.TrialEndsAtUtc,
            subscription.TrialEndsAtUtc,
            subscription.NextChargeDueAtUtc,
            subscription.GracePeriodStartedAtUtc,
            subscription.GracePeriodEndsAtUtc,
            daysRemaining,
            subscription.CreatedAtUtc,
            subscription.UpdatedAtUtc));
    }

    private sealed record SubscriptionQueryProjection(
        Guid BreedingFarmId,
        string PlanCode,
        BillingCycle BillingCycle,
        SubscriptionStatus Status,
        DateTimeOffset? TrialStartedAtUtc,
        DateTimeOffset? TrialEndsAtUtc,
        DateTimeOffset? NextChargeDueAtUtc,
        DateTimeOffset? GracePeriodStartedAtUtc,
        DateTimeOffset? GracePeriodEndsAtUtc,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);
}

public sealed class ListPaymentsQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<ListPaymentsQuery, ListPaymentsResult>
{
    public async Task<ListPaymentsResult> Handle(
        ListPaymentsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var access = await BillingQueryAccess.ResolveOwnerFarmAsync(dbContext, query.UserId, cancellationToken);
        if (access.Status != BillingQueryStatus.Success)
        {
            return ListPaymentsResult.Failed(access.Status);
        }

        var payments = dbContext.Payments
            .AsNoTracking()
            .Where(candidate => candidate.BreedingFarmId == access.BreedingFarmId);
        var totalCount = await payments.CountAsync(cancellationToken);
        var skip = (long)(query.Page - 1) * query.PageSize;
        PaymentQueryProjection[] rows;
        if (skip > int.MaxValue)
        {
            rows = [];
        }
        else
        {
            rows = await payments
                .OrderByDescending(candidate => candidate.DueAtUtc)
                .ThenByDescending(candidate => candidate.Id)
                .Skip((int)skip)
                .Take(query.PageSize)
                .Select(candidate => new PaymentQueryProjection(
                    candidate.Id,
                    candidate.Amount,
                    candidate.CurrencyCode,
                    candidate.DueAtUtc,
                    candidate.Status,
                    candidate.PaidAtUtc,
                    candidate.CreatedAtUtc))
                .ToArrayAsync(cancellationToken);
        }

        return ListPaymentsResult.Succeeded(
            access.BreedingFarmId!.Value,
            rows.Select(row => new PaymentQueryResult(
                row.PaymentId,
                row.Amount,
                row.CurrencyCode,
                row.DueAtUtc,
                row.Status,
                row.PaidAtUtc,
                row.CreatedAtUtc)).ToArray(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    private sealed record PaymentQueryProjection(
        Guid PaymentId,
        decimal Amount,
        string CurrencyCode,
        DateTimeOffset DueAtUtc,
        PaymentStatus Status,
        DateTimeOffset? PaidAtUtc,
        DateTimeOffset CreatedAtUtc);
}

internal sealed record BillingOwnerFarmAccess(BillingQueryStatus Status, Guid? BreedingFarmId);

internal static class BillingQueryAccess
{
    public static async Task<BillingOwnerFarmAccess> ResolveOwnerFarmAsync(
        CriatorioVirtualDbContext dbContext,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null)
        {
            return new BillingOwnerFarmAccess(BillingQueryStatus.UserNotFound, null);
        }

        if (user.SelectedBreedingFarmId is not { } breedingFarmId)
        {
            return new BillingOwnerFarmAccess(BillingQueryStatus.BreedingFarmNotSelected, null);
        }

        var hasActiveOwnerMembership = await dbContext.BreedingFarmUsers
            .AsNoTracking()
            .AnyAsync(
                membership =>
                    membership.BreedingFarmId == breedingFarmId &&
                    membership.UserId == userId &&
                    membership.IsActive &&
                    membership.Role == BreedingFarmRole.Owner,
                cancellationToken);
        return hasActiveOwnerMembership
            ? new BillingOwnerFarmAccess(BillingQueryStatus.Success, breedingFarmId)
            : new BillingOwnerFarmAccess(BillingQueryStatus.BreedingFarmNotFound, null);
    }
}
