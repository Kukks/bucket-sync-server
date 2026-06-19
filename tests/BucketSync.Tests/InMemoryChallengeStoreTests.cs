using BucketSync.Core;
using BucketSync.TestKit;

namespace BucketSync.Tests;

public class InMemoryChallengeStoreTests : ChallengeStoreContractTests
{
    protected override Task<IChallengeStore> NewStoreAsync()
        => Task.FromResult<IChallengeStore>(new InMemoryChallengeStore());
}
