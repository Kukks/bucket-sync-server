namespace WalletSync.Core;

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

public sealed record Principal(string Pubkey, string BucketId);

public sealed record Challenge(string Nonce, string Pubkey, DateTimeOffset ExpiresAt);

public sealed record Session(string Token, string BucketId, string? Device, DateTimeOffset ExpiresAt);
