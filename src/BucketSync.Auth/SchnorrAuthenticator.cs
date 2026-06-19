using NBitcoin.Secp256k1;
using BucketSync.Core;

namespace BucketSync.Auth;

public sealed class SchnorrAuthenticator : IAuthenticator
{
    public Task<Principal?> VerifyAsync(Challenge challenge, byte[] signature, CancellationToken ct = default)
    {
        Principal? result = null;
        try
        {
            var pub = Convert.FromHexString(challenge.Pubkey);
            if (pub.Length == 32 && signature.Length == 64
                && SecpSchnorrSignature.TryCreate(signature, out var sig))
            {
                var xonly = ECXOnlyPubKey.Create(pub);                 // throws on invalid point
                var msg = AuthChallengeMessage.Compute(challenge.Nonce);
                if (xonly.SigVerifyBIP340(sig, msg))
                    result = new Principal(challenge.Pubkey, BucketIdentity.Derive(challenge.Pubkey));
            }
        }
        catch (FormatException) { /* bad hex */ }
        catch (ArgumentException) { /* bad pubkey bytes */ }
        return Task.FromResult(result);
    }
}
