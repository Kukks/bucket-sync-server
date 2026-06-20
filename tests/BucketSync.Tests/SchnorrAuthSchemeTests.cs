using System.Security.Cryptography;
using BucketSync.Auth;
using BucketSync.Core;
using BucketSync.TestKit;

namespace BucketSync.Tests;

public class SchnorrAuthSchemeTests
{
    private static Challenge Issue() =>
        new(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), "schnorr", DateTimeOffset.UtcNow.AddMinutes(5));

    [Fact]
    public void Valid_signature_yields_credential()
    {
        var seed = RandomNumberGenerator.GetBytes(32);
        var (pubkey, _) = TestSigner.Sign(seed, new string('0', 64)); // get pubkey
        var challenge = Issue();
        var (_, sig) = TestSigner.Sign(seed, challenge.Nonce);

        var cred = new SchnorrAuthScheme().Verify(pubkey, challenge, sig);
        Assert.NotNull(cred);
        Assert.Equal("schnorr", cred!.Scheme);
        Assert.Equal(pubkey, cred.CredentialId);                 // credentialId IS the x-only pubkey
        Assert.Equal(Convert.FromHexString(pubkey), cred.PublicKey);
    }

    [Fact]
    public void Signature_over_a_different_nonce_is_rejected()
    {
        var seed = RandomNumberGenerator.GetBytes(32);
        var (pubkey, _) = TestSigner.Sign(seed, new string('0', 64));
        var (_, wrongSig) = TestSigner.Sign(seed, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
        Assert.Null(new SchnorrAuthScheme().Verify(pubkey, Issue(), wrongSig));
    }

    [Fact]
    public void Signature_from_a_different_key_is_rejected()
    {
        var (pubkey, _) = TestSigner.Sign(RandomNumberGenerator.GetBytes(32), new string('0', 64));
        var challenge = Issue();
        var (_, otherSig) = TestSigner.Sign(RandomNumberGenerator.GetBytes(32), challenge.Nonce); // signed by someone else
        Assert.Null(new SchnorrAuthScheme().Verify(pubkey, challenge, otherSig));
    }

    [Fact]
    public void Malformed_inputs_return_null_not_throw()
    {
        var scheme = new SchnorrAuthScheme();
        Assert.Null(scheme.Verify("not-hex", Issue(), new byte[10]));
        Assert.Null(scheme.Verify("deadbeef", Issue(), Array.Empty<byte>()));
    }

    [Fact]
    public void Short_nonce_in_challenge_returns_null_not_throw()
    {
        // A valid sig over a proper 32-byte nonce, but the challenge carries a short nonce — must return null, not throw.
        var seed = RandomNumberGenerator.GetBytes(32);
        var (pubkey, _) = TestSigner.Sign(seed, new string('0', 64));
        var (_, sig) = TestSigner.Sign(seed, new string('0', 64));
        var badChallenge = new Challenge("00", "schnorr", DateTimeOffset.UtcNow.AddMinutes(5)); // 1-byte nonce
        Assert.Null(new SchnorrAuthScheme().Verify(pubkey, badChallenge, sig));
    }
}
