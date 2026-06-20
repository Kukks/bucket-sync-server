using Npgsql;
using NpgsqlTypes;
using BucketSync.Core;

namespace BucketSync.Postgres;

public sealed class PostgresCredentialStore : ICredentialStore
{
    private readonly NpgsqlDataSource _ds;
    public PostgresCredentialStore(NpgsqlDataSource ds) => _ds = ds;

    public async Task<string?> ResolveBucketAsync(string scheme, string credentialId, CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand(
            "SELECT bucket_id FROM credentials WHERE scheme=@s AND credential_id=@c");
        cmd.Parameters.AddWithValue("s", scheme);
        cmd.Parameters.AddWithValue("c", credentialId);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    public async Task<VerifiedCredential?> GetAsync(string scheme, string credentialId, CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand(
            "SELECT scheme, credential_id, public_key, label FROM credentials WHERE scheme=@s AND credential_id=@c");
        cmd.Parameters.AddWithValue("s", scheme);
        cmd.Parameters.AddWithValue("c", credentialId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new VerifiedCredential(r.GetString(0), r.GetString(1),
            r.IsDBNull(2) ? null : (byte[])r[2], r.IsDBNull(3) ? null : r.GetString(3));
    }

    public async Task<bool> BindAsync(VerifiedCredential credential, string bucketId, CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand(
            @"INSERT INTO credentials (scheme, credential_id, bucket_id, public_key, label)
              VALUES (@s, @c, @b, @pk, @l)
              ON CONFLICT (scheme, credential_id) DO NOTHING");
        cmd.Parameters.AddWithValue("s", credential.Scheme);
        cmd.Parameters.AddWithValue("c", credential.CredentialId);
        cmd.Parameters.AddWithValue("b", bucketId);
        cmd.Parameters.Add(new NpgsqlParameter("pk", NpgsqlDbType.Bytea) { Value = (object?)credential.PublicKey ?? DBNull.Value });
        cmd.Parameters.AddWithValue("l", (object?)credential.Label ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync(ct) > 0; // 0 rows = conflict (already bound)
    }

    public async Task<IReadOnlyList<VerifiedCredential>> ListAsync(string bucketId, CancellationToken ct = default)
    {
        var list = new List<VerifiedCredential>();
        await using var cmd = _ds.CreateCommand(
            "SELECT scheme, credential_id, public_key, label FROM credentials WHERE bucket_id=@b");
        cmd.Parameters.AddWithValue("b", bucketId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new VerifiedCredential(
                r.GetString(0), r.GetString(1),
                r.IsDBNull(2) ? null : (byte[])r[2],
                r.IsDBNull(3) ? null : r.GetString(3)));
        return list;
    }
}
