using BucketSync.Core;

namespace BucketSync.Auth;

/// <summary>
/// WebAuthn / passkey auth scheme (ES256). Registration learns the credential's public key (attestation
/// is NOT verified — provenance is irrelevant to bucket binding); authentication verifies an assertion
/// signature against the stored key. The challenge is the 32-byte nonce; clientDataJSON.challenge is its base64url.
/// </summary>
public sealed class PasskeyAuthScheme : IAuthScheme
{
    public string Scheme => "passkey";
    private readonly PasskeyOptions _opt;
    public PasskeyAuthScheme(PasskeyOptions opt) => _opt = opt;

    public string RpId => _opt.RpId;

    private string ExpectedChallenge(Challenge challenge)
    {
        try { return WebAuthn.B64Url(Convert.FromHexString(challenge.Nonce)); }
        catch (FormatException) { return ""; }
    }

    /// <summary>create ceremony → the proven credential (incl. its COSE public key), or null if invalid.</summary>
    public VerifiedCredential? VerifyRegistration(byte[] clientDataJson, byte[] attestationObject, Challenge challenge, string? label = null)
    {
        var cd = WebAuthn.ParseClientData(clientDataJson);
        if (cd is null || cd.Type != "webauthn.create" || cd.Origin != _opt.Origin) return null;
        var expected = ExpectedChallenge(challenge);
        if (expected.Length == 0 || cd.Challenge != expected) return null;

        var reg = WebAuthn.ParseRegistration(attestationObject);
        if (reg is null) return null;
        var (credId, coseKey) = reg.Value;
        using var key = WebAuthn.ImportEs256(coseKey); // ensure it's a usable ES256 key
        if (key is null) return null;
        return new VerifiedCredential("passkey", WebAuthn.B64Url(credId), coseKey, label);
    }

    /// <summary>get ceremony → true if the assertion verifies against the stored COSE key.</summary>
    public bool VerifyAssertion(byte[] clientDataJson, byte[] authenticatorData, byte[] signature, byte[] storedCoseKey, Challenge challenge)
    {
        var cd = WebAuthn.ParseClientData(clientDataJson);
        if (cd is null || cd.Type != "webauthn.get" || cd.Origin != _opt.Origin) return false;
        var expected = ExpectedChallenge(challenge);
        if (expected.Length == 0 || cd.Challenge != expected) return false;

        var ad = WebAuthn.ParseAuthData(authenticatorData);
        if (ad is null) return false;
        var (rpIdHash, flags) = ad.Value;
        if ((flags & WebAuthn.FlagUserPresent) == 0) return false;
        if (!rpIdHash.AsSpan().SequenceEqual(WebAuthn.RpIdHash(_opt.RpId))) return false;

        using var key = WebAuthn.ImportEs256(storedCoseKey);
        return key is not null && WebAuthn.VerifyEs256(key, authenticatorData, clientDataJson, signature);
    }
}
