using WalletSync.Core;
using WalletSync.TestKit;

namespace WalletSync.Tests;

public class InMemoryChallengeStoreTests : ChallengeStoreContractTests
{
    protected override Task<IChallengeStore> NewStoreAsync()
        => Task.FromResult<IChallengeStore>(new InMemoryChallengeStore());
}
