using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;

namespace CriatorioVirtual.Application.Billing;

public sealed record RegularizeHostedInvoiceCommand(Guid UserId, Guid PaymentId)
    : ICommand<RegularizeHostedInvoiceResult>;

public enum RegularizeHostedInvoiceStatus
{
    AwaitingCustomerPayment,
    PaymentNotFound,
    UserNotFound,
    BreedingFarmNotSelected,
    NotFarmOwner,
    SubscriptionNotRecoverable,
    PaymentNotCurrent,
    PaymentAlreadyConfirmed,
    GatewayPaymentMismatch,
    GatewayUnavailable,
    InvalidInvoiceUrl
}

public sealed record RegularizeHostedInvoiceResult(
    RegularizeHostedInvoiceStatus Status,
    Guid? PaymentId = null,
    PaymentStatus? PaymentStatus = null,
    string? PaymentUrl = null)
{
    public static RegularizeHostedInvoiceResult Failed(RegularizeHostedInvoiceStatus status) => new(status);
}
