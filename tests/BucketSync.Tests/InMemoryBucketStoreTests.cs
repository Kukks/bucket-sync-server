using BucketSync.Core;
using BucketSync.TestKit;

namespace BucketSync.Tests;

public class InMemoryBucketStoreTests : BucketStoreContractTests
{
    protected override async Task<IBucketStore> NewStoreAsync()
    {
        var store = new InMemoryBucketStore();
        await store.EnsureBucketAsync(Bucket);
        return store;
    }
}
