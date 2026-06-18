using System.Security.Cryptography;
using System.Text;

namespace WalletSync.Core;

public static class Hashing
{
    public static string Sha256Hex(ReadOnlySpan<byte> data) =>
        Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>
    /// Deterministic digest over the live entry set: entries sorted by Key (ordinal),
    /// each serialized as "{key}\n{version}\n{contentHash}\n{deleted?1:0}\n", UTF-8, SHA-256.
    /// Empty set => "" (matches the SQL default and a never-written bucket head).
    /// Tombstones are included so two devices that applied the same deletes agree.
    /// </summary>
    public static string BucketContentHash(IEnumerable<BucketEntry> entries)
    {
        var ordered = entries.OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
        if (ordered.Count == 0) return "";
        var sb = new StringBuilder();
        foreach (var e in ordered)
            sb.Append(e.Key).Append('\n')
              .Append(e.Version).Append('\n')
              .Append(e.ContentHash).Append('\n')
              .Append(e.Deleted ? '1' : '0').Append('\n');
        return Sha256Hex(Encoding.UTF8.GetBytes(sb.ToString()));
    }
}
