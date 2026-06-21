namespace BucketSync.Core;

public interface IBucketStore
{
    Task EnsureBucketAsync(string bucketId, CancellationToken ct = default);
    Task<BucketHead> GetHeadAsync(string bucketId, CancellationToken ct = default);
    Task<IReadOnlyList<BucketEntry>> GetBatchAsync(string bucketId, IReadOnlyList<string> keys, CancellationToken ct = default);
    Task<CommitResult> CommitBatchAsync(string bucketId, IReadOnlyList<WriteOp> ops, CancellationToken ct = default);
    Task<DiffPage> DiffAsync(string bucketId, long sinceSeq, int limit, CancellationToken ct = default);
    /// <summary>Audit query: entries changed after <paramref name="since"/> (commit time), paged by distinct
    /// timestamp (batch-respecting) up to <paramref name="limit"/> commits. Approximate — not a sync cursor.</summary>
    Task<ChangesPage> ChangesSinceAsync(string bucketId, DateTimeOffset since, int limit, CancellationToken ct = default);
}

/// <summary>
/// A pluggable auth scheme. This is a thin marker (name + discovery); the substantive contract is
/// "verify a proof, produce a <see cref="VerifiedCredential"/>", done in the scheme's typed module and
/// endpoints (so source-gen JSON / the Request Delegate Generator stay AOT-clean — no opaque credential).
/// </summary>
public interface IAuthScheme
{
    string Scheme { get; }
}

/// <summary>
/// The identity registry: maps a proven (scheme, credentialId) to exactly one bucket.
/// This is the decoupling that replaces bucketId = hash(pubkey).
/// </summary>
public interface ICredentialStore
{
    Task<string?> ResolveBucketAsync(string scheme, string credentialId, CancellationToken ct = default);
    /// <summary>The full stored credential (incl. public key), or null if unregistered.</summary>
    Task<VerifiedCredential?> GetAsync(string scheme, string credentialId, CancellationToken ct = default);
    /// <summary>Bind a credential to a bucket. Returns false if (scheme, credentialId) is already bound (to any bucket).</summary>
    Task<bool> BindAsync(VerifiedCredential credential, string bucketId, CancellationToken ct = default);
    Task<IReadOnlyList<VerifiedCredential>> ListAsync(string bucketId, CancellationToken ct = default);
}

public interface IChallengeStore
{
    Task<Challenge> IssueAsync(string scheme, CancellationToken ct = default);
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
