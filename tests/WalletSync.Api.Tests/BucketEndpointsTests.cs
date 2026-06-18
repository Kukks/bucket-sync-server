using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WalletSync.Api.Tests;

public class BucketEndpointsTests
{
    private static async Task<HttpClient> AuthedClientAsync(WebApplicationFactory<Program> f)
    {
        var c = f.CreateClient();
        var token = await TestAuth.AuthenticateAsync(c, RandomNumberGenerator.GetBytes(32));
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static WriteOpDto Put(string k, long v, byte[] val) =>
        new(k, v, "cse-v1", Convert.ToBase64String(val), false);

    [Fact]
    public async Task Commit_then_head_get_diff_round_trips()
    {
        await using var f = new WebApplicationFactory<Program>();
        var c = await AuthedClientAsync(f);

        var commit = await c.PostAsJsonAsync("/v1/bucket/commit", new CommitRequest(new[] { Put("vtxo:a", 0, new byte[] { 1, 2, 3 }) }));
        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);
        var commitBody = (await commit.Content.ReadFromJsonAsync<CommitResponse>())!;
        Assert.True(commitBody.Committed);
        Assert.Equal(1, commitBody.NewSeq);

        var head = (await (await c.GetAsync("/v1/bucket/head")).Content.ReadFromJsonAsync<HeadResponse>())!;
        Assert.Equal(1, head.CurrentSeq);

        var got = (await (await c.PostAsJsonAsync("/v1/bucket/get", new GetRequest(new[] { "vtxo:a" })))
            .Content.ReadFromJsonAsync<EntriesResponse>())!;
        var entry = Assert.Single(got.Entries);
        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), entry.Value);

        var diff = (await (await c.GetAsync("/v1/bucket/diff?since=0&limit=10")).Content.ReadFromJsonAsync<DiffResponse>())!;
        Assert.Equal("vtxo:a", Assert.Single(diff.Entries).Key);
        Assert.Equal(1, diff.NextSeq);
    }

    [Fact]
    public async Task Stale_commit_returns_409_with_conflicts()
    {
        await using var f = new WebApplicationFactory<Program>();
        var c = await AuthedClientAsync(f);
        await c.PostAsJsonAsync("/v1/bucket/commit", new CommitRequest(new[] { Put("k", 0, new byte[] { 1 }) })); // k -> v1

        var resp = await c.PostAsJsonAsync("/v1/bucket/commit", new CommitRequest(new[] { Put("k", 0, new byte[] { 9 }) })); // stale
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<CommitResponse>())!;
        Assert.False(body.Committed);
        Assert.Equal(1, Assert.Single(body.Conflicts).CurrentVersion);
    }

    [Fact]
    public async Task Two_devices_same_identity_share_a_bucket()
    {
        await using var f = new WebApplicationFactory<Program>();
        var seed = RandomNumberGenerator.GetBytes(32);

        var dev1 = f.CreateClient();
        var t1 = await TestAuth.AuthenticateAsync(dev1, seed);
        dev1.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", t1);

        var dev2 = f.CreateClient();
        var t2 = await TestAuth.AuthenticateAsync(dev2, seed); // SAME seed => same pubkey => same bucket
        dev2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", t2);

        await dev1.PostAsJsonAsync("/v1/bucket/commit", new CommitRequest(new[] { Put("shared", 0, new byte[] { 7 }) }));
        var diff = (await (await dev2.GetAsync("/v1/bucket/diff?since=0&limit=10")).Content.ReadFromJsonAsync<DiffResponse>())!;
        Assert.Equal("shared", Assert.Single(diff.Entries).Key); // device 2 sees device 1's write
    }
}
