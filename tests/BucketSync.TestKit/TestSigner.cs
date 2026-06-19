using NBitcoin.Secp256k1;
using BucketSync.Auth;

namespace BucketSync.TestKit;

public static class TestSigner
{
    /// <summary>Derives an x-only pubkey from seed and produces a BIP-340 signature over the auth message.</summary>
    public static (string PubkeyHex, byte[] Signature) Sign(byte[] seed32, string nonceHex)
    {
        var priv = ECPrivKey.Create(seed32);
        var xonly = priv.CreateXOnlyPubKey();
        var msg = AuthChallengeMessage.Compute(nonceHex);
        var sig = priv.SignBIP340(msg);
        return (Convert.ToHexStringLower(xonly.ToBytes()), sig.ToBytes());
    }
}
