using WalletSync.Core;

namespace WalletSync.Api;

public record ChallengeRequest(string Pubkey);
public record ChallengeResponse(string Nonce, DateTimeOffset ExpiresAt);
public record VerifyRequest(string Pubkey, string Nonce, string Signature, string? Device);
public record VerifyResponse(string Token, DateTimeOffset ExpiresAt);

public record HeadResponse(long CurrentSeq, string ContentHash);

public record GetRequest(IReadOnlyList<string> Keys);
public record EntryDto(string Key, long Version, long Seq, string ContentHash, string Scheme, bool Deleted, string Value)
{
    public static EntryDto From(BucketEntry e) =>
        new(e.Key, e.Version, e.Seq, e.ContentHash, e.Scheme, e.Deleted, Convert.ToBase64String(e.Value));
}
public record EntriesResponse(IReadOnlyList<EntryDto> Entries);

public record WriteOpDto(string Key, long ExpectedVersion, string Scheme, string Value, bool Delete)
{
    public WriteOp ToWriteOp() =>
        new(Key, ExpectedVersion, Scheme, Delete ? Array.Empty<byte>() : Convert.FromBase64String(Value ?? ""), Delete);
}
public record CommitRequest(IReadOnlyList<WriteOpDto> Ops);
public record ConflictDto(string Key, long CurrentVersion);
public record CommitResponse(bool Committed, long NewSeq, IReadOnlyList<ConflictDto> Conflicts)
{
    public static CommitResponse From(CommitResult r) =>
        new(r.Committed, r.NewSeq, r.Conflicts.Select(c => new ConflictDto(c.Key, c.CurrentVersion)).ToList());
}
public record DiffResponse(IReadOnlyList<EntryDto> Entries, long NextSeq, bool HasMore);
