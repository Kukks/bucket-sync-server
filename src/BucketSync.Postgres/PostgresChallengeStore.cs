using System.Security.Cryptography;
using Npgsql;
using BucketSync.Core;

namespace BucketSync.Postgres;

public sealed class PostgresChallengeStore : IChallengeStore
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private readonly NpgsqlDataSource _ds;
    public PostgresChallengeStore(NpgsqlDataSource ds) => _ds = ds;

    public async Task<Challenge> IssueAsync(string scheme, CancellationToken ct = default)
    {
        var nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var expires = DateTimeOffset.UtcNow.Add(Ttl);
        await using var cmd = _ds.CreateCommand(
            "INSERT INTO challenges (nonce, scheme, expires_at) VALUES (@n, @s, @e)");
        cmd.Parameters.AddWithValue("n", nonce);
        cmd.Parameters.AddWithValue("s", scheme);
        cmd.Parameters.AddWithValue("e", expires);
        await cmd.ExecuteNonQueryAsync(ct);
        return new Challenge(nonce, scheme, expires);
    }

    public async Task<Challenge?> ConsumeAsync(string nonce, CancellationToken ct = default)
    {
        // Atomically mark consumed; return only if it was unconsumed and unexpired.
        await using var cmd = _ds.CreateCommand(
            @"UPDATE challenges SET consumed = true
              WHERE nonce = @n AND consumed = false AND expires_at > now()
              RETURNING scheme, expires_at");
        cmd.Parameters.AddWithValue("n", nonce);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new Challenge(nonce, r.GetString(0), r.GetFieldValue<DateTimeOffset>(1));
    }
}
