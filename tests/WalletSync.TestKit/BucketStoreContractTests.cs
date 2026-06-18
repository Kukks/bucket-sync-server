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
}
