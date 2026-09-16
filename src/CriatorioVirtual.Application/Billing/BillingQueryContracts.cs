using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;

namespace CriatorioVirtual.Application.Billing;

public static class BillingQueryLimits
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
}

public enum BillingQueryStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    SubscriptionNotFound
}

public sealed record GetSubscriptionQuery(Guid UserId) : IQuery<GetSubscriptionResult>;

public sealed record GetSubscriptionResult(
    BillingQueryStatus Status,
    SubscriptionQueryResult? Subscription)
{
    public static GetSubscriptionResult Succeeded(SubscriptionQueryResult subscription) =>
        new(BillingQueryStatus.Success, subscription);

    public static GetSubscriptionResult Failed(BillingQueryStatus status) =>
        new(status, null);
}

public sealed record SubscriptionQueryResult(
    Guid BreedingFarmId,
    string PlanCode,
    BillingCycle BillingCycle,
    SubscriptionStatus Status,
    DateTimeOffset? TrialStartedAtUtc,
    DateTimeOffset? TrialEndsAtUtc,
    DateTimeOffset? FirstChargeDueAtUtc,
    DateTimeOffset? NextChargeDueAtUtc,
    DateTimeOffset? GracePeriodStartedAtUtc,
    DateTimeOffset? GracePeriodEndsAtUtc,
    int? GracePeriodDaysRemaining,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ListPaymentsQuery(
    Guid UserId,
    int Page,
    int PageSize) : IQuery<ListPaymentsResult>;

public sealed record ListPaymentsResult(
    BillingQueryStatus Status,
    Guid? BreedingFarmId,
    IReadOnlyCollection<PaymentQueryResult> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public static ListPaymentsResult Succeeded(
        Guid breedingFarmId,
        IReadOnlyCollection<PaymentQueryResult> items,
        int page,
        int pageSize,
        int totalCount) =>
        new(BillingQueryStatus.Success, breedingFarmId, items, page, pageSize, totalCount);

    public static ListPaymentsResult Failed(BillingQueryStatus status) =>
        new(status, null, [], 0, 0, 0);
}

public sealed record PaymentQueryResult(
    Guid PaymentId,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset DueAtUtc,
    PaymentStatus Status,
    DateTimeOffset? PaidAtUtc,
    DateTimeOffset CreatedAtUtc);
