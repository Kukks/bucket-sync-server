namespace WalletSync.Core;

public interface IBucketStore
{
    Task EnsureBucketAsync(string bucketId, string ownerPubkey, CancellationToken ct = default);
    Task<BucketHead> GetHeadAsync(string bucketId, CancellationToken ct = default);
    Task<IReadOnlyList<BucketEntry>> GetBatchAsync(string bucketId, IReadOnlyList<string> keys, CancellationToken ct = default);
    Task<CommitResult> CommitBatchAsync(string bucketId, IReadOnlyList<WriteOp> ops, CancellationToken ct = default);
    Task<DiffPage> DiffAsync(string bucketId, long sinceSeq, int limit, CancellationToken ct = default);
}

public interface IAuthenticator
{
    Task<Principal?> VerifyAsync(Challenge challenge, byte[] signature, CancellationToken ct = default);
}

public interface IChallengeStore
{
    Task<Challenge> IssueAsync(string pubkey, CancellationToken ct = default);
    Task<Challenge?> ConsumeAsync(string nonce, CancellationToken ct = default);
}

public interface ISessionStore
{
    Task<Session> CreateAsync(Principal principal, string? device, CancellationToken ct = default);
    Task<Session?> ValidateAsync(string token, CancellationToken ct = default);
    Task RevokeAsync(string token, CancellationToken ct = default);
}

public interface IChangeNotifier
{
    Task PublishAsync(string bucketId, long seq, CancellationToken ct = default);
    IAsyncEnumerable<long> Subscribe(string bucketId, CancellationToken ct);
}
