using WalletSync.Core;
using WalletSync.Postgres;
using WalletSync.TestKit;
using Xunit;

namespace WalletSync.Postgres.Tests;

[Collection(nameof(PostgresCollection))]
public class PostgresBucketStoreTests : BucketStoreContractTests
{
    private readonly PostgresFixture _fx;
    public PostgresBucketStoreTests(PostgresFixture fx) => _fx = fx;

    protected override async Task<IBucketStore> NewStoreAsync()
    {
        // Each call gets a unique physical bucket so concurrent store instances are isolated.
        var physicalBucket = $"test-{Guid.NewGuid():N}";
        var inner = new PostgresBucketStore(_fx.DataSource);
        await inner.EnsureBucketAsync(physicalBucket, Pubkey);
        // Return a wrapper that redirects the well-known "b-test" bucket to the unique physical one.
        return new ScopedBucketStore(inner, Bucket, physicalBucket);
    }

    /// <summary>
    /// Redirects all calls using the contract's well-known <paramref name="logicalBucket"/>
    /// to a unique <paramref name="physicalBucket"/> so each test instance is fully isolated.
    /// </summary>
    private sealed class ScopedBucketStore : IBucketStore
    {
        private readonly IBucketStore _inner;
        private readonly string _logical;
        private readonly string _physical;

        public ScopedBucketStore(IBucketStore inner, string logical, string physical)
        {
            _inner = inner;
            _logical = logical;
            _physical = physical;
        }

        private string Map(string id) => id == _logical ? _physical : id;

        public Task EnsureBucketAsync(string bucketId, string ownerPubkey, CancellationToken ct = default) =>
            _inner.EnsureBucketAsync(Map(bucketId), ownerPubkey, ct);

        public Task<BucketHead> GetHeadAsync(string bucketId, CancellationToken ct = default) =>
            _inner.GetHeadAsync(Map(bucketId), ct);

        public Task<IReadOnlyList<BucketEntry>> GetBatchAsync(string bucketId, IReadOnlyList<string> keys, CancellationToken ct = default) =>
            _inner.GetBatchAsync(Map(bucketId), keys, ct);

        public Task<CommitResult> CommitBatchAsync(string bucketId, IReadOnlyList<WriteOp> ops, CancellationToken ct = default) =>
            _inner.CommitBatchAsync(Map(bucketId), ops, ct);

        public Task<DiffPage> DiffAsync(string bucketId, long sinceSeq, int limit, CancellationToken ct = default) =>
            _inner.DiffAsync(Map(bucketId), sinceSeq, limit, ct);
    }
}
