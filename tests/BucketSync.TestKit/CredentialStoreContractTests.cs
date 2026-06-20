using BucketSync.Core;
using Xunit;

namespace BucketSync.TestKit;

public abstract class CredentialStoreContractTests
{
    protected abstract Task<ICredentialStore> NewStoreAsync();

    private static VerifiedCredential Cred(string scheme, string id, byte[]? pk = null) => new(scheme, id, pk, null);

    [Fact]
    public async Task Bind_then_resolve_returns_bucket()
    {
        var s = await NewStoreAsync();
        Assert.True(await s.BindAsync(Cred("schnorr", "abc"), "bucket-1"));
        Assert.Equal("bucket-1", await s.ResolveBucketAsync("schnorr", "abc"));
    }

    [Fact]
    public async Task Resolve_unknown_returns_null()
        => Assert.Null(await (await NewStoreAsync()).ResolveBucketAsync("schnorr", "nope"));

    [Fact]
    public async Task Bind_is_unique_per_scheme_and_credential()
    {
        var s = await NewStoreAsync();
        Assert.True(await s.BindAsync(Cred("schnorr", "abc"), "bucket-1"));
        Assert.False(await s.BindAsync(Cred("schnorr", "abc"), "bucket-2")); // already bound — second bind rejected
        Assert.Equal("bucket-1", await s.ResolveBucketAsync("schnorr", "abc")); // mapping unchanged
    }

    [Fact]
    public async Task Same_credentialId_different_scheme_is_distinct()
    {
        var s = await NewStoreAsync();
        Assert.True(await s.BindAsync(Cred("schnorr", "x"), "b1"));
        Assert.True(await s.BindAsync(Cred("passkey", "x"), "b2"));
        Assert.Equal("b1", await s.ResolveBucketAsync("schnorr", "x"));
        Assert.Equal("b2", await s.ResolveBucketAsync("passkey", "x"));
    }

    [Fact]
    public async Task List_returns_all_credentials_for_a_bucket()
    {
        var s = await NewStoreAsync();
        await s.BindAsync(Cred("schnorr", "a", new byte[] { 1, 2 }), "shared");
        await s.BindAsync(Cred("passkey", "b"), "shared");
        await s.BindAsync(Cred("schnorr", "c"), "other-bucket");

        var list = await s.ListAsync("shared");
        Assert.Equal(2, list.Count);
        Assert.Contains(list, c => c.Scheme == "schnorr" && c.CredentialId == "a");
        Assert.Contains(list, c => c.Scheme == "passkey" && c.CredentialId == "b");
    }
}
