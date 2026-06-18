using WalletSync.Core;
using Xunit;

namespace WalletSync.TestKit;

public abstract class BucketStoreContractTests
{
    protected const string Bucket = "b-test";
    protected const string Pubkey = "deadbeef";

    /// <summary>Return a fresh, empty store whose bucket "b-test" is already provisioned.</summary>
    protected abstract Task<IBucketStore> NewStoreAsync();

    protected static WriteOp Put(string key, long expectedVersion, byte[] value) =>
        new(key, expectedVersion, "cse-v1", value, Delete: false);

    [Fact]
    public async Task Head_of_fresh_bucket_is_seq0_and_empty_hash()
    {
        var s = await NewStoreAsync();
        var head = await s.GetHeadAsync(Bucket);
        Assert.Equal(0, head.CurrentSeq);
        Assert.Equal("", head.ContentHash);
    }

    [Fact]
    public async Task First_create_yields_version1_seq1_and_is_readable()
    {
        var s = await NewStoreAsync();
        var r = await s.CommitBatchAsync(Bucket, new[] { Put("vtxo:a", 0, new byte[] { 1, 2, 3 }) });
        Assert.True(r.Committed);
        Assert.Equal(1, r.NewSeq);
        Assert.Empty(r.Conflicts);

        var entries = await s.GetBatchAsync(Bucket, new[] { "vtxo:a" });
        var e = Assert.Single(entries);
        Assert.Equal(1, e.Version);
        Assert.Equal(1, e.Seq);
        Assert.False(e.Deleted);
        Assert.Equal(new byte[] { 1, 2, 3 }, e.Value);
        Assert.Equal(Hashing.Sha256Hex(new byte[] { 1, 2, 3 }), e.ContentHash);

        var head = await s.GetHeadAsync(Bucket);
        Assert.Equal(1, head.CurrentSeq);
    }

    [Fact]
    public async Task Update_with_correct_expected_version_bumps_version_and_seq()
    {
        var s = await NewStoreAsync();
        await s.CommitBatchAsync(Bucket, new[] { Put("k", 0, new byte[] { 1 }) });
        var r = await s.CommitBatchAsync(Bucket, new[] { Put("k", 1, new byte[] { 2 }) });
        Assert.True(r.Committed);
        Assert.Equal(2, r.NewSeq);

        var e = Assert.Single(await s.GetBatchAsync(Bucket, new[] { "k" }));
        Assert.Equal(2, e.Version);
        Assert.Equal(2, e.Seq);
        Assert.Equal(new byte[] { 2 }, e.Value);
    }

    [Fact]
    public async Task Stale_expected_version_is_rejected_and_writes_nothing()
    {
        var s = await NewStoreAsync();
        await s.CommitBatchAsync(Bucket, new[] { Put("k", 0, new byte[] { 1 }) }); // version 1

        var r = await s.CommitBatchAsync(Bucket, new[] { Put("k", 0, new byte[] { 9 }) }); // stale: expects 0
        Assert.False(r.Committed);
        var c = Assert.Single(r.Conflicts);
        Assert.Equal("k", c.Key);
        Assert.Equal(1, c.CurrentVersion);

        var e = Assert.Single(await s.GetBatchAsync(Bucket, new[] { "k" }));
        Assert.Equal(1, e.Version);                  // unchanged
        Assert.Equal(new byte[] { 1 }, e.Value);     // original value intact
        Assert.Equal(1, (await s.GetHeadAsync(Bucket)).CurrentSeq); // seq did not advance
    }

    [Fact]
    public async Task Batch_is_all_or_nothing_when_one_op_conflicts()
    {
        var s = await NewStoreAsync();
        await s.CommitBatchAsync(Bucket, new[] { Put("a", 0, new byte[] { 1 }) }); // a -> v1

        // batch: a expects 0 (stale, real is 1) + b expects 0 (fresh). Whole batch must be rejected.
        var r = await s.CommitBatchAsync(Bucket, new[]
        {
            Put("a", 0, new byte[] { 2 }),
            Put("b", 0, new byte[] { 3 }),
        });
        Assert.False(r.Committed);
        Assert.Equal("a", Assert.Single(r.Conflicts).Key);

        Assert.Empty(await s.GetBatchAsync(Bucket, new[] { "b" })); // b was NOT created
        var a = Assert.Single(await s.GetBatchAsync(Bucket, new[] { "a" }));
        Assert.Equal(1, a.Version);                                 // a unchanged
    }

    [Fact]
    public async Task Multi_op_batch_commits_atomically_at_one_seq()
    {
        var s = await NewStoreAsync();
        var r = await s.CommitBatchAsync(Bucket, new[]
        {
            Put("a", 0, new byte[] { 1 }),
            Put("b", 0, new byte[] { 2 }),
            Put("c", 0, new byte[] { 3 }),
        });
        Assert.True(r.Committed);
        Assert.Equal(1, r.NewSeq);

        var entries = await s.GetBatchAsync(Bucket, new[] { "a", "b", "c" });
        Assert.Equal(3, entries.Count);
        Assert.All(entries, e => Assert.Equal(1, e.Seq)); // all share the one commit's seq
    }

    [Fact]
    public async Task Duplicate_key_in_one_batch_throws()
    {
        var s = await NewStoreAsync();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            s.CommitBatchAsync(Bucket, new[]
            {
                Put("k", 0, new byte[] { 1 }),
                Put("k", 0, new byte[] { 2 }),
            }));
    }

    [Fact]
    public async Task All_conflicting_keys_are_reported_not_just_the_first()
    {
        var s = await NewStoreAsync();
        await s.CommitBatchAsync(Bucket, new[] { Put("a", 0, new byte[] { 1 }) });
        await s.CommitBatchAsync(Bucket, new[] { Put("b", 0, new byte[] { 2 }) });

        // both a and b are now at version 1, so expecting 0 is stale for both
        var r = await s.CommitBatchAsync(Bucket, new[]
        {
            Put("a", 0, new byte[] { 9 }),
            Put("b", 0, new byte[] { 9 }),
        });
        Assert.False(r.Committed);
        Assert.Equal(2, r.Conflicts.Count);
        Assert.Contains(r.Conflicts, c => c.Key == "a");
        Assert.Contains(r.Conflicts, c => c.Key == "b");
    }
}
