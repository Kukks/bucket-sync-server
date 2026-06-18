using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WalletSync.Api.Tests;

public class BearerGateTests
{
    [Fact]
    public async Task Bucket_route_without_token_is_401()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var resp = await factory.CreateClient().GetAsync("/v1/bucket/head");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Bucket_route_with_garbage_token_is_401()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/bucket/head")).StatusCode);
    }

    [Fact]
    public async Task Bucket_route_with_valid_token_is_200()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var token = await TestAuth.AuthenticateAsync(client, RandomNumberGenerator.GetBytes(32));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/bucket/head")).StatusCode);
    }
}
