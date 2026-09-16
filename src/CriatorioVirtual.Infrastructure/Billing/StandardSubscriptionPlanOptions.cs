using CriatorioVirtual.Domain.Billing;

namespace CriatorioVirtual.Infrastructure.Billing;

public sealed class StandardSubscriptionPlanOptions
{
    public const string SectionName = "Billing:Plans:Standard";

    public const string PlanCode = "standard";

    private const decimal MaximumAmount = Subscription.MaximumAgreedAmount;

    public decimal? MonthlyAmount { get; set; }

    public decimal? AnnualAmount { get; set; }

    public decimal? GetAmount(BillingCycle billingCycle)
    {
        var amount = billingCycle switch
        {
            BillingCycle.Monthly => MonthlyAmount,
            BillingCycle.Annual => AnnualAmount,
            _ => null
        };

        return amount is > 0 and <= MaximumAmount &&
               decimal.Round(amount.Value, 2, MidpointRounding.ToEven) == amount.Value
            ? amount
            : null;
    }
}
