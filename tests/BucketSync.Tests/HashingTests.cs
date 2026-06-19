using BucketSync.Core;

namespace BucketSync.Tests;

public class HashingTests
{
    [Fact]
    public void Sha256Hex_is_lowercase_hex_of_sha256()
    {
        // SHA-256("") well-known vector
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            Hashing.Sha256Hex(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void BucketContentHash_of_empty_set_is_empty_string()
        => Assert.Equal("", Hashing.BucketContentHash(Array.Empty<BucketEntry>()));

    [Fact]
    public void BucketContentHash_is_order_independent_over_keys()
    {
        var a = new BucketEntry("b", "k1", 1, 1, "h1", "cse-v1", false, new byte[] { 1 });
        var c = new BucketEntry("b", "k2", 1, 1, "h2", "cse-v1", false, new byte[] { 2 });
        Assert.Equal(
            Hashing.BucketContentHash(new[] { a, c }),
            Hashing.BucketContentHash(new[] { c, a }));
    }

    [Fact]
    public void BucketContentHash_changes_when_a_version_changes()
    {
        var v1 = new BucketEntry("b", "k", 1, 1, "h", "cse-v1", false, new byte[] { 1 });
        var v2 = v1 with { Version = 2 };
        Assert.NotEqual(Hashing.BucketContentHash(new[] { v1 }), Hashing.BucketContentHash(new[] { v2 }));
    }

    [Fact]
    public void BucketContentHash_changes_when_deleted_flag_changes()
    {
        var live = new BucketEntry("b", "k", 1, 1, "h", "cse-v1", false, new byte[] { 1 });
        var dead = live with { Deleted = true };
        Assert.NotEqual(Hashing.BucketContentHash(new[] { live }), Hashing.BucketContentHash(new[] { dead }));
    }
}
