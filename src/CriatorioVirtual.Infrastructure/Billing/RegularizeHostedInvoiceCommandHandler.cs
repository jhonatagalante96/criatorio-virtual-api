using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Billing;

public sealed class RegularizeHostedInvoiceCommandHandler(
    CriatorioVirtualDbContext dbContext,
    IBillingGateway billingGateway)
    : ICommandHandler<RegularizeHostedInvoiceCommand, RegularizeHostedInvoiceResult>
{
    public async Task<RegularizeHostedInvoiceResult> Handle(
        RegularizeHostedInvoiceCommand command,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return RegularizeHostedInvoiceResult.Failed(RegularizeHostedInvoiceStatus.UserNotFound);
        }

        if (user.SelectedBreedingFarmId is not { } farmId)
        {
            return RegularizeHostedInvoiceResult.Failed(RegularizeHostedInvoiceStatus.BreedingFarmNotSelected);
        }

        var isOwner = await dbContext.BreedingFarmUsers.AsNoTracking().AnyAsync(
            membership => membership.BreedingFarmId == farmId && membership.UserId == user.Id &&
                          membership.IsActive && membership.Role == BreedingFarmRole.Owner,
            cancellationToken);
        if (!isOwner)
        {
            return RegularizeHostedInvoiceResult.Failed(RegularizeHostedInvoiceStatus.NotFarmOwner);
        }

        var payment = await dbContext.Payments.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.PaymentId && candidate.BreedingFarmId == farmId, cancellationToken);
        if (payment is null)
        {
            return RegularizeHostedInvoiceResult.Failed(RegularizeHostedInvoiceStatus.PaymentNotFound);
        }

        if (payment.Status == PaymentStatus.Confirmed)
        {
            return RegularizeHostedInvoiceResult.Failed(RegularizeHostedInvoiceStatus.PaymentAlreadyConfirmed);
        }

        var subscription = await dbContext.Subscriptions.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == payment.SubscriptionId && candidate.BreedingFarmId == farmId, cancellationToken);
        if (subscription is null || subscription.Status is not (SubscriptionStatus.GracePeriod or SubscriptionStatus.Blocked))
        {
            return RegularizeHostedInvoiceResult.Failed(RegularizeHostedInvoiceStatus.SubscriptionNotRecoverable);
        }

        if (subscription.NextChargeDueAtUtc is not { } dueAtUtc ||
            DateOnly.FromDateTime(dueAtUtc.UtcDateTime) != DateOnly.FromDateTime(payment.DueAtUtc.UtcDateTime) ||
            payment.Status is not (PaymentStatus.Pending or PaymentStatus.Failed))
        {
            return RegularizeHostedInvoiceResult.Failed(RegularizeHostedInvoiceStatus.PaymentNotCurrent);
        }

        if (string.IsNullOrWhiteSpace(subscription.GatewayCustomerId) ||
            string.IsNullOrWhiteSpace(subscription.GatewaySubscriptionId))
        {
            return RegularizeHostedInvoiceResult.Failed(RegularizeHostedInvoiceStatus.GatewayPaymentMismatch);
        }

        BillingGatewayPayment? remote;
        try
        {
            remote = await billingGateway.GetPaymentAsync(payment.GatewayPaymentId, cancellationToken);
        }
        catch (Exception exception) when (
            exception is BillingGatewayException or HttpRequestException ||
            exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return RegularizeHostedInvoiceResult.Failed(RegularizeHostedInvoiceStatus.GatewayUnavailable);
        }

        if (remote is null)
        {
            return RegularizeHostedInvoiceResult.Failed(RegularizeHostedInvoiceStatus.GatewayPaymentMismatch);
        }

        if (!string.Equals(remote.Id, payment.GatewayPaymentId, StringComparison.Ordinal) ||
            !string.Equals(remote.CustomerId, subscription.GatewayCustomerId, StringComparison.Ordinal) ||
            !string.Equals(remote.SubscriptionId, subscription.GatewaySubscriptionId, StringComparison.Ordinal) ||
            remote.Amount != payment.Amount ||
            remote.DueDate != DateOnly.FromDateTime(payment.DueAtUtc.UtcDateTime))
        {
            return RegularizeHostedInvoiceResult.Failed(RegularizeHostedInvoiceStatus.GatewayPaymentMismatch);
        }

        if (remote.Status is not ("PENDING" or "OVERDUE"))
        {
            return RegularizeHostedInvoiceResult.Failed(
                remote.Status is "CONFIRMED" or "RECEIVED" or "RECEIVED_IN_CASH"
                    ? RegularizeHostedInvoiceStatus.PaymentAlreadyConfirmed
                    : RegularizeHostedInvoiceStatus.PaymentNotCurrent);
        }

        if (!IsAsaasInvoiceUrl(remote.InvoiceUrl))
        {
            return RegularizeHostedInvoiceResult.Failed(RegularizeHostedInvoiceStatus.InvalidInvoiceUrl);
        }

        return new RegularizeHostedInvoiceResult(
            RegularizeHostedInvoiceStatus.AwaitingCustomerPayment,
            payment.Id,
            payment.Status,
            remote.InvoiceUrl);
    }

    private static bool IsAsaasInvoiceUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.Port == 443 &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        (uri.Host.Equals("asaas.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".asaas.com", StringComparison.OrdinalIgnoreCase));
}
