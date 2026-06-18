using WalletSync.Core;

namespace WalletSync.Tests;

public class SyncServiceCommitTests
{
    private sealed class RecordingNotifier : IChangeNotifier
    {
        public readonly List<(string Bucket, long Seq)> Published = new();
        public Task PublishAsync(string bucketId, long seq, CancellationToken ct = default)
        { Published.Add((bucketId, seq)); return Task.CompletedTask; }
        public IAsyncEnumerable<long> Subscribe(string bucketId, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private static WriteOp Put(string k, long v, byte[] val) => new(k, v, "cse-v1", val, false);

    [Fact]
    public async Task Successful_commit_publishes_new_seq_once()
    {
        var store = new InMemoryBucketStore();
        await store.EnsureBucketAsync("b", "pk");
        var notifier = new RecordingNotifier();
        var sync = new SyncService(store, notifier);

        var r = await sync.CommitAsync("b", new[] { Put("k", 0, new byte[] { 1 }) });
        Assert.True(r.Committed);
        Assert.Equal(("b", 1L), Assert.Single(notifier.Published));
    }

    [Fact]
    public async Task Conflicted_commit_publishes_nothing()
    {
        var store = new InMemoryBucketStore();
        await store.EnsureBucketAsync("b", "pk");
        await store.CommitBatchAsync("b", new[] { Put("k", 0, new byte[] { 1 }) }); // k -> v1
        var notifier = new RecordingNotifier();
        var sync = new SyncService(store, notifier);

        var r = await sync.CommitAsync("b", new[] { Put("k", 0, new byte[] { 2 }) }); // stale
        Assert.False(r.Committed);
        Assert.Empty(notifier.Published);
    }
}
