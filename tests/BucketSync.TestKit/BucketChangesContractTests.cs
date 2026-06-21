using BucketSync.Core;
using Xunit;

namespace BucketSync.TestKit;

/// <summary>Contract for the time-based audit query (`ChangesSinceAsync`), run against both backends.
/// Each test instance uses a unique bucket so backends sharing storage stay isolated.</summary>
public abstract class BucketChangesContractTests
{
    protected string Bucket { get; } = $"b-changes-{Guid.NewGuid():N}";
    protected abstract Task<IBucketStore> NewStoreAsync(FakeTimeProvider time);
    private static WriteOp Put(string k, byte[] v) => new(k, 0, "cse-v1", v, false);

    [Fact]
    public async Task Changes_since_returns_only_entries_after_the_timestamp()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var s = await NewStoreAsync(time);

        await s.CommitBatchAsync(Bucket, new[] { Put("a", new byte[] { 1 }) });
        var t0 = time.GetUtcNow();
        time.Advance(TimeSpan.FromSeconds(10));
        await s.CommitBatchAsync(Bucket, new[] { Put("b", new byte[] { 2 }) });

        var all = await s.ChangesSinceAsync(Bucket, t0.AddSeconds(-1), 100);
        Assert.Equal(new[] { "a", "b" }, all.Entries.Select(e => e.Key).OrderBy(x => x).ToArray());

        var after = await s.ChangesSinceAsync(Bucket, t0, 100);          // strict > t0 -> only b
        Assert.Equal("b", Assert.Single(after.Entries).Key);
        Assert.False(after.HasMore);

        var none = await s.ChangesSinceAsync(Bucket, after.NextSince, 100); // cursor held -> empty
        Assert.Empty(none.Entries);
        Assert.False(none.HasMore);
    }

    [Fact]
    public async Task Changes_limit_counts_commits_and_never_splits_a_batch()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var s = await NewStoreAsync(time);

        await s.CommitBatchAsync(Bucket, new[] { Put("a", new byte[] { 1 }), Put("b", new byte[] { 2 }) }); // batch @ t0
        time.Advance(TimeSpan.FromSeconds(1));
        await s.CommitBatchAsync(Bucket, new[] { Put("c", new byte[] { 3 }) });                              // @ t0+1

        var start = new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero);
        var p1 = await s.ChangesSinceAsync(Bucket, start, 1); // limit 1 commit -> the whole 2-key batch
        Assert.Equal(2, p1.Entries.Count);
        Assert.All(p1.Entries, e => Assert.Equal(p1.Entries[0].UpdatedAt, e.UpdatedAt)); // one batch, one timestamp
        Assert.True(p1.HasMore);

        var p2 = await s.ChangesSinceAsync(Bucket, p1.NextSince, 1); // resume from NextSince -> c
        Assert.Equal("c", Assert.Single(p2.Entries).Key);
        Assert.False(p2.HasMore);
    }
}
