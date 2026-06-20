using System.Collections.Concurrent;

namespace BucketSync.Core;

public sealed class InMemoryBucketStore : IBucketStore
{
    private sealed class Bucket
    {
        public long CurrentSeq;
        public string ContentHash = "";
        public readonly Dictionary<string, BucketEntry> Entries = new(StringComparer.Ordinal);
        public readonly object Gate = new();
    }

    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);

    public Task EnsureBucketAsync(string bucketId, CancellationToken ct = default)
    {
        _buckets.GetOrAdd(bucketId, _ => new Bucket());
        return Task.CompletedTask;
    }

    public Task<BucketHead> GetHeadAsync(string bucketId, CancellationToken ct = default)
    {
        if (!_buckets.TryGetValue(bucketId, out var b))
            return Task.FromResult(new BucketHead(bucketId, 0, ""));
        lock (b.Gate)
            return Task.FromResult(new BucketHead(bucketId, b.CurrentSeq, b.ContentHash));
    }

    public Task<IReadOnlyList<BucketEntry>> GetBatchAsync(string bucketId, IReadOnlyList<string> keys, CancellationToken ct = default)
    {
        var list = new List<BucketEntry>();
        if (_buckets.TryGetValue(bucketId, out var b))
            lock (b.Gate)
                foreach (var k in keys)
                    if (b.Entries.TryGetValue(k, out var e)) list.Add(e);
        return Task.FromResult<IReadOnlyList<BucketEntry>>(list);
    }

    public Task<CommitResult> CommitBatchAsync(string bucketId, IReadOnlyList<WriteOp> ops, CancellationToken ct = default)
    {
        if (ops.Count == 0) throw new ArgumentException("empty batch", nameof(ops));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var op in ops)
            if (!seen.Add(op.Key))
                throw new ArgumentException($"duplicate key in batch: {op.Key}", nameof(ops));

        // The bucket must be provisioned first (EnsureBucketAsync) — parity with PostgresBucketStore,
        // which requires the bucket row to exist before a commit (provisioning happens at auth/TOFU).
        if (!_buckets.TryGetValue(bucketId, out var b))
            throw new InvalidOperationException($"bucket not provisioned: {bucketId}");
        lock (b.Gate)
        {
            var conflicts = new List<Conflict>();
            foreach (var op in ops)
            {
                long current = b.Entries.TryGetValue(op.Key, out var e) ? e.Version : 0;
                if (op.ExpectedVersion != current) conflicts.Add(new Conflict(op.Key, current));
            }
            if (conflicts.Count > 0)
                return Task.FromResult(new CommitResult(false, b.CurrentSeq, conflicts));

            long newSeq = b.CurrentSeq + 1;
            foreach (var op in ops)
            {
                long current = b.Entries.TryGetValue(op.Key, out var e) ? e.Version : 0;
                var value = op.Delete ? Array.Empty<byte>() : op.Value;
                b.Entries[op.Key] = new BucketEntry(
                    bucketId, op.Key, current + 1, newSeq,
                    Hashing.Sha256Hex(value), op.Scheme, op.Delete, value);
            }
            b.CurrentSeq = newSeq;
            b.ContentHash = Hashing.BucketContentHash(b.Entries.Values);
            return Task.FromResult(new CommitResult(true, newSeq, Array.Empty<Conflict>()));
        }
    }

    public Task<DiffPage> DiffAsync(string bucketId, long sinceSeq, int limit, CancellationToken ct = default)
    {
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
        if (!_buckets.TryGetValue(bucketId, out var b))
            return Task.FromResult(new DiffPage(Array.Empty<BucketEntry>(), sinceSeq, false));
        lock (b.Gate)
        {
            var ordered = b.Entries.Values
                .Where(e => e.Seq > sinceSeq)
                .OrderBy(e => e.Seq).ThenBy(e => e.Key, StringComparer.Ordinal)
                .ToList();
            if (ordered.Count == 0)
                return Task.FromResult(new DiffPage(Array.Empty<BucketEntry>(), sinceSeq, false));

            var page = new List<BucketEntry>();
            long lastSeq = sinceSeq;
            int seqsTaken = 0;
            foreach (var grp in ordered.GroupBy(e => e.Seq)) // ordered => groups ascending
            {
                if (seqsTaken >= limit) break;
                page.AddRange(grp);
                lastSeq = grp.Key;
                seqsTaken++;
            }
            bool hasMore = ordered.Any(e => e.Seq > lastSeq);
            return Task.FromResult(new DiffPage(page, lastSeq, hasMore));
        }
    }
}
