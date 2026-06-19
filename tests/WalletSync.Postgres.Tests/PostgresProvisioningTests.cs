using WalletSync.Core;
using WalletSync.Postgres;
using Xunit;

namespace WalletSync.Postgres.Tests;

[Collection(nameof(PostgresCollection))]
public class PostgresProvisioningTests
{
    private readonly PostgresFixture _fx;
    public PostgresProvisioningTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Commit_to_unprovisioned_bucket_throws_clear_error()
    {
        var store = new PostgresBucketStore(_fx.DataSource);
        var bucket = $"never-provisioned-{System.Guid.NewGuid():N}";
        var op = new WriteOp("k", 0, "cse-v1", new byte[] { 1 }, false);

        // No EnsureBucketAsync first: commit must fail with a clear InvalidOperationException,
        // not a NullReferenceException from unboxing a missing current_seq.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CommitBatchAsync(bucket, new[] { op }));
    }
}
