using System.Runtime.CompilerServices;

namespace WalletSync.Core;

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

    // StreamAsync added in Task 14.
}
