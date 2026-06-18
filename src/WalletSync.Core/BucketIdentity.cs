namespace WalletSync.Core;

public static class BucketIdentity
{
    /// <summary>bucketId = lowercase-hex(SHA-256(xOnlyPubKeyBytes)). Derived server-side; never client-supplied.</summary>
    public static string Derive(string pubkeyHex) =>
        Hashing.Sha256Hex(Convert.FromHexString(pubkeyHex));
}
