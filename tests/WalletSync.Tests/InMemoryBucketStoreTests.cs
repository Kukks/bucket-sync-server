using WalletSync.Core;
using WalletSync.TestKit;

namespace WalletSync.Tests;

public class InMemoryBucketStoreTests : BucketStoreContractTests
{
    protected override async Task<IBucketStore> NewStoreAsync()
    {
        var store = new InMemoryBucketStore();
        await store.EnsureBucketAsync(Bucket, Pubkey);
        return store;
    }
}
