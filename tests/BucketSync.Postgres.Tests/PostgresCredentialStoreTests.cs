using BucketSync.Core;
using BucketSync.Postgres;
using BucketSync.TestKit;
using Xunit;

namespace BucketSync.Postgres.Tests;

[Collection(nameof(PostgresCollection))]
public class PostgresCredentialStoreTests : CredentialStoreContractTests
{
    private readonly PostgresFixture _fx;
    public PostgresCredentialStoreTests(PostgresFixture fx) => _fx = fx;

    // The PostgresCollection serializes these tests, so clearing the shared table per store
    // keeps the contract's fixed ids isolated between methods.
    protected override async Task<ICredentialStore> NewStoreAsync()
    {
        await using (var cmd = _fx.DataSource.CreateCommand("DELETE FROM credentials"))
            await cmd.ExecuteNonQueryAsync();
        return new PostgresCredentialStore(_fx.DataSource);
    }
}
