using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace WalletSync.Core;

public sealed class InMemoryChallengeStore : IChallengeStore
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, Challenge> _byNonce = new(StringComparer.Ordinal);

    public Task<Challenge> IssueAsync(string pubkey, CancellationToken ct = default)
    {
        var nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var c = new Challenge(nonce, pubkey, DateTimeOffset.UtcNow.Add(Ttl));
        _byNonce[nonce] = c;
        return Task.FromResult(c);
    }

    public Task<Challenge?> ConsumeAsync(string nonce, CancellationToken ct = default)
    {
        if (_byNonce.TryRemove(nonce, out var c) && c.ExpiresAt > DateTimeOffset.UtcNow)
            return Task.FromResult<Challenge?>(c);
        return Task.FromResult<Challenge?>(null);
    }
}
