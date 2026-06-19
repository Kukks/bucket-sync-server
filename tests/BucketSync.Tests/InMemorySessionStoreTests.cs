using BucketSync.Core;
using BucketSync.TestKit;

namespace BucketSync.Tests;

public class InMemorySessionStoreTests : SessionStoreContractTests
{
    protected override Task<ISessionStore> NewStoreAsync()
        => Task.FromResult<ISessionStore>(new InMemorySessionStore());
}
