using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using WalletSync.TestKit;

namespace WalletSync.Api.Tests;

public class AuthEndpointsTests
{
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
    public async Task Verify_with_wrong_signature_is_401()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var (pubkey, _) = TestSigner.Sign(RandomNumberGenerator.GetBytes(32), "0000000000000000000000000000000000000000000000000000000000000000");

        var challenge = (await (await client.PostAsJsonAsync("/v1/auth/challenge", new ChallengeRequest(pubkey)))
            .Content.ReadFromJsonAsync<ChallengeResponse>())!;
        // sign with a DIFFERENT key
        var (_, wrongSig) = TestSigner.Sign(RandomNumberGenerator.GetBytes(32), challenge.Nonce);

        var resp = await client.PostAsJsonAsync("/v1/auth/verify",
            new VerifyRequest(pubkey, challenge.Nonce, Convert.ToHexStringLower(wrongSig), null));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Replaying_a_consumed_nonce_is_401()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var seed = RandomNumberGenerator.GetBytes(32);
        var (pubkey, _) = TestSigner.Sign(seed, "0000000000000000000000000000000000000000000000000000000000000000");

        var challenge = (await (await client.PostAsJsonAsync("/v1/auth/challenge", new ChallengeRequest(pubkey)))
            .Content.ReadFromJsonAsync<ChallengeResponse>())!;
        var (_, sig) = TestSigner.Sign(seed, challenge.Nonce);
        var body = new VerifyRequest(pubkey, challenge.Nonce, Convert.ToHexStringLower(sig), null);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/v1/auth/verify", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/auth/verify", body)).StatusCode); // nonce consumed
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
