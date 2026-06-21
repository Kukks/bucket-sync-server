using BucketSync.Core;
using BucketSync.Postgres;
using BucketSync.TestKit;
using Xunit;

namespace BucketSync.Postgres.Tests;

[Collection(nameof(PostgresCollection))]
public class PostgresBucketChangesTests : BucketChangesContractTests
{
    private readonly PostgresFixture _fx;
    public PostgresBucketChangesTests(PostgresFixture fx) => _fx = fx;

    protected override async Task<IBucketStore> NewStoreAsync(FakeTimeProvider time)
    {
        // Bucket is unique per test instance, so a shared DB stays isolated; the store uses the
        // fake clock for updated_at, so the time-range assertions are deterministic.
        var s = new PostgresBucketStore(_fx.DataSource, time);
        await s.EnsureBucketAsync(Bucket);
        return s;
    }
}
