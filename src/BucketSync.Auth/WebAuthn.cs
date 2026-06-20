using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BucketSync.Auth;

/// <summary>
/// Minimal, real WebAuthn verification — ES256 (P-256) only, no attestation. Enough to register a
/// credential (learn its public key) and verify later assertions. Hand-rolled to stay AOT-clean
/// (System.Security.Cryptography + System.Formats.Cbor + JsonDocument; no reflection, no Fido2NetLib).
/// </summary>
internal static class WebAuthn
{
    public const byte FlagUserPresent = 0x01;
    public const byte FlagAttestedCredentialData = 0x40;

    // ---- base64url (shared impl) ----
    public static string B64Url(ReadOnlySpan<byte> b) => Base64Url.Encode(b);
    public static byte[]? FromB64Url(string s) => Base64Url.Decode(s);

    // ---- clientDataJSON ----
    public sealed record ClientData(string Type, string Challenge, string Origin);

    public static ClientData? ParseClientData(byte[] clientDataJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(clientDataJson);
            var root = doc.RootElement;
            return new ClientData(
                root.GetProperty("type").GetString() ?? "",
                root.GetProperty("challenge").GetString() ?? "",
                root.GetProperty("origin").GetString() ?? "");
        }
        catch (JsonException) { return null; }
        catch (KeyNotFoundException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    // ---- registration: attestationObject (CBOR { fmt, attStmt, authData }) -> (credentialId, COSE key) ----
    // attStmt is NOT verified (no attestation).
    public static (byte[] CredentialId, byte[] CoseKey)? ParseRegistration(byte[] attestationObject)
    {
        try
        {
            var reader = new CborReader(attestationObject);
            int n = reader.ReadStartMap() ?? -1;
            byte[]? authData = null;
            for (int i = 0; i < n; i++)
            {
                var key = reader.ReadTextString();
                if (key == "authData") authData = reader.ReadByteString();
                else reader.SkipValue();
            }
            reader.ReadEndMap();
            return authData is null ? null : ParseAttestedCredential(authData);
        }
        catch (CborContentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    // authData: rpIdHash(32) | flags(1) | signCount(4) | aaguid(16) | credIdLen(2) | credId | COSE key
    private static (byte[] CredentialId, byte[] CoseKey)? ParseAttestedCredential(byte[] authData)
    {
        if (authData.Length < 55 || (authData[32] & FlagAttestedCredentialData) == 0) return null;
        int pos = 37 + 16; // rpIdHash+flags+signCount, then aaguid
        int credIdLen = BinaryPrimitives.ReadUInt16BigEndian(authData.AsSpan(pos, 2));
        pos += 2;
        if (pos + credIdLen > authData.Length) return null;
        var credId = authData.AsSpan(pos, credIdLen).ToArray();
        var coseKey = authData.AsSpan(pos + credIdLen).ToArray(); // remainder = COSE public key (CBOR)
        return (credId, coseKey);
    }

    // ---- assertion authenticatorData: rpIdHash(32) | flags(1) | signCount(4) ----
    public static (byte[] RpIdHash, byte Flags)? ParseAuthData(byte[] authData) =>
        authData.Length < 37 ? null : (authData.AsSpan(0, 32).ToArray(), authData[32]);

    // ---- COSE ES256 key -> ECDsa (public). null unless EC2 / ES256 / P-256 with x,y present ----
    public static ECDsa? ImportEs256(byte[] coseKey)
    {
        try
        {
            var reader = new CborReader(coseKey);
            int n = reader.ReadStartMap() ?? -1;
            byte[]? x = null, y = null;
            int kty = 0, alg = 0, crv = 0;
            for (int i = 0; i < n; i++)
            {
                int label = reader.ReadInt32();
                switch (label)
                {
                    case 1: kty = reader.ReadInt32(); break;
                    case 3: alg = reader.ReadInt32(); break;
                    case -1: crv = reader.ReadInt32(); break;
                    case -2: x = reader.ReadByteString(); break;
                    case -3: y = reader.ReadByteString(); break;
                    default: reader.SkipValue(); break;
                }
            }
            reader.ReadEndMap();
            if (kty != 2 || alg != -7 || crv != 1 || x is null || y is null) return null;
            return ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = x, Y = y },
            });
        }
        catch (CborContentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (CryptographicException) { return null; }
    }

    // ---- ES256 assertion: DER signature over (authData || SHA-256(clientDataJSON)) ----
    public static bool VerifyEs256(ECDsa key, byte[] authData, byte[] clientDataJson, byte[] derSignature)
    {
        var clientHash = SHA256.HashData(clientDataJson);
        var signed = new byte[authData.Length + clientHash.Length];
        authData.CopyTo(signed, 0);
        clientHash.CopyTo(signed, authData.Length);
        try { return key.VerifyData(signed, derSignature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence); }
        catch (CryptographicException) { return false; }
    }

    public static byte[] RpIdHash(string rpId) => SHA256.HashData(Encoding.UTF8.GetBytes(rpId));
}
