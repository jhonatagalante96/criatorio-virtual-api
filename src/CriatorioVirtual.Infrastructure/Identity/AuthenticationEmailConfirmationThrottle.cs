using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CriatorioVirtual.Infrastructure.Identity;

public sealed class AuthenticationEmailConfirmationThrottle(
    IMemoryCache cache,
    IOptions<AuthenticationEmailOptions> options)
    : IAuthenticationEmailConfirmationThrottle
{
    private readonly object gate = new();

    public bool TryAcquire(string normalizedEmail)
    {
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return false;
        }

        var key = $"authentication-confirmation:{normalizedEmail.Trim().ToUpperInvariant()}";
        lock (gate)
        {
            if (cache.TryGetValue(key, out _))
            {
                return false;
            }

            cache.Set(
                key,
                true,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = options.Value.ConfirmationResendWindow,
                    Size = 1
                });
            return true;
        }
    }
}
