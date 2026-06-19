using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;

namespace BucketSync.Api.Tests;

public class PostgresEndToEndTests : IClassFixture<PostgresApiFactory>
{
    private readonly PostgresApiFactory _factory;
    public PostgresEndToEndTests(PostgresApiFactory f) => _factory = f;

    private static WriteOpDto Put(string k, long v, byte[] val) =>
        new(k, v, "cse-v1", Convert.ToBase64String(val), false);

    [Fact]
    public async Task Full_sync_loop_over_postgres()
    {
        var client = _factory.CreateClient();
        var token = await TestAuth.AuthenticateAsync(client, RandomNumberGenerator.GetBytes(32));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // commit
        var commit = await client.PostAsJsonAsync("/v1/bucket/commit", new CommitRequest(new[] { Put("vtxo:a", 0, new byte[] { 1, 2 }) }));
        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);

        // head reflects it (with a real content hash)
        var head = (await (await client.GetAsync("/v1/bucket/head")).Content.ReadFromJsonAsync<HeadResponse>())!;
        Assert.Equal(1, head.CurrentSeq);
        Assert.Matches("^[0-9a-f]{64}$", head.ContentHash); // a real 64-char hex SHA-256, not merely non-empty

        // diff returns it
        var diff = (await (await client.GetAsync("/v1/bucket/diff?since=0&limit=10")).Content.ReadFromJsonAsync<DiffResponse>())!;
        Assert.Equal("vtxo:a", Assert.Single(diff.Entries).Key);

        // CAS conflict path -> 409
        var stale = await client.PostAsJsonAsync("/v1/bucket/commit", new CommitRequest(new[] { Put("vtxo:a", 0, new byte[] { 9 }) }));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        // revoke kills the session
        var del = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/v1/auth/session"));
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/bucket/head")).StatusCode);
    }
}
