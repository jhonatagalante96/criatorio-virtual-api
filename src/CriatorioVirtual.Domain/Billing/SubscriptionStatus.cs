namespace CriatorioVirtual.Domain.Billing;

public enum SubscriptionStatus
{
    PendingSubscription = 1,
    Trial = 2,
    Active = 3,
    GracePeriod = 4,
    Cancelled = 5
}
