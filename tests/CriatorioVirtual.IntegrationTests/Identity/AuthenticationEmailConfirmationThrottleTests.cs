using CriatorioVirtual.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Identity;

public sealed class AuthenticationEmailConfirmationThrottleTests
{
    [Fact]
    public void TryAcquire_NormalizesEmailAndBlocksWithinTheConfiguredWindow()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var throttle = new AuthenticationEmailConfirmationThrottle(
            cache,
            Options.Create(new AuthenticationEmailOptions
            {
                ConfirmationResendWindow = TimeSpan.FromMinutes(5)
            }));

        Assert.True(throttle.TryAcquire(" User@Example.com "));
        Assert.False(throttle.TryAcquire("user@example.com"));
        Assert.True(throttle.TryAcquire("other@example.com"));
    }
}
