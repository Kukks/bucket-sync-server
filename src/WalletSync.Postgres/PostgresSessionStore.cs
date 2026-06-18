using System.Security.Cryptography;
using Npgsql;
using WalletSync.Core;

namespace WalletSync.Postgres;

public sealed class PostgresSessionStore : ISessionStore
{
    public static readonly TimeSpan Ttl = TimeSpan.FromDays(30);
    private readonly NpgsqlDataSource _ds;
    public PostgresSessionStore(NpgsqlDataSource ds) => _ds = ds;

    public async Task<Session> CreateAsync(Principal principal, string? device, CancellationToken ct = default)
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var expires = DateTimeOffset.UtcNow.Add(Ttl);
        await using var cmd = _ds.CreateCommand(
            "INSERT INTO sessions (token_hash, bucket_id, device, expires_at) VALUES (@h, @b, @d, @e)");
        cmd.Parameters.AddWithValue("h", Hashing.Sha256Hex(Convert.FromHexString(token)));
        cmd.Parameters.AddWithValue("b", principal.BucketId);
        cmd.Parameters.AddWithValue("d", (object?)device ?? DBNull.Value);
        cmd.Parameters.AddWithValue("e", expires);
        await cmd.ExecuteNonQueryAsync(ct);
        return new Session(token, principal.BucketId, device, expires);
    }

    public async Task<Session?> ValidateAsync(string token, CancellationToken ct = default)
    {
        string hash;
        try { hash = Hashing.Sha256Hex(Convert.FromHexString(token)); }
        catch (FormatException) { return null; }

        await using var cmd = _ds.CreateCommand(
            @"UPDATE sessions SET last_seen = now()
              WHERE token_hash = @h AND expires_at > now()
              RETURNING bucket_id, device, expires_at");
        cmd.Parameters.AddWithValue("h", hash);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new Session(token, r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetFieldValue<DateTimeOffset>(2));
    }

    public async Task RevokeAsync(string token, CancellationToken ct = default)
    {
        string hash;
        try { hash = Hashing.Sha256Hex(Convert.FromHexString(token)); }
        catch (FormatException) { return; }
        await using var cmd = _ds.CreateCommand("DELETE FROM sessions WHERE token_hash = @h");
        cmd.Parameters.AddWithValue("h", hash);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
