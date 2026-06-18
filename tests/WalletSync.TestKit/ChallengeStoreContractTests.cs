using WalletSync.Core;
using Xunit;

namespace WalletSync.TestKit;

public abstract class ChallengeStoreContractTests
{
    protected abstract Task<IChallengeStore> NewStoreAsync();

    [Fact]
    public async Task Issue_returns_fresh_nonce_for_pubkey()
    {
        var s = await NewStoreAsync();
        var c = await s.IssueAsync("pubkey-1");
        Assert.Equal(64, c.Nonce.Length);             // 32 bytes hex
        Assert.Equal("pubkey-1", c.Pubkey);
        Assert.True(c.ExpiresAt > DateTimeOffset.UtcNow);

        var c2 = await s.IssueAsync("pubkey-1");
        Assert.NotEqual(c.Nonce, c2.Nonce);           // nonces are unique
    }

    [Fact]
    public async Task Consume_returns_challenge_once_then_null()
    {
        var s = await NewStoreAsync();
        var c = await s.IssueAsync("pk");
        var first = await s.ConsumeAsync(c.Nonce);
        Assert.NotNull(first);
        Assert.Equal("pk", first!.Pubkey);
        Assert.Null(await s.ConsumeAsync(c.Nonce));   // single-use
    }

    [Fact]
    public async Task Consume_unknown_nonce_returns_null()
        => Assert.Null(await (await NewStoreAsync()).ConsumeAsync("00"));
}
