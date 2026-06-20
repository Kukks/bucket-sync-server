using System.Security.Cryptography;

namespace BucketSync.Core;

public static class BucketIdentity
{
    /// <summary>
    /// A fresh, opaque bucket id (256-bit, lowercase hex). Minted when any credential creates a bucket;
    /// the credential registry maps credentials to it. Never client-supplied. Identity is no longer
    /// derived from a single key — a bucket is reachable by a set of credentials of any scheme.
    /// </summary>
    public static string NewBucketId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
}
