using Microsoft.AspNetCore.Mvc.Testing;

namespace WalletSync.Api.Tests;

public class HealthTests
{
    [Fact]
    public async Task Health_returns_200()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var resp = await client.GetAsync("/health");
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
    }
}
