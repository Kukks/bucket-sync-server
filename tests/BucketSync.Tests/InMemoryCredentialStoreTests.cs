using BucketSync.Core;
using BucketSync.TestKit;

namespace BucketSync.Tests;

public class InMemoryCredentialStoreTests : CredentialStoreContractTests
{
    protected override Task<ICredentialStore> NewStoreAsync() =>
        Task.FromResult<ICredentialStore>(new InMemoryCredentialStore());
}
