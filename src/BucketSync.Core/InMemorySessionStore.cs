using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace BucketSync.Core;

public sealed class InMemorySessionStore : ISessionStore
{
    public static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    private sealed record Stored(string BucketId, string? Device, DateTimeOffset ExpiresAt);
    private readonly ConcurrentDictionary<string, Stored> _byTokenHash = new(StringComparer.Ordinal);

    public Task<Session> CreateAsync(Principal principal, string? device, CancellationToken ct = default)
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var expires = DateTimeOffset.UtcNow.Add(Ttl);
        _byTokenHash[Hashing.Sha256Hex(Convert.FromHexString(token))] = new Stored(principal.BucketId, device, expires);
        return Task.FromResult(new Session(token, principal.BucketId, device, expires));
    }

    public Task<Session?> ValidateAsync(string token, CancellationToken ct = default)
    {
        Session? result = null;
        try
        {
            var hash = Hashing.Sha256Hex(Convert.FromHexString(token));
            if (_byTokenHash.TryGetValue(hash, out var s) && s.ExpiresAt > DateTimeOffset.UtcNow)
                result = new Session(token, s.BucketId, s.Device, s.ExpiresAt);
        }
        catch (FormatException) { /* non-hex token => invalid */ }
        return Task.FromResult(result);
    }

    public Task RevokeAsync(string token, CancellationToken ct = default)
    {
        try { _byTokenHash.TryRemove(Hashing.Sha256Hex(Convert.FromHexString(token)), out _); }
        catch (FormatException) { }
        return Task.CompletedTask;
    }
}
