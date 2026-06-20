using System.Security.Cryptography;
using BucketSync.Auth;
using BucketSync.Core;
using BucketSync.TestKit;

namespace BucketSync.Tests;

public class PasskeyAuthSchemeTests
{
    private static readonly PasskeyOptions Opt = PasskeyOptions.LocalDev;

    private static Challenge Ch() =>
        new(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), "passkey", DateTimeOffset.UtcNow.AddMinutes(5));

    [Fact]
    public void Register_then_assert_round_trips()
    {
        var scheme = new PasskeyAuthScheme(Opt);
        var authenticator = new TestPasskey();

        var reg = Ch();
        var (rcd, att) = authenticator.Register(reg.Nonce);
        var cred = scheme.VerifyRegistration(rcd, att, reg);
        Assert.NotNull(cred);
        Assert.Equal("passkey", cred!.Scheme);
        Assert.Equal(authenticator.CredentialIdB64Url, cred.CredentialId);
        Assert.NotNull(cred.PublicKey);

        var auth = Ch();
        var (acd, ad, sig) = authenticator.Assert(auth.Nonce);
        Assert.True(scheme.VerifyAssertion(acd, ad, sig, cred.PublicKey!, auth));
    }

    [Fact]
    public void Assertion_against_a_different_challenge_fails()
    {
        var scheme = new PasskeyAuthScheme(Opt);
        var authenticator = new TestPasskey();
        var reg = Ch();
        var (rcd, att) = authenticator.Register(reg.Nonce);
        var cred = scheme.VerifyRegistration(rcd, att, reg)!;

        var (acd, ad, sig) = authenticator.Assert(Ch().Nonce);  // signed over challenge A
        Assert.False(scheme.VerifyAssertion(acd, ad, sig, cred.PublicKey!, Ch())); // verified against challenge B
    }

    [Fact]
    public void Assertion_from_a_different_key_fails()
    {
        var scheme = new PasskeyAuthScheme(Opt);
        var registrant = new TestPasskey();
        var reg = Ch();
        var (rcd, att) = registrant.Register(reg.Nonce);
        var cred = scheme.VerifyRegistration(rcd, att, reg)!;

        var attacker = new TestPasskey();          // different keypair, same rpId/origin
        var ch = Ch();
        var (acd, ad, sig) = attacker.Assert(ch.Nonce);
        Assert.False(scheme.VerifyAssertion(acd, ad, sig, cred.PublicKey!, ch)); // against the registrant's stored key
    }

    [Fact]
    public void Wrong_ceremony_type_is_rejected()
    {
        var scheme = new PasskeyAuthScheme(Opt);
        var authenticator = new TestPasskey();
        var reg = Ch();
        // an assertion (get) cannot satisfy registration (create)
        var (acd, _, _) = authenticator.Assert(reg.Nonce);
        var (_, att) = authenticator.Register(reg.Nonce);
        Assert.Null(scheme.VerifyRegistration(acd, att, reg));
    }
}
