using System.Text.Json.Serialization;

namespace BucketSync.Api;

/// <summary>Health endpoint payload (named so it is source-gen serializable, not anonymous).</summary>
public record HealthResponse(string Status);

/// <summary>
/// Source-generated JSON metadata for every type that crosses the wire. Registered via
/// ConfigureHttpJsonOptions so minimal-API (de)serialization uses no reflection — required for
/// NativeAOT / trimming. Add any new request/response DTO here.
/// </summary>
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(ChallengeRequest))]
[JsonSerializable(typeof(ChallengeResponse))]
[JsonSerializable(typeof(VerifyRequest))]
[JsonSerializable(typeof(VerifyResponse))]
[JsonSerializable(typeof(HeadResponse))]
[JsonSerializable(typeof(GetRequest))]
[JsonSerializable(typeof(EntryDto))]
[JsonSerializable(typeof(EntriesResponse))]
[JsonSerializable(typeof(WriteOpDto))]
[JsonSerializable(typeof(CommitRequest))]
[JsonSerializable(typeof(ConflictDto))]
[JsonSerializable(typeof(CommitResponse))]
[JsonSerializable(typeof(DiffResponse))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
