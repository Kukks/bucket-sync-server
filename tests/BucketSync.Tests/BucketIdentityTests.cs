using BucketSync.Core;

namespace BucketSync.Tests;

public class BucketIdentityTests
{
    [Fact]
    public void NewBucketId_is_64_lowercase_hex_chars()
    {
        var id = BucketIdentity.NewBucketId();
        Assert.Equal(64, id.Length);          // 32 bytes hex
        Assert.Matches("^[0-9a-f]{64}$", id);
    }

    [Fact]
    public void NewBucketId_is_unique_per_call()
        => Assert.NotEqual(BucketIdentity.NewBucketId(), BucketIdentity.NewBucketId());
}
