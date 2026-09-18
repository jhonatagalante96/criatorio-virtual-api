using CriatorioVirtual.Infrastructure.Billing;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Billing;

public sealed class SubscriptionGracePeriodBlockingWorkerTests
{
    [Fact]
    public void TimeUntilNextSaoPauloMidnight_UsesSaoPauloCalendarDay()
    {
        var beforeMidnightUtc = new DateTimeOffset(2026, 9, 18, 2, 30, 0, TimeSpan.Zero);
        var afterMidnightUtc = new DateTimeOffset(2026, 9, 18, 4, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            TimeSpan.FromMinutes(30),
            SubscriptionGracePeriodBlockingWorker.TimeUntilNextSaoPauloMidnight(beforeMidnightUtc));
        Assert.Equal(
            TimeSpan.FromHours(23),
            SubscriptionGracePeriodBlockingWorker.TimeUntilNextSaoPauloMidnight(afterMidnightUtc));
    }
}
