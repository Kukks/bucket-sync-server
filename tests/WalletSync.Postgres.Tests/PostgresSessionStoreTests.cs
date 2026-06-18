using WalletSync.Core;
using WalletSync.Postgres;
using WalletSync.TestKit;
using Xunit;

namespace WalletSync.Postgres.Tests;

[Collection(nameof(PostgresCollection))]
public class PostgresSessionStoreTests : SessionStoreContractTests
{
    private readonly PostgresFixture _fx;
    public PostgresSessionStoreTests(PostgresFixture fx) => _fx = fx;
    protected override Task<ISessionStore> NewStoreAsync()
        => Task.FromResult<ISessionStore>(new PostgresSessionStore(_fx.DataSource));
}
