using CriatorioVirtual.Domain.Billing;
using Xunit;

namespace CriatorioVirtual.Domain.Tests.Billing;

public sealed class PaymentTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Confirm_IsIdempotentForTheSamePaidTimestamp()
    {
        var payment = CreatePayment();
        var paidAt = CreatedAtUtc.AddDays(7);

        payment.Confirm(paidAt);
        payment.Confirm(paidAt);

        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
        Assert.Equal(paidAt, payment.PaidAtUtc);
        Assert.Equal(paidAt, payment.UpdatedAtUtc);
    }

    [Fact]
    public void Confirm_ReconcilesANewerSuccessButRejectsAnOlderEvent()
    {
        var payment = CreatePayment();
        var failedAt = CreatedAtUtc.AddDays(7);
        payment.Fail(failedAt);

        Assert.Throws<InvalidOperationException>(() => payment.Confirm(failedAt.AddMinutes(-1)));
        Assert.Equal(PaymentStatus.Failed, payment.Status);

        var paidAt = failedAt.AddMinutes(1);
        payment.Confirm(paidAt);

        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
        Assert.Equal(paidAt, payment.PaidAtUtc);
    }

    [Fact]
    public void Fail_IsIdempotentAndCannotUndoAConfirmedPayment()
    {
        var failedPayment = CreatePayment();
        var failedAt = CreatedAtUtc.AddDays(7);
        failedPayment.Fail(failedAt);
        failedPayment.Fail(failedAt.AddMinutes(1));

        Assert.Equal(PaymentStatus.Failed, failedPayment.Status);
        Assert.Equal(failedAt, failedPayment.UpdatedAtUtc);

        var paidPayment = CreatePayment();
        paidPayment.Confirm(failedAt);
        Assert.Throws<InvalidOperationException>(() => paidPayment.Fail(failedAt.AddMinutes(1)));
        Assert.Equal(PaymentStatus.Confirmed, paidPayment.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveAmounts(decimal amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreatePayment(amount));
    }

    private static Payment CreatePayment(decimal amount = 49.90m) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "payment-123",
        amount,
        "brl",
        CreatedAtUtc.AddDays(7),
        CreatedAtUtc);
}
