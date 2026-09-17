namespace CriatorioVirtual.Domain.Billing;

public enum PaymentAttemptStatus
{
    Processing = 1,
    AwaitingConfirmation = 2,
    Failed = 3,
    OutcomeUnknown = 4
}
