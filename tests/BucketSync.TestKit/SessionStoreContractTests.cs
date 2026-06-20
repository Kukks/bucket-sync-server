using BucketSync.Core;
using Xunit;

namespace BucketSync.TestKit;

public abstract class SessionStoreContractTests
{
    protected abstract Task<ISessionStore> NewStoreAsync();
    private static Principal P => new("bucket-id", new VerifiedCredential("schnorr", "cred-1", null, null));

    [Fact]
    public async Task Create_then_validate_returns_bound_session()
    {
        var s = await NewStoreAsync();
        var session = await s.CreateAsync(P, "device-A");
        Assert.False(string.IsNullOrEmpty(session.Token));
        Assert.Equal("bucket-id", session.BucketId);

        var v = await s.ValidateAsync(session.Token);
        Assert.NotNull(v);
        Assert.Equal("bucket-id", v!.BucketId);
        Assert.Equal("device-A", v.Device);
    }

    [Fact]
    public async Task Validate_unknown_token_returns_null()
        => Assert.Null(await (await NewStoreAsync()).ValidateAsync("nope"));

    [Fact]
    public async Task Revoke_kills_the_session()
    {
        var s = await NewStoreAsync();
        var session = await s.CreateAsync(P, null);
        await s.RevokeAsync(session.Token);
        Assert.Null(await s.ValidateAsync(session.Token));
    }
}
