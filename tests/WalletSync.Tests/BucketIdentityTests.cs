using WalletSync.Core;

namespace WalletSync.Tests;

public class BucketIdentityTests
{
    [Fact]
    public void Derive_is_deterministic_and_independent_of_hex_case()
    {
        const string pk = "18845781f631c48f1c9709e23092067d06837f30aa0cd0544ac887fe91ddd166";
        Assert.Equal(BucketIdentity.Derive(pk), BucketIdentity.Derive(pk.ToUpperInvariant()));
        Assert.Equal(64, BucketIdentity.Derive(pk).Length); // 32-byte hash, hex
    }

    [Fact]
    public void Derive_differs_for_different_pubkeys()
    {
        var a = BucketIdentity.Derive("00".PadRight(64, '0'));
        var b = BucketIdentity.Derive("01".PadRight(64, '0'));
        Assert.NotEqual(a, b);
    }
}
