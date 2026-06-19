using BucketSync.Core;
using BucketSync.Postgres;
using BucketSync.TestKit;
using Xunit;

namespace BucketSync.Postgres.Tests;

[Collection(nameof(PostgresCollection))]
public class PostgresChallengeStoreTests : ChallengeStoreContractTests
{
    private readonly PostgresFixture _fx;
    public PostgresChallengeStoreTests(PostgresFixture fx) => _fx = fx;
    protected override Task<IChallengeStore> NewStoreAsync()
        => Task.FromResult<IChallengeStore>(new PostgresChallengeStore(_fx.DataSource));
}
