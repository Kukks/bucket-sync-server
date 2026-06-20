using System.Collections.Concurrent;

namespace BucketSync.Core;

public sealed class InMemoryCredentialStore : ICredentialStore
{
    // (scheme, credentialId) -> bucketId, and -> the stored credential. Tuple key uses ordinal string equality.
    private readonly ConcurrentDictionary<(string Scheme, string CredentialId), string> _bucketByCred = new();
    private readonly ConcurrentDictionary<(string Scheme, string CredentialId), VerifiedCredential> _creds = new();

    public Task<string?> ResolveBucketAsync(string scheme, string credentialId, CancellationToken ct = default) =>
        Task.FromResult(_bucketByCred.TryGetValue((scheme, credentialId), out var b) ? b : null);

    public Task<bool> BindAsync(VerifiedCredential credential, string bucketId, CancellationToken ct = default)
    {
        var key = (credential.Scheme, credential.CredentialId);
        if (!_bucketByCred.TryAdd(key, bucketId)) return Task.FromResult(false); // already bound
        _creds[key] = credential;
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<VerifiedCredential>> ListAsync(string bucketId, CancellationToken ct = default)
    {
        var list = _bucketByCred.Where(kv => kv.Value == bucketId).Select(kv => _creds[kv.Key]).ToList();
        return Task.FromResult<IReadOnlyList<VerifiedCredential>>(list);
    }
}
