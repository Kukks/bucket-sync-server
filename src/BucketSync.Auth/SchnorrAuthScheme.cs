using NBitcoin.Secp256k1;
using BucketSync.Core;

namespace BucketSync.Auth;

/// <summary>
/// secp256k1 / BIP-340 Schnorr auth scheme. The credential IS the x-only pubkey, proven by a
/// signature over SHA-256("bucket-sync:auth:v1" ‖ nonce). No separate registration ceremony.
/// </summary>
public sealed class SchnorrAuthScheme : IAuthScheme
{
    public string Scheme => "schnorr";

    /// <summary>Verify the proof against a consumed challenge; return the credential or null if invalid.</summary>
    public VerifiedCredential? Verify(string pubkeyHex, Challenge challenge, byte[] signature)
    {
        try
        {
            var pub = Convert.FromHexString(pubkeyHex);
            if (pub.Length != 32 || signature.Length != 64) return null;
            if (!SecpSchnorrSignature.TryCreate(signature, out var sig)) return null;
            var xonly = ECXOnlyPubKey.Create(pub);                 // throws on invalid point
            var msg = AuthChallengeMessage.Compute(challenge.Nonce);
            if (!xonly.SigVerifyBIP340(sig, msg)) return null;
            return new VerifiedCredential("schnorr", Convert.ToHexStringLower(pub), pub, null);
        }
        catch (FormatException) { return null; }   // bad hex
        catch (ArgumentException) { return null; }  // bad pubkey bytes
    }
}
