using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using BucketSync.Auth;
using BucketSync.TestKit;

namespace BucketSync.Api.Tests;

public class PasskeyEndToEndTests
{
    private static async Task<string> ChallengeAsync(HttpClient client)
    {
        var r = await client.PostAsync("/v1/auth/passkey/challenge", null);
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<PasskeyChallengeResponse>())!.Nonce;
    }

    private static PasskeyRegisterRequest RegisterBody(TestPasskey pk, string nonce, string? device = null)
    {
        var (cd, att) = pk.Register(nonce);
        return new PasskeyRegisterRequest(nonce, Base64Url.Encode(cd), Base64Url.Encode(att), device);
    }

    private static PasskeyVerifyRequest VerifyBody(TestPasskey pk, string nonce)
    {
        var (cd, ad, sig) = pk.Assert(nonce);
        return new PasskeyVerifyRequest(nonce, pk.CredentialIdB64Url,
            Base64Url.Encode(cd), Base64Url.Encode(ad), Base64Url.Encode(sig), null);
    }

    [Fact]
    public async Task Register_creates_a_bucket_then_verify_authenticates()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var pk = new TestPasskey();

        var reg = await client.PostAsJsonAsync("/v1/auth/passkey/register", RegisterBody(pk, await ChallengeAsync(client)));
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        var token = (await reg.Content.ReadFromJsonAsync<TokenResponse>())!.Token;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/bucket/head")).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        var ver = await client.PostAsJsonAsync("/v1/auth/passkey/verify", VerifyBody(pk, await ChallengeAsync(client)));
        Assert.Equal(HttpStatusCode.OK, ver.StatusCode);
    }

    [Fact]
    public async Task Tampered_assertion_is_rejected()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var pk = new TestPasskey();
        (await client.PostAsJsonAsync("/v1/auth/passkey/register", RegisterBody(pk, await ChallengeAsync(client)))).EnsureSuccessStatusCode();

        // an assertion from a DIFFERENT key for the same credentialId must fail
        var attacker = new TestPasskey();
        var nonce = await ChallengeAsync(client);
        var (cd, ad, sig) = attacker.Assert(nonce);
        var forged = new PasskeyVerifyRequest(nonce, pk.CredentialIdB64Url,
            Base64Url.Encode(cd), Base64Url.Encode(ad), Base64Url.Encode(sig), null);
        var resp = await client.PostAsJsonAsync("/v1/auth/passkey/verify", forged);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Schnorr_created_bucket_plus_added_passkey_share_the_same_bucket()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        // 1) create a bucket with schnorr and commit something
        var schnorrToken = await TestAuth.AuthenticateAsync(client, RandomNumberGenerator.GetBytes(32));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", schnorrToken);
        var value = Convert.ToBase64String(new byte[] { 1, 2, 3 });
        var commit = await client.PostAsJsonAsync("/v1/bucket/commit",
            new CommitRequest(new[] { new WriteOpDto("k", 0, "cse-v1", value, false) }));
        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);

        // 2) add a passkey to THIS bucket (authenticated register -> 204)
        var pk = new TestPasskey();
        var add = await client.PostAsJsonAsync("/v1/auth/passkey/register", RegisterBody(pk, await ChallengeAsync(client)));
        Assert.Equal(HttpStatusCode.NoContent, add.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        // 3) authenticate with the passkey -> a token for the SAME bucket
        var ver = await client.PostAsJsonAsync("/v1/auth/passkey/verify", VerifyBody(pk, await ChallengeAsync(client)));
        Assert.Equal(HttpStatusCode.OK, ver.StatusCode);
        var passkeyToken = (await ver.Content.ReadFromJsonAsync<TokenResponse>())!.Token;

        // 4) the passkey session sees the schnorr session's data -> proves it's the same bucket
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", passkeyToken);
        var get = await client.PostAsJsonAsync("/v1/bucket/get", new GetRequest(new[] { "k" }));
        get.EnsureSuccessStatusCode();
        var entries = (await get.Content.ReadFromJsonAsync<EntriesResponse>())!;
        var e = Assert.Single(entries.Entries);
        Assert.Equal("k", e.Key);
        Assert.Equal(value, e.Value);
    }
}
