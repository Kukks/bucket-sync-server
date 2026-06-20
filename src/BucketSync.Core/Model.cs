namespace BucketSync.Core;

public sealed record BucketEntry(
    string BucketId, string Key, long Version, long Seq,
    string ContentHash, string Scheme, bool Deleted, byte[] Value);

public sealed record WriteOp(
    string Key, long ExpectedVersion, string Scheme, byte[] Value, bool Delete);

public sealed record Conflict(string Key, long CurrentVersion);

public sealed record CommitResult(
    bool Committed, long NewSeq, IReadOnlyList<Conflict> Conflicts);

public sealed record DiffPage(
    IReadOnlyList<BucketEntry> Entries, long NextSeq, bool HasMore);

public sealed record BucketHead(string BucketId, long CurrentSeq, string ContentHash);

public sealed record Principal(string BucketId, VerifiedCredential Credential);

/// <summary>A credential proven by a scheme. CredentialId is unique within a scheme and maps to one bucket.</summary>
public sealed record VerifiedCredential(string Scheme, string CredentialId, byte[]? PublicKey, string? Label);

public sealed record Challenge(string Nonce, string Scheme, DateTimeOffset ExpiresAt);

public sealed record Session(string Token, string BucketId, string? Device, DateTimeOffset ExpiresAt);
