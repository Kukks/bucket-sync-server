using System.Security.Cryptography;
using WalletSync.Auth;
using WalletSync.Core;
using WalletSync.TestKit;

namespace WalletSync.Tests;

public class SchnorrAuthenticatorTests
{
    private static Challenge Issue(string pubkey) =>
        new(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), pubkey, DateTimeOffset.UtcNow.AddMinutes(5));

    [Fact]
    public async Task Valid_signature_yields_principal_with_derived_bucket()
    {
        var seed = RandomNumberGenerator.GetBytes(32);
        var (pubkey, _) = TestSigner.Sign(seed, new string('0', 64)); // get pubkey
        var challenge = Issue(pubkey);
        var (_, sig) = TestSigner.Sign(seed, challenge.Nonce);

        var principal = await new SchnorrAuthenticator().VerifyAsync(challenge, sig);
        Assert.NotNull(principal);
        Assert.Equal(pubkey, principal!.Pubkey);
        Assert.Equal(BucketIdentity.Derive(pubkey), principal.BucketId);
    }

    [Fact]
    public async Task Signature_over_a_different_nonce_is_rejected()
    {
        var seed = RandomNumberGenerator.GetBytes(32);
        var (pubkey, _) = TestSigner.Sign(seed, new string('0', 64));
        var challenge = Issue(pubkey);
        var (_, wrongSig) = TestSigner.Sign(seed, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
        Assert.Null(await new SchnorrAuthenticator().VerifyAsync(challenge, wrongSig));
    }

    [Fact]
    public async Task Signature_from_a_different_key_is_rejected()
    {
        var (pubkey, _) = TestSigner.Sign(RandomNumberGenerator.GetBytes(32), new string('0', 64));
        var challenge = Issue(pubkey);
        var (_, otherSig) = TestSigner.Sign(RandomNumberGenerator.GetBytes(32), challenge.Nonce); // signed by someone else
        Assert.Null(await new SchnorrAuthenticator().VerifyAsync(challenge, otherSig));
    }

    [Fact]
    public async Task Malformed_inputs_return_null_not_throw()
    {
        var auth = new SchnorrAuthenticator();
        Assert.Null(await auth.VerifyAsync(new Challenge("zz", "not-hex", DateTimeOffset.UtcNow.AddMinutes(5)), new byte[10]));
        Assert.Null(await auth.VerifyAsync(new Challenge("00", "deadbeef", DateTimeOffset.UtcNow.AddMinutes(5)), Array.Empty<byte>()));
    }

    [Fact]
    public async Task Short_nonce_in_challenge_returns_null_not_throw()
    {
        // A valid sig over a proper 32-byte nonce, but the challenge carries a short nonce — VerifyAsync must return null, not throw.
        var seed = RandomNumberGenerator.GetBytes(32);
        var (pubkey, _) = TestSigner.Sign(seed, new string('0', 64));
        var (_, sig) = TestSigner.Sign(seed, new string('0', 64));
        var badChallenge = new Challenge("00", pubkey, DateTimeOffset.UtcNow.AddMinutes(5)); // 1-byte nonce
        Assert.Null(await new SchnorrAuthenticator().VerifyAsync(badChallenge, sig));
    }
}
