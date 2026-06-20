using System.Net.Http.Json;
using BucketSync.TestKit;

namespace BucketSync.Api.Tests;

public static class TestAuth
{
    /// <summary>challenge → sign → register (new identity) or verify (already registered): returns a bearer token.
    /// Calling twice with the same seed lands on the same bucket.</summary>
    public static async Task<string> AuthenticateAsync(HttpClient client, byte[] seed)
    {
        var (pubkey, _) = TestSigner.Sign(seed, new string('0', 64)); // derive pubkey
        return await AttemptAsync(client, "register", pubkey, seed)   // creates a bucket for a brand-new pubkey
            ?? await AttemptAsync(client, "verify", pubkey, seed)     // already registered (409) → authenticate
            ?? throw new InvalidOperationException("schnorr auth failed");
    }

    private static async Task<string?> AttemptAsync(HttpClient client, string verb, string pubkey, byte[] seed)
    {
        var challengeResp = await client.PostAsync("/v1/auth/schnorr/challenge", null);
        challengeResp.EnsureSuccessStatusCode();
        var challenge = (await challengeResp.Content.ReadFromJsonAsync<ChallengeResponse>())!;

        var (_, sig) = TestSigner.Sign(seed, challenge.Nonce);
        var resp = await client.PostAsJsonAsync($"/v1/auth/schnorr/{verb}",
            new SchnorrAuthRequest(pubkey, challenge.Nonce, Convert.ToHexStringLower(sig), "test-device"));
        return resp.IsSuccessStatusCode ? (await resp.Content.ReadFromJsonAsync<TokenResponse>())!.Token : null;
    }
}
