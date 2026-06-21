using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BucketSync.Api.Tests;

public class ChangesEndpointTests
{
    [Fact]
    public async Task Changes_endpoint_filters_by_time()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var token = await TestAuth.AuthenticateAsync(client, RandomNumberGenerator.GetBytes(32));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var before = DateTimeOffset.UtcNow.AddMinutes(-1);
        (await client.PostAsJsonAsync("/v1/bucket/commit", new CommitRequest(new[]
        {
            new WriteOpDto("k", 0, "cse-v1", Convert.ToBase64String(new byte[] { 1 }), false)
        }))).EnsureSuccessStatusCode();

        // since a minute ago -> includes our commit
        var recent = await client.GetAsync($"/v1/bucket/changes?since={Uri.EscapeDataString(before.ToString("o"))}");
        recent.EnsureSuccessStatusCode();
        var page = (await recent.Content.ReadFromJsonAsync<ChangesResponse>())!;
        Assert.Contains(page.Entries, e => e.Key == "k");

        // since the future -> empty
        var future = DateTimeOffset.UtcNow.AddMinutes(5);
        var empty = await client.GetAsync($"/v1/bucket/changes?since={Uri.EscapeDataString(future.ToString("o"))}");
        empty.EnsureSuccessStatusCode();
        Assert.Empty((await empty.Content.ReadFromJsonAsync<ChangesResponse>())!.Entries);
    }
}
