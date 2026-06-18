using WalletSync.Core;

namespace WalletSync.Tests;

public class InProcChangeNotifierTests
{
    [Fact]
    public async Task Subscriber_receives_published_seq()
    {
        var n = new InProcChangeNotifier();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var e = n.Subscribe("b", cts.Token).GetAsyncEnumerator(cts.Token);

        var move = e.MoveNextAsync();
        await n.PublishAsync("b", 7);
        Assert.True(await move);
        Assert.Equal(7, e.Current);
    }

    [Fact]
    public async Task Two_subscribers_both_receive()
    {
        var n = new InProcChangeNotifier();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var e1 = n.Subscribe("b", cts.Token).GetAsyncEnumerator(cts.Token);
        await using var e2 = n.Subscribe("b", cts.Token).GetAsyncEnumerator(cts.Token);

        var m1 = e1.MoveNextAsync();
        var m2 = e2.MoveNextAsync();
        await n.PublishAsync("b", 3);
        Assert.True(await m1);
        Assert.True(await m2);
        Assert.Equal(3, e1.Current);
        Assert.Equal(3, e2.Current);
    }

    [Fact]
    public async Task Publish_to_a_bucket_with_no_subscribers_is_a_noop()
    {
        var n = new InProcChangeNotifier();
        await n.PublishAsync("nobody", 1); // must not throw
    }

    [Fact]
    public async Task Other_buckets_do_not_receive()
    {
        var n = new InProcChangeNotifier();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await using var e = n.Subscribe("b1", cts.Token).GetAsyncEnumerator(cts.Token);
        var move = e.MoveNextAsync();
        await n.PublishAsync("b2", 9); // different bucket
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await move);
    }
}
