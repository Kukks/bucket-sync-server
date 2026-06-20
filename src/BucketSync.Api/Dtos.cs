using BucketSync.Core;

namespace BucketSync.Api;

public record ChallengeResponse(string Nonce, DateTimeOffset ExpiresAt);
public record TokenResponse(string Token, DateTimeOffset ExpiresAt);
public record SchnorrAuthRequest(string Pubkey, string Nonce, string Signature, string? Device);

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
    public WriteOp ToWriteOp()
    {
        if (Delete) return new(Key, ExpectedVersion, Scheme, Array.Empty<byte>(), Delete: true);
        // A non-delete op must carry a value. Null is a malformed request (→ 400);
        // an empty/invalid base64 string still maps to 400 via the commit handler's catch.
        if (Value is null) throw new ArgumentException("value is required for a non-delete op", nameof(Value));
        return new(Key, ExpectedVersion, Scheme, Convert.FromBase64String(Value), Delete: false);
    }
}
public record CommitRequest(IReadOnlyList<WriteOpDto> Ops);
public record ConflictDto(string Key, long CurrentVersion);
public record CommitResponse(bool Committed, long NewSeq, IReadOnlyList<ConflictDto> Conflicts)
{
    public static CommitResponse From(CommitResult r) =>
        new(r.Committed, r.NewSeq, r.Conflicts.Select(c => new ConflictDto(c.Key, c.CurrentVersion)).ToList());
}
public record DiffResponse(IReadOnlyList<EntryDto> Entries, long NextSeq, bool HasMore);
