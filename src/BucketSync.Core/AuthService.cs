namespace BucketSync.Core;

/// <summary>
/// Scheme-agnostic auth core. A scheme proves a credential; this maps it to a bucket and a session.
/// Rule: any auth can create a bucket; any other credential can be added to that bucket.
/// </summary>
public sealed class AuthService
{
    private readonly IBucketStore _buckets;
    private readonly ICredentialStore _credentials;
    private readonly ISessionStore _sessions;

    public AuthService(IBucketStore buckets, ICredentialStore credentials, ISessionStore sessions)
    {
        _buckets = buckets;
        _credentials = credentials;
        _sessions = sessions;
    }

    /// <summary>register, no bearer: mint a new bucket for a brand-new credential.
    /// Returns null if the credential is already bound to a bucket (caller → 409).</summary>
    public async Task<Session?> CreateBucketAsync(VerifiedCredential credential, string? device, CancellationToken ct = default)
    {
        var bucketId = BucketIdentity.NewBucketId();
        if (!await _credentials.BindAsync(credential, bucketId, ct)) return null; // already bound elsewhere
        await _buckets.EnsureBucketAsync(bucketId, ct);
        return await _sessions.CreateAsync(new Principal(bucketId, credential), device, ct);
    }

    /// <summary>verify: authenticate a registered credential.
    /// Returns null if the credential is not registered (caller → 401).</summary>
    public async Task<Session?> AuthenticateAsync(VerifiedCredential credential, string? device, CancellationToken ct = default)
    {
        var bucketId = await _credentials.ResolveBucketAsync(credential.Scheme, credential.CredentialId, ct);
        if (bucketId is null) return null;
        return await _sessions.CreateAsync(new Principal(bucketId, credential), device, ct);
    }

    /// <summary>register, with bearer: add a credential to an existing bucket.
    /// true = bound (or already bound to this bucket); false = the credential belongs to another bucket (caller → 409).</summary>
    public async Task<bool> AddCredentialAsync(string bucketId, VerifiedCredential credential, CancellationToken ct = default)
    {
        var existing = await _credentials.ResolveBucketAsync(credential.Scheme, credential.CredentialId, ct);
        if (existing == bucketId) return true;       // idempotent
        if (existing is not null) return false;      // owned by another bucket
        return await _credentials.BindAsync(credential, bucketId, ct);
    }
}
