using System.Net.Http.Json;
using BucketSync.TestKit;

namespace BucketSync.Api.Tests;

public static class TestAuth
{
    /// <summary>Runs the full challenge→sign→verify handshake and returns a bearer token.</summary>
    public static async Task<string> AuthenticateAsync(HttpClient client, byte[] seed)
    {
        var (pubkey, _) = TestSigner.Sign(seed, "0000000000000000000000000000000000000000000000000000000000000000"); // derive pubkey
        var challengeResp = await client.PostAsJsonAsync("/v1/auth/challenge", new ChallengeRequest(pubkey));
        challengeResp.EnsureSuccessStatusCode();
        var challenge = (await challengeResp.Content.ReadFromJsonAsync<ChallengeResponse>())!;

        var (_, sig) = TestSigner.Sign(seed, challenge.Nonce);
        var verifyResp = await client.PostAsJsonAsync("/v1/auth/verify",
            new VerifyRequest(pubkey, challenge.Nonce, Convert.ToHexStringLower(sig), "test-device"));
        verifyResp.EnsureSuccessStatusCode();
        return (await verifyResp.Content.ReadFromJsonAsync<VerifyResponse>())!.Token;
    }
}
