using BucketSync.Core;
using Xunit;

namespace BucketSync.TestKit;

public abstract class ChallengeStoreContractTests
{
    protected abstract Task<IChallengeStore> NewStoreAsync();

    [Fact]
    public async Task Issue_returns_fresh_nonce_for_scheme()
    {
        var s = await NewStoreAsync();
        var c = await s.IssueAsync("schnorr");
        Assert.Equal(64, c.Nonce.Length);             // 32 bytes hex
        Assert.Equal("schnorr", c.Scheme);
        Assert.True(c.ExpiresAt > DateTimeOffset.UtcNow);

        var c2 = await s.IssueAsync("schnorr");
        Assert.NotEqual(c.Nonce, c2.Nonce);           // nonces are unique
    }

    [Fact]
    public async Task Consume_returns_challenge_once_then_null()
    {
        var s = await NewStoreAsync();
        var c = await s.IssueAsync("passkey");
        var first = await s.ConsumeAsync(c.Nonce);
        Assert.NotNull(first);
        Assert.Equal("passkey", first!.Scheme);
        Assert.Null(await s.ConsumeAsync(c.Nonce));   // single-use
    }

    [Fact]
    public async Task Consume_unknown_nonce_returns_null()
        => Assert.Null(await (await NewStoreAsync()).ConsumeAsync("00"));
}
