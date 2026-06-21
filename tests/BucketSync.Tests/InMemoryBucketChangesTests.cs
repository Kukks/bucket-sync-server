using BucketSync.Core;
using BucketSync.TestKit;

namespace BucketSync.Tests;

public class InMemoryBucketChangesTests : BucketChangesContractTests
{
    protected override async Task<IBucketStore> NewStoreAsync(FakeTimeProvider time)
    {
        var s = new InMemoryBucketStore(time);
        await s.EnsureBucketAsync(Bucket);
        return s;
    }
}
