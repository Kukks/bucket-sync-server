using Npgsql;
using NpgsqlTypes;
using WalletSync.Core;

namespace WalletSync.Postgres;

public sealed class PostgresBucketStore : IBucketStore
{
    private readonly NpgsqlDataSource _ds;
    public PostgresBucketStore(NpgsqlDataSource ds) => _ds = ds;

    public async Task EnsureBucketAsync(string bucketId, string ownerPubkey, CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand(
            "INSERT INTO buckets (bucket_id, owner_pubkey) VALUES (@b, @p) ON CONFLICT (bucket_id) DO NOTHING");
        cmd.Parameters.AddWithValue("b", bucketId);
        cmd.Parameters.AddWithValue("p", ownerPubkey);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<BucketHead> GetHeadAsync(string bucketId, CancellationToken ct = default)
    {
        await using var cmd = _ds.CreateCommand("SELECT current_seq, content_hash FROM buckets WHERE bucket_id=@b");
        cmd.Parameters.AddWithValue("b", bucketId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return new BucketHead(bucketId, 0, "");
        return new BucketHead(bucketId, r.GetInt64(0), r.GetString(1));
    }

    public async Task<IReadOnlyList<BucketEntry>> GetBatchAsync(string bucketId, IReadOnlyList<string> keys, CancellationToken ct = default)
    {
        var list = new List<BucketEntry>();
        if (keys.Count == 0) return list;
        await using var cmd = _ds.CreateCommand(
            "SELECT key, version, seq, content_hash, scheme, deleted, value FROM entries WHERE bucket_id=@b AND key = ANY(@keys)");
        cmd.Parameters.AddWithValue("b", bucketId);
        cmd.Parameters.AddWithValue("keys", keys.ToArray());
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new BucketEntry(bucketId, r.GetString(0), r.GetInt64(1), r.GetInt64(2),
                r.GetString(3), r.GetString(4), r.GetBoolean(5), (byte[])r[6]));
        return list;
    }

    public async Task<CommitResult> CommitBatchAsync(string bucketId, IReadOnlyList<WriteOp> ops, CancellationToken ct = default)
    {
        if (ops.Count == 0) throw new ArgumentException("empty batch", nameof(ops));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var op in ops)
            if (!seen.Add(op.Key)) throw new ArgumentException($"duplicate key in batch: {op.Key}", nameof(ops));

        await using var conn = await _ds.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var locker = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@b, 0))", conn, tx))
        {
            locker.Parameters.AddWithValue("b", bucketId);
            await locker.ExecuteScalarAsync(ct);
        }

        // current_seq (bucket guaranteed to exist: provisioned at auth)
        long currentSeq;
        await using (var headCmd = new NpgsqlCommand("SELECT current_seq FROM buckets WHERE bucket_id=@b", conn, tx))
        {
            headCmd.Parameters.AddWithValue("b", bucketId);
            currentSeq = (long)(await headCmd.ExecuteScalarAsync(ct))!;
        }

        // current versions for the op keys
        var current = new Dictionary<string, long>(StringComparer.Ordinal);
        await using (var verCmd = new NpgsqlCommand("SELECT key, version FROM entries WHERE bucket_id=@b AND key = ANY(@keys)", conn, tx))
        {
            verCmd.Parameters.AddWithValue("b", bucketId);
            verCmd.Parameters.AddWithValue("keys", ops.Select(o => o.Key).ToArray());
            await using var r = await verCmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) current[r.GetString(0)] = r.GetInt64(1);
        }

        var conflicts = new List<Conflict>();
        foreach (var op in ops)
        {
            long have = current.TryGetValue(op.Key, out var v) ? v : 0;
            if (op.ExpectedVersion != have) conflicts.Add(new Conflict(op.Key, have));
        }
        if (conflicts.Count > 0)
        {
            await tx.RollbackAsync(ct);
            return new CommitResult(false, currentSeq, conflicts);
        }

        long newSeq = currentSeq + 1;
        await using (var bump = new NpgsqlCommand("UPDATE buckets SET current_seq=@s WHERE bucket_id=@b", conn, tx))
        {
            bump.Parameters.AddWithValue("s", newSeq);
            bump.Parameters.AddWithValue("b", bucketId);
            await bump.ExecuteNonQueryAsync(ct);
        }

        foreach (var op in ops)
        {
            long have = current.TryGetValue(op.Key, out var v) ? v : 0;
            var value = op.Delete ? Array.Empty<byte>() : op.Value;
            await using var up = new NpgsqlCommand(
                @"INSERT INTO entries (bucket_id, key, version, seq, content_hash, scheme, deleted, value, updated_at)
                  VALUES (@b, @k, @ver, @seq, @ch, @scheme, @del, @val, now())
                  ON CONFLICT (bucket_id, key) DO UPDATE SET
                    version=excluded.version, seq=excluded.seq, content_hash=excluded.content_hash,
                    scheme=excluded.scheme, deleted=excluded.deleted, value=excluded.value, updated_at=now()", conn, tx);
            up.Parameters.AddWithValue("b", bucketId);
            up.Parameters.AddWithValue("k", op.Key);
            up.Parameters.AddWithValue("ver", have + 1);
            up.Parameters.AddWithValue("seq", newSeq);
            up.Parameters.AddWithValue("ch", Hashing.Sha256Hex(value));
            up.Parameters.AddWithValue("scheme", op.Scheme);
            up.Parameters.AddWithValue("del", op.Delete);
            up.Parameters.Add(new NpgsqlParameter("val", NpgsqlDbType.Bytea) { Value = value });
            await up.ExecuteNonQueryAsync(ct);
        }

        // Recompute bucket content_hash over the full live set (parity with in-memory store).
        var all = new List<BucketEntry>();
        await using (var allCmd = new NpgsqlCommand("SELECT key, version, content_hash, deleted FROM entries WHERE bucket_id=@b", conn, tx))
        {
            allCmd.Parameters.AddWithValue("b", bucketId);
            await using var r = await allCmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                all.Add(new BucketEntry(bucketId, r.GetString(0), r.GetInt64(1), 0, r.GetString(2), "", r.GetBoolean(3), Array.Empty<byte>()));
        }
        var contentHash = Hashing.BucketContentHash(all);
        await using (var setHash = new NpgsqlCommand("UPDATE buckets SET content_hash=@h WHERE bucket_id=@b", conn, tx))
        {
            setHash.Parameters.AddWithValue("h", contentHash);
            setHash.Parameters.AddWithValue("b", bucketId);
            await setHash.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return new CommitResult(true, newSeq, Array.Empty<Conflict>());
    }

    public async Task<DiffPage> DiffAsync(string bucketId, long sinceSeq, int limit, CancellationToken ct = default)
    {
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));

        // Find the cutoff seq = the (limit)-th distinct seq greater than sinceSeq.
        long? cutoff;
        await using (var cutCmd = _ds.CreateCommand(
            "SELECT seq FROM entries WHERE bucket_id=@b AND seq > @s GROUP BY seq ORDER BY seq LIMIT @lim"))
        {
            cutCmd.Parameters.AddWithValue("b", bucketId);
            cutCmd.Parameters.AddWithValue("s", sinceSeq);
            cutCmd.Parameters.AddWithValue("lim", limit);
            long? last = null;
            await using var r = await cutCmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) last = r.GetInt64(0);
            cutoff = last;
        }
        if (cutoff is null) return new DiffPage(Array.Empty<BucketEntry>(), sinceSeq, false);

        var entries = new List<BucketEntry>();
        await using (var pageCmd = _ds.CreateCommand(
            @"SELECT key, version, seq, content_hash, scheme, deleted, value FROM entries
              WHERE bucket_id=@b AND seq > @s AND seq <= @cut ORDER BY seq, key"))
        {
            pageCmd.Parameters.AddWithValue("b", bucketId);
            pageCmd.Parameters.AddWithValue("s", sinceSeq);
            pageCmd.Parameters.AddWithValue("cut", cutoff.Value);
            await using var r = await pageCmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                entries.Add(new BucketEntry(bucketId, r.GetString(0), r.GetInt64(1), r.GetInt64(2),
                    r.GetString(3), r.GetString(4), r.GetBoolean(5), (byte[])r[6]));
        }

        bool hasMore;
        await using (var moreCmd = _ds.CreateCommand("SELECT EXISTS(SELECT 1 FROM entries WHERE bucket_id=@b AND seq > @cut)"))
        {
            moreCmd.Parameters.AddWithValue("b", bucketId);
            moreCmd.Parameters.AddWithValue("cut", cutoff.Value);
            hasMore = (bool)(await moreCmd.ExecuteScalarAsync(ct))!;
        }
        return new DiffPage(entries, cutoff.Value, hasMore);
    }
}
