using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;

namespace BucketSync.TestKit;

/// <summary>
/// A software WebAuthn authenticator (ES256 / P-256) for tests. Crafts real create/get-ceremony
/// artifacts (clientDataJSON, attestationObject, authenticatorData, DER signature) so the server's
/// hand-rolled verification is exercised against genuine WebAuthn structures.
/// </summary>
public sealed class TestPasskey
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _rpId;
    private readonly string _origin;

    public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(16);
    public string CredentialIdB64Url => B64Url(CredentialId);

    public TestPasskey(string rpId = "localhost", string origin = "http://localhost")
    {
        _rpId = rpId;
        _origin = origin;
    }

    /// <summary>create ceremony → (clientDataJSON, attestationObject).</summary>
    public (byte[] ClientDataJson, byte[] AttestationObject) Register(string nonceHex)
    {
        var clientData = ClientDataJson("webauthn.create", nonceHex);
        var authData = AuthData(attested: true);
        var w = new CborWriter();
        w.WriteStartMap(3);
        w.WriteTextString("fmt"); w.WriteTextString("none");
        w.WriteTextString("attStmt"); w.WriteStartMap(0); w.WriteEndMap();
        w.WriteTextString("authData"); w.WriteByteString(authData);
        w.WriteEndMap();
        return (clientData, w.Encode());
    }

    /// <summary>get ceremony → (clientDataJSON, authenticatorData, DER signature).</summary>
    public (byte[] ClientDataJson, byte[] AuthenticatorData, byte[] Signature) Assert(string nonceHex)
    {
        var clientData = ClientDataJson("webauthn.get", nonceHex);
        var authData = AuthData(attested: false);
        var signed = Concat(authData, SHA256.HashData(clientData));
        var sig = _key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return (clientData, authData, sig);
    }

    private byte[] ClientDataJson(string type, string nonceHex)
    {
        var challenge = B64Url(Convert.FromHexString(nonceHex));
        return Encoding.UTF8.GetBytes($"{{\"type\":\"{type}\",\"challenge\":\"{challenge}\",\"origin\":\"{_origin}\"}}");
    }

    private byte[] AuthData(bool attested)
    {
        var rpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(_rpId));
        if (!attested)
        {
            var ad = new byte[37];
            rpIdHash.CopyTo(ad, 0);
            ad[32] = 0x01; // UP
            return ad;     // signCount 0
        }
        var cose = CoseKey();
        var data = new byte[32 + 1 + 4 + 16 + 2 + CredentialId.Length + cose.Length];
        int p = 0;
        rpIdHash.CopyTo(data, p); p += 32;
        data[p++] = 0x41;     // UP | AT
        p += 4;               // signCount 0
        p += 16;              // aaguid 0
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(p, 2), (ushort)CredentialId.Length); p += 2;
        CredentialId.CopyTo(data, p); p += CredentialId.Length;
        cose.CopyTo(data, p);
        return data;
    }

    private byte[] CoseKey()
    {
        var p = _key.ExportParameters(false);
        var w = new CborWriter();
        w.WriteStartMap(5);
        w.WriteInt32(1); w.WriteInt32(2);     // kty: EC2
        w.WriteInt32(3); w.WriteInt32(-7);    // alg: ES256
        w.WriteInt32(-1); w.WriteInt32(1);    // crv: P-256
        w.WriteInt32(-2); w.WriteByteString(p.Q.X!);  // x
        w.WriteInt32(-3); w.WriteByteString(p.Q.Y!);  // y
        w.WriteEndMap();
        return w.Encode();
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        a.CopyTo(r, 0); b.CopyTo(r, a.Length);
        return r;
    }

    private static string B64Url(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
