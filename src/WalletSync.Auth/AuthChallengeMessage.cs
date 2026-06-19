using System.Security.Cryptography;
using System.Text;

namespace WalletSync.Auth;

public static class AuthChallengeMessage
{
    private static readonly byte[] Tag = Encoding.UTF8.GetBytes("bucket-sync:auth:v1");

    /// <summary>BIP-340 message: SHA-256( tag || nonceBytes ), 32 bytes. A client signs exactly this.</summary>
    public static byte[] Compute(string nonceHex)
    {
        var nonce = Convert.FromHexString(nonceHex);
        if (nonce.Length != 32) throw new ArgumentException("nonce must be 32 bytes", nameof(nonceHex));
        var buf = new byte[Tag.Length + nonce.Length];
        Tag.CopyTo(buf, 0);
        nonce.CopyTo(buf, Tag.Length);
        return SHA256.HashData(buf);
    }
}
