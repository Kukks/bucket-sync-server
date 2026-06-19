using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace BucketSync.Core;

public sealed class InProcChangeNotifier : IChangeNotifier
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<long>>> _subs =
        new(StringComparer.Ordinal);

    public Task PublishAsync(string bucketId, long seq, CancellationToken ct = default)
    {
        if (_subs.TryGetValue(bucketId, out var set))
            foreach (var ch in set.Values)
                ch.Writer.TryWrite(seq); // bounded(1, DropOldest) => always succeeds, keeps newest
        return Task.CompletedTask;
    }

    public IAsyncEnumerable<long> Subscribe(string bucketId, CancellationToken ct)
    {
        // Register the channel EAGERLY (synchronously) at call time — NOT lazily on first
        // MoveNextAsync. Callers (SyncService.StreamAsync) rely on subscribe-before-head: a
        // publish landing between this call and the first read must be captured by the channel.
        var ch = Channel.CreateBounded<long>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        var id = Guid.NewGuid();
        var set = _subs.GetOrAdd(bucketId, _ => new ConcurrentDictionary<Guid, Channel<long>>());
        set[id] = ch;
        return Read(ch, set, id, ct);

        static async IAsyncEnumerable<long> Read(
            Channel<long> ch, ConcurrentDictionary<Guid, Channel<long>> set, Guid id,
            [EnumeratorCancellation] CancellationToken ct)
        {
            try
            {
                await foreach (var seq in ch.Reader.ReadAllAsync(ct))
                    yield return seq;
            }
            finally
            {
                // Remove only this subscriber. We deliberately do NOT prune the now-possibly-empty
                // per-bucket dict: a check-then-remove on _subs would race a concurrently-arriving
                // subscriber (GetOrAdd sees the empty set, then we delete it) and orphan its channel.
                // Leaving an empty inner dict is a negligible memory cost.
                set.TryRemove(id, out _);
            }
        }
    }
}
