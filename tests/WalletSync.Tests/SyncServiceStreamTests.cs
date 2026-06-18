using WalletSync.Core;

namespace WalletSync.Tests;

public class SyncServiceStreamTests
{
    private static WriteOp Put(string k, long v, byte[] val) => new(k, v, "cse-v1", val, false);

    private static async Task<SyncService> NewAsync(InMemoryBucketStore store, InProcChangeNotifier notifier)
    {
        await store.EnsureBucketAsync("b", "pk");
        return new SyncService(store, notifier);
    }

    [Fact]
    public async Task Behind_client_gets_catchup_seq_immediately()
    {
        var store = new InMemoryBucketStore();
        var notifier = new InProcChangeNotifier();
        var sync = await NewAsync(store, notifier);
        await store.CommitBatchAsync("b", new[] { Put("a", 0, new byte[] { 1 }) }); // seq 1, client cursor 0

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var e = sync.StreamAsync("b", lastEventId: 0, cts.Token).GetAsyncEnumerator(cts.Token);
        Assert.True(await e.MoveNextAsync());
        Assert.Equal(1, e.Current); // catch-up signal
    }

    [Fact]
    public async Task Caught_up_client_then_receives_live_commit()
    {
        var store = new InMemoryBucketStore();
        var notifier = new InProcChangeNotifier();
        var sync = await NewAsync(store, notifier);
        await store.CommitBatchAsync("b", new[] { Put("a", 0, new byte[] { 1 }) }); // seq 1

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        // client is already at seq 1 -> no catch-up; first event must be the live seq 2.
        await using var e = sync.StreamAsync("b", lastEventId: 1, cts.Token).GetAsyncEnumerator(cts.Token);
        var move = e.MoveNextAsync();

        await Task.Yield();
        // a live commit, published via the SyncService used by the API; here drive store + notifier directly.
        await store.CommitBatchAsync("b", new[] { Put("b", 0, new byte[] { 2 }) }); // seq 2
        await notifier.PublishAsync("b", 2);

        Assert.True(await move);
        Assert.Equal(2, e.Current);
    }

}
