using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using BucketSync.Cse;

namespace BucketSync.Api.Tests;

public class CseV1EndToEndTests
{
    [Fact]
    public async Task Real_cse_v1_ciphertext_round_trips_through_the_opaque_store()
    {
        await using var factory = new WebApplicationFactory<Program>(); // in-memory backend (default)
        var client = factory.CreateClient();

        var token = await TestAuth.AuthenticateAsync(client, RandomNumberGenerator.GetBytes(32));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Client seals a real envelope; the server only ever sees these opaque bytes.
        var kwk = RandomNumberGenerator.GetBytes(32); // key-wrapping key, distinct from the signing key
        var plaintext = System.Text.Encoding.UTF8.GetBytes("""{"vtxo":"abc","state":"settled"}""");
        var envelope = CseV1Envelope.Seal(plaintext, kwk);

        var commit = await client.PostAsJsonAsync("/v1/bucket/commit",
            new CommitRequest(new[] { new WriteOpDto("vtxo:abc", 0, CseV1Envelope.Scheme, Convert.ToBase64String(envelope), false) }));
        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);

        // Read it back and decrypt — proves opaque storage round-trips end to end.
        var entries = (await (await client.PostAsJsonAsync("/v1/bucket/get", new GetRequest(new[] { "vtxo:abc" })))
            .Content.ReadFromJsonAsync<EntriesResponse>())!;
        var entry = Assert.Single(entries.Entries);
        Assert.Equal("cse-v1", entry.Scheme);

        var storedEnvelope = Convert.FromBase64String(entry.Value);
        Assert.Equal(plaintext, CseV1Envelope.Open(storedEnvelope, kwk));
    }
}
