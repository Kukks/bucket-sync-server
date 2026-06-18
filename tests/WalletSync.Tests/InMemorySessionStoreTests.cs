using WalletSync.Core;
using WalletSync.TestKit;

namespace WalletSync.Tests;

public class InMemorySessionStoreTests : SessionStoreContractTests
{
    protected override Task<ISessionStore> NewStoreAsync()
        => Task.FromResult<ISessionStore>(new InMemorySessionStore());
}
