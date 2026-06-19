using System.Runtime.CompilerServices;

namespace BucketSync.Core;

public sealed class SyncService
{
    private readonly IBucketStore _store;
    private readonly IChangeNotifier _notifier;

    public SyncService(IBucketStore store, IChangeNotifier notifier)
    {
        _store = store;
        _notifier = notifier;
    }

    public async Task<CommitResult> CommitAsync(string bucketId, IReadOnlyList<WriteOp> ops, CancellationToken ct = default)
    {
        var result = await _store.CommitBatchAsync(bucketId, ops, ct);
        if (result.Committed)
            await _notifier.PublishAsync(bucketId, result.NewSeq, ct);
        return result;
    }

    public async IAsyncEnumerable<long> StreamAsync(
        string bucketId, long lastEventId, [EnumeratorCancellation] CancellationToken ct)
    {
        // Subscribe BEFORE reading head so a commit in the gap is captured by the live channel.
        await using var live = _notifier.Subscribe(bucketId, ct).GetAsyncEnumerator(ct);

        long lastEmitted = lastEventId;
        var head = await _store.GetHeadAsync(bucketId, ct);
        if (head.CurrentSeq > lastEmitted)
        {
            lastEmitted = head.CurrentSeq;
            yield return head.CurrentSeq;
        }

        while (await live.MoveNextAsync())
        {
            var seq = live.Current;
            if (seq > lastEmitted)
            {
                lastEmitted = seq;
                yield return seq;
            }
        }
    }
}
