namespace BucketSync.Auth;

/// <summary>Unpadded base64url (RFC 4648 §5) — how WebAuthn/JSON carry binary fields.</summary>
public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[]? Decode(string s)
    {
        try
        {
            var t = s.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(t.PadRight(t.Length + (4 - t.Length % 4) % 4, '='));
        }
        catch (FormatException) { return null; }
    }
}
