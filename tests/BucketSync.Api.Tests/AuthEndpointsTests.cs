using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using BucketSync.TestKit;

namespace BucketSync.Api.Tests;

public class AuthEndpointsTests
{
    private static async Task<ChallengeResponse> ChallengeAsync(HttpClient client)
    {
        var r = await client.PostAsync("/v1/auth/schnorr/challenge", null);
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<ChallengeResponse>())!;
    }

    [Fact]
    public async Task Full_handshake_returns_a_usable_token()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var token = await TestAuth.AuthenticateAsync(client, RandomNumberGenerator.GetBytes(32));
        Assert.False(string.IsNullOrEmpty(token));

        // the token must actually authenticate, not just be non-empty
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var head = await client.GetAsync("/v1/bucket/head");
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
    }

    [Fact]
    public async Task Register_creates_a_bucket_then_verify_authenticates_and_dup_is_409()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var seed = RandomNumberGenerator.GetBytes(32);
        var (pubkey, _) = TestSigner.Sign(seed, new string('0', 64));

        // verify before register => 401 (not registered)
        var ch0 = await ChallengeAsync(client);
        var (_, sig0) = TestSigner.Sign(seed, ch0.Nonce);
        var pre = await client.PostAsJsonAsync("/v1/auth/schnorr/verify",
            new SchnorrAuthRequest(pubkey, ch0.Nonce, Convert.ToHexStringLower(sig0), null));
        Assert.Equal(HttpStatusCode.Unauthorized, pre.StatusCode);

        // register => 200 (creates the bucket)
        var ch1 = await ChallengeAsync(client);
        var (_, sig1) = TestSigner.Sign(seed, ch1.Nonce);
        var reg = await client.PostAsJsonAsync("/v1/auth/schnorr/register",
            new SchnorrAuthRequest(pubkey, ch1.Nonce, Convert.ToHexStringLower(sig1), null));
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);

        // verify => 200 (now registered)
        var ch2 = await ChallengeAsync(client);
        var (_, sig2) = TestSigner.Sign(seed, ch2.Nonce);
        var ver = await client.PostAsJsonAsync("/v1/auth/schnorr/verify",
            new SchnorrAuthRequest(pubkey, ch2.Nonce, Convert.ToHexStringLower(sig2), null));
        Assert.Equal(HttpStatusCode.OK, ver.StatusCode);

        // re-register the same pubkey with no bearer => 409 (credential already owns a bucket)
        var ch3 = await ChallengeAsync(client);
        var (_, sig3) = TestSigner.Sign(seed, ch3.Nonce);
        var dup = await client.PostAsJsonAsync("/v1/auth/schnorr/register",
            new SchnorrAuthRequest(pubkey, ch3.Nonce, Convert.ToHexStringLower(sig3), null));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task Verify_with_wrong_signature_is_401()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var (pubkey, _) = TestSigner.Sign(RandomNumberGenerator.GetBytes(32), new string('0', 64));

        var challenge = await ChallengeAsync(client);
        var (_, wrongSig) = TestSigner.Sign(RandomNumberGenerator.GetBytes(32), challenge.Nonce); // different key
        var resp = await client.PostAsJsonAsync("/v1/auth/schnorr/verify",
            new SchnorrAuthRequest(pubkey, challenge.Nonce, Convert.ToHexStringLower(wrongSig), null));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Replaying_a_consumed_nonce_is_401()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var seed = RandomNumberGenerator.GetBytes(32);
        var (pubkey, _) = TestSigner.Sign(seed, new string('0', 64));

        var challenge = await ChallengeAsync(client);
        var (_, sig) = TestSigner.Sign(seed, challenge.Nonce);
        var body = new SchnorrAuthRequest(pubkey, challenge.Nonce, Convert.ToHexStringLower(sig), null);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/v1/auth/schnorr/register", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/auth/schnorr/register", body)).StatusCode); // nonce consumed
    }

    [Fact]
    public async Task Revoking_session_invalidates_token()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var token = await TestAuth.AuthenticateAsync(client, RandomNumberGenerator.GetBytes(32));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var del = await client.DeleteAsync("/v1/auth/session");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        // protected route must now reject the revoked token
        var head = await client.GetAsync("/v1/bucket/head");
        Assert.Equal(HttpStatusCode.Unauthorized, head.StatusCode);
    }
}
