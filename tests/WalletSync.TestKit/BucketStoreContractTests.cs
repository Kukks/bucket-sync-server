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

    protected static WriteOp Del(string key, long expectedVersion) =>
        new(key, expectedVersion, "cse-v1", Array.Empty<byte>(), Delete: true);

    [Fact]
    public async Task Head_of_fresh_bucket_is_seq0_and_empty_hash()
    {
        var s = await NewStoreAsync();
        var head = await s.GetHeadAsync(Bucket);
        Assert.Equal(0, head.CurrentSeq);
        Assert.Equal("", head.ContentHash);
    }

    [Fact]
    public async Task Commit_to_unprovisioned_bucket_throws()
    {
        var s = await NewStoreAsync();
        // A bucket that was never provisioned (EnsureBucketAsync) is not committable —
        // both backends must reject it with a clear error, not auto-create or NRE.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => s.CommitBatchAsync("never-provisioned", new[] { Put("k", 0, new byte[] { 1 }) }));
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

    [Fact]
    public async Task Delete_creates_a_versioned_tombstone()
    {
        var s = await NewStoreAsync();
        await s.CommitBatchAsync(Bucket, new[] { Put("k", 0, new byte[] { 7 }) }); // v1

        var r = await s.CommitBatchAsync(Bucket, new[] { Del("k", 1) });
        Assert.True(r.Committed);
        Assert.Equal(2, r.NewSeq);

        var e = Assert.Single(await s.GetBatchAsync(Bucket, new[] { "k" }));
        Assert.True(e.Deleted);
        Assert.Equal(2, e.Version);
        Assert.Empty(e.Value);
    }

    [Fact]
    public async Task Tombstone_still_participates_in_cas()
    {
        var s = await NewStoreAsync();
        await s.CommitBatchAsync(Bucket, new[] { Put("k", 0, new byte[] { 7 }) }); // v1
        await s.CommitBatchAsync(Bucket, new[] { Del("k", 1) });                   // v2 tombstone

        // Re-creating the key must expect the tombstone's version (2), not 0.
        var stale = await s.CommitBatchAsync(Bucket, new[] { Put("k", 0, new byte[] { 8 }) });
        Assert.False(stale.Committed);
        Assert.Equal(2, Assert.Single(stale.Conflicts).CurrentVersion);

        var ok = await s.CommitBatchAsync(Bucket, new[] { Put("k", 2, new byte[] { 8 }) });
        Assert.True(ok.Committed);
        var e = Assert.Single(await s.GetBatchAsync(Bucket, new[] { "k" }));
        Assert.False(e.Deleted);
        Assert.Equal(3, e.Version);
    }

    [Fact]
    public async Task Diff_returns_changes_in_seq_order_since_cursor()
    {
        var s = await NewStoreAsync();
        await s.CommitBatchAsync(Bucket, new[] { Put("a", 0, new byte[] { 1 }) }); // seq 1
        await s.CommitBatchAsync(Bucket, new[] { Put("b", 0, new byte[] { 2 }) }); // seq 2

        var page = await s.DiffAsync(Bucket, sinceSeq: 0, limit: 10);
        Assert.Equal(new[] { "a", "b" }, page.Entries.Select(e => e.Key).ToArray());
        Assert.Equal(2, page.NextSeq);
        Assert.False(page.HasMore);

        var tail = await s.DiffAsync(Bucket, sinceSeq: 1, limit: 10);
        Assert.Equal("b", Assert.Single(tail.Entries).Key);
        Assert.Equal(2, tail.NextSeq);
    }

    [Fact]
    public async Task Diff_limit_counts_commits_and_never_splits_a_batch()
    {
        var s = await NewStoreAsync();
        // seq 1 = a 3-key batch; seq 2 = a 1-key batch.
        await s.CommitBatchAsync(Bucket, new[] { Put("a", 0, new byte[] { 1 }), Put("b", 0, new byte[] { 2 }), Put("c", 0, new byte[] { 3 }) });
        await s.CommitBatchAsync(Bucket, new[] { Put("d", 0, new byte[] { 4 }) });

        // limit: 1 commit -> must return ALL of seq 1 (3 entries), not 1 entry.
        var page = await s.DiffAsync(Bucket, sinceSeq: 0, limit: 1);
        Assert.Equal(3, page.Entries.Count);
        Assert.All(page.Entries, e => Assert.Equal(1, e.Seq));
        Assert.Equal(1, page.NextSeq);
        Assert.True(page.HasMore);

        // resume from NextSeq -> the second commit.
        var page2 = await s.DiffAsync(Bucket, sinceSeq: page.NextSeq, limit: 1);
        Assert.Equal("d", Assert.Single(page2.Entries).Key);
        Assert.Equal(2, page2.NextSeq);
        Assert.False(page2.HasMore);
    }

    [Fact]
    public async Task Diff_with_no_changes_returns_empty_and_holds_cursor()
    {
        var s = await NewStoreAsync();
        await s.CommitBatchAsync(Bucket, new[] { Put("a", 0, new byte[] { 1 }) });
        var page = await s.DiffAsync(Bucket, sinceSeq: 1, limit: 10);
        Assert.Empty(page.Entries);
        Assert.Equal(1, page.NextSeq);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task Same_commits_yield_same_content_hash_across_instances()
    {
        var s1 = await NewStoreAsync();
        var s2 = await NewStoreAsync();
        foreach (var s in new[] { s1, s2 })
        {
            await s.CommitBatchAsync(Bucket, new[] { Put("a", 0, new byte[] { 1 }), Put("b", 0, new byte[] { 2 }) });
            await s.CommitBatchAsync(Bucket, new[] { Put("a", 1, new byte[] { 9 }) });
        }
        var h1 = (await s1.GetHeadAsync(Bucket)).ContentHash;
        var h2 = (await s2.GetHeadAsync(Bucket)).ContentHash;
        Assert.Equal(h1, h2);
        Assert.NotEqual("", h1);
    }

    [Fact]
    public async Task Different_content_yields_different_hash()
    {
        var s1 = await NewStoreAsync();
        var s2 = await NewStoreAsync();
        await s1.CommitBatchAsync(Bucket, new[] { Put("k", 0, new byte[] { 1 }) });
        await s2.CommitBatchAsync(Bucket, new[] { Put("k", 0, new byte[] { 2 }) });
        Assert.NotEqual((await s1.GetHeadAsync(Bucket)).ContentHash, (await s2.GetHeadAsync(Bucket)).ContentHash);
    }

    [Fact]
    public async Task GetBatch_returns_existing_including_tombstones_and_omits_missing()
    {
        var s = await NewStoreAsync();
        await s.CommitBatchAsync(Bucket, new[] { Put("a", 0, new byte[] { 1 }), Put("b", 0, new byte[] { 2 }) });
        await s.CommitBatchAsync(Bucket, new[] { Del("b", 1) }); // b -> tombstone

        var got = await s.GetBatchAsync(Bucket, new[] { "a", "b", "missing" });
        Assert.Equal(2, got.Count);
        Assert.Contains(got, e => e.Key == "a" && !e.Deleted);
        Assert.Contains(got, e => e.Key == "b" && e.Deleted);
        Assert.DoesNotContain(got, e => e.Key == "missing");
    }

    [Fact]
    public async Task GetBatch_on_empty_key_list_returns_empty()
    {
        var s = await NewStoreAsync();
        Assert.Empty(await s.GetBatchAsync(Bucket, Array.Empty<string>()));
    }

    [Fact]
    public async Task GetBatch_on_unknown_bucket_returns_empty()
    {
        var s = await NewStoreAsync();
        Assert.Empty(await s.GetBatchAsync("no-such-bucket", new[] { "k" }));
    }
}
