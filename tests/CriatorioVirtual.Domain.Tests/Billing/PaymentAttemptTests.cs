using CriatorioVirtual.Domain.Billing;
using Xunit;

namespace CriatorioVirtual.Domain.Tests.Billing;

public sealed class PaymentAttemptTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(PaymentAttemptStatus.AwaitingConfirmation)]
    [InlineData(PaymentAttemptStatus.Failed)]
    public void Attempt_CanLeaveProcessingOnlyOnce(PaymentAttemptStatus finalStatus)
    {
        var attempt = CreateAttempt();
        Transition(attempt, finalStatus, CreatedAtUtc.AddMinutes(1));

        Assert.Equal(finalStatus, attempt.Status);
        Assert.Equal(CreatedAtUtc.AddMinutes(1), attempt.UpdatedAtUtc);
        Assert.Throws<InvalidOperationException>(() => Transition(
            attempt,
            finalStatus == PaymentAttemptStatus.Failed
                ? PaymentAttemptStatus.AwaitingConfirmation
                : PaymentAttemptStatus.Failed,
            CreatedAtUtc.AddMinutes(2)));
    }

    [Fact]
    public void UnknownOutcome_CanReconcileToPaidOrUnpaid()
    {
        var paidAttempt = CreateAttempt();
        paidAttempt.MarkOutcomeUnknown(CreatedAtUtc.AddMinutes(1));
        paidAttempt.MarkAwaitingConfirmation(CreatedAtUtc.AddMinutes(2));
        Assert.Equal(PaymentAttemptStatus.AwaitingConfirmation, paidAttempt.Status);

        var unpaidAttempt = CreateAttempt();
        unpaidAttempt.MarkOutcomeUnknown(CreatedAtUtc.AddMinutes(1));
        unpaidAttempt.MarkFailed(CreatedAtUtc.AddMinutes(2));
        Assert.Equal(PaymentAttemptStatus.Failed, unpaidAttempt.Status);
    }

    [Fact]
    public void Constructor_RequiresAWellFormedFingerprintAndIdentifiers()
    {
        Assert.Throws<ArgumentException>(() => new PaymentAttempt(
            Guid.NewGuid(),
            Guid.Empty,
            Guid.NewGuid(),
            new string('A', PaymentAttempt.RequestFingerprintLength),
            CreatedAtUtc));
        Assert.Throws<ArgumentException>(() => new PaymentAttempt(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.Empty,
            new string('A', PaymentAttempt.RequestFingerprintLength),
            CreatedAtUtc));
        Assert.Throws<ArgumentException>(() => new PaymentAttempt(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string('Z', PaymentAttempt.RequestFingerprintLength),
            CreatedAtUtc));
    }

    private static PaymentAttempt CreateAttempt() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        new string('A', PaymentAttempt.RequestFingerprintLength),
        CreatedAtUtc);

    private static void Transition(PaymentAttempt attempt, PaymentAttemptStatus status, DateTimeOffset atUtc)
    {
        switch (status)
        {
            case PaymentAttemptStatus.AwaitingConfirmation:
                attempt.MarkAwaitingConfirmation(atUtc);
                break;
            case PaymentAttemptStatus.Failed:
                attempt.MarkFailed(atUtc);
                break;
            case PaymentAttemptStatus.OutcomeUnknown:
                attempt.MarkOutcomeUnknown(atUtc);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }
    }
}
