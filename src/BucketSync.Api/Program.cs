using Npgsql;
using BucketSync.Api;
using BucketSync.Auth;
using BucketSync.Core;
using BucketSync.Postgres;

var builder = WebApplication.CreateSlimBuilder(args);

// Source-generated JSON (no reflection) so the minimal API is NativeAOT / trim safe.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
builder.Services.AddOpenApi();

builder.Services.AddSingleton<SchnorrAuthScheme>();
builder.Services.AddSingleton(new PasskeyOptions(
    builder.Configuration["Passkey:RpId"] ?? "localhost",
    builder.Configuration["Passkey:Origin"] ?? "http://localhost"));
builder.Services.AddSingleton<PasskeyAuthScheme>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IChangeNotifier, InProcChangeNotifier>();
builder.Services.AddSingleton<SyncService>();

var backend = builder.Configuration["Backend"] ?? "InMemory";
if (string.Equals(backend, "Postgres", StringComparison.OrdinalIgnoreCase))
{
    var cs = builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres required when Backend=Postgres");
    builder.Services.AddSingleton(NpgsqlDataSource.Create(cs));
    builder.Services.AddSingleton<IBucketStore, PostgresBucketStore>();
    builder.Services.AddSingleton<ICredentialStore, PostgresCredentialStore>();
    builder.Services.AddSingleton<IChallengeStore, PostgresChallengeStore>();
    builder.Services.AddSingleton<ISessionStore, PostgresSessionStore>();
}
else
{
    builder.Services.AddSingleton<IBucketStore, InMemoryBucketStore>();
    builder.Services.AddSingleton<ICredentialStore, InMemoryCredentialStore>();
    builder.Services.AddSingleton<IChallengeStore, InMemoryChallengeStore>();
    builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();
}

var app = builder.Build();

if (string.Equals(backend, "Postgres", StringComparison.OrdinalIgnoreCase))
    await Migrations.ApplyAsync(app.Services.GetRequiredService<NpgsqlDataSource>());

app.MapGet("/health", () => Results.Ok(new HealthResponse("ok")));

// ---- auth: pluggable schemes (challenge -> register/verify -> opaque bearer + session) ----
var auth = app.MapGroup("/v1/auth");

auth.MapPost("/schnorr/challenge", async (IChallengeStore challenges, CancellationToken ct) =>
{
    var c = await challenges.IssueAsync("schnorr", ct);
    return Results.Ok(new ChallengeResponse(c.Nonce, c.ExpiresAt));
});

// register — no bearer: create a bucket for a brand-new pubkey; bearer: add this pubkey to my bucket.
auth.MapPost("/schnorr/register", async (SchnorrAuthRequest req, HttpContext http, IChallengeStore challenges,
    SchnorrAuthScheme scheme, AuthService authsvc, ISessionStore sessions, CancellationToken ct) =>
{
    var cred = await VerifySchnorr(req, challenges, scheme, ct);
    if (cred is null) return Results.Unauthorized();
    var bucketId = await BucketFromBearer(http, sessions);
    if (bucketId is not null)
        return await authsvc.AddCredentialAsync(bucketId, cred, ct) ? Results.NoContent() : Results.Conflict();
    var session = await authsvc.CreateBucketAsync(cred, req.Device, ct);
    return session is null ? Results.Conflict() : Results.Ok(new TokenResponse(session.Token, session.ExpiresAt));
});

// verify — authenticate an already-registered pubkey.
auth.MapPost("/schnorr/verify", async (SchnorrAuthRequest req, IChallengeStore challenges,
    SchnorrAuthScheme scheme, AuthService authsvc, CancellationToken ct) =>
{
    var cred = await VerifySchnorr(req, challenges, scheme, ct);
    if (cred is null) return Results.Unauthorized();
    var session = await authsvc.AuthenticateAsync(cred, req.Device, ct);
    return session is null ? Results.Unauthorized() : Results.Ok(new TokenResponse(session.Token, session.ExpiresAt));
});

auth.MapPost("/passkey/challenge", async (IChallengeStore challenges, PasskeyAuthScheme scheme, CancellationToken ct) =>
{
    var c = await challenges.IssueAsync("passkey", ct);
    return Results.Ok(new PasskeyChallengeResponse(c.Nonce, scheme.RpId, c.ExpiresAt));
});

// register — create ceremony. no bearer: create a bucket; bearer: add the passkey to my bucket.
auth.MapPost("/passkey/register", async (PasskeyRegisterRequest req, HttpContext http, IChallengeStore challenges,
    PasskeyAuthScheme scheme, AuthService authsvc, ISessionStore sessions, CancellationToken ct) =>
{
    var clientData = Base64Url.Decode(req.ClientDataJson);
    var attestation = Base64Url.Decode(req.AttestationObject);
    if (clientData is null || attestation is null) return Results.BadRequest();
    var challenge = await challenges.ConsumeAsync(req.Nonce, ct);
    if (challenge is null || challenge.Scheme != "passkey") return Results.Unauthorized();
    var cred = scheme.VerifyRegistration(clientData, attestation, challenge);
    if (cred is null) return Results.Unauthorized();

    var bucketId = await BucketFromBearer(http, sessions);
    if (bucketId is not null)
        return await authsvc.AddCredentialAsync(bucketId, cred, ct) ? Results.NoContent() : Results.Conflict();
    var session = await authsvc.CreateBucketAsync(cred, req.Device, ct);
    return session is null ? Results.Conflict() : Results.Ok(new TokenResponse(session.Token, session.ExpiresAt));
});

// verify — get ceremony: authenticate with a registered passkey.
auth.MapPost("/passkey/verify", async (PasskeyVerifyRequest req, IChallengeStore challenges,
    PasskeyAuthScheme scheme, ICredentialStore credentials, AuthService authsvc, CancellationToken ct) =>
{
    var clientData = Base64Url.Decode(req.ClientDataJson);
    var authData = Base64Url.Decode(req.AuthenticatorData);
    var sig = Base64Url.Decode(req.Signature);
    if (clientData is null || authData is null || sig is null) return Results.BadRequest();
    var challenge = await challenges.ConsumeAsync(req.Nonce, ct);
    if (challenge is null || challenge.Scheme != "passkey") return Results.Unauthorized();

    var stored = await credentials.GetAsync("passkey", req.CredentialId, ct);
    if (stored?.PublicKey is null) return Results.Unauthorized();
    if (!scheme.VerifyAssertion(clientData, authData, sig, stored.PublicKey, challenge)) return Results.Unauthorized();
    var session = await authsvc.AuthenticateAsync(stored, req.Device, ct);
    return session is null ? Results.Unauthorized() : Results.Ok(new TokenResponse(session.Token, session.ExpiresAt));
});

auth.MapDelete("/session", async (HttpContext http, ISessionStore sessions, CancellationToken ct) =>
{
    var token = BearerAuthFilter.ExtractBearer(http.Request)!; // filter guarantees non-null
    await sessions.RevokeAsync(token, ct);
    return Results.NoContent();
}).AddEndpointFilter<BearerAuthFilter>();

// ---- bucket ----
var bucket = app.MapGroup("/v1/bucket").AddEndpointFilter<BearerAuthFilter>();
bucket.MapGet("/head", async (HttpContext http, IBucketStore store, CancellationToken ct) =>
{
    var b = (string)http.Items[BearerAuthFilter.BucketIdItem]!;
    var head = await store.GetHeadAsync(b, ct);
    return Results.Ok(new HeadResponse(head.CurrentSeq, head.ContentHash));
});
bucket.MapPost("/get", async (HttpContext http, GetRequest req, IBucketStore store, CancellationToken ct) =>
{
    var b = (string)http.Items[BearerAuthFilter.BucketIdItem]!;
    var entries = await store.GetBatchAsync(b, req.Keys, ct);
    return Results.Ok(new EntriesResponse(entries.Select(EntryDto.From).ToList()));
});
bucket.MapPost("/commit", async (HttpContext http, CommitRequest req, SyncService sync, CancellationToken ct) =>
{
    var b = (string)http.Items[BearerAuthFilter.BucketIdItem]!;
    CommitResult result;
    try { result = await sync.CommitAsync(b, req.Ops.Select(o => o.ToWriteOp()).ToList(), ct); }
    catch (Exception ex) when (ex is ArgumentException or FormatException) { return Results.BadRequest(); }
    var body = CommitResponse.From(result);
    return result.Committed
        ? Results.Ok(body)
        : Results.Json(body, AppJsonContext.Default.CommitResponse, statusCode: StatusCodes.Status409Conflict);
});
bucket.MapGet("/diff", async (HttpContext http, long since, int? limit, IBucketStore store, CancellationToken ct) =>
{
    var b = (string)http.Items[BearerAuthFilter.BucketIdItem]!;
    var page = await store.DiffAsync(b, since, limit ?? 100, ct);
    return Results.Ok(new DiffResponse(page.Entries.Select(EntryDto.From).ToList(), page.NextSeq, page.HasMore));
});
// Time-based audit query (approximate — NOT a sync cursor; use /diff with the seq cursor for sync).
bucket.MapGet("/changes", async (HttpContext http, DateTimeOffset since, int? limit, IBucketStore store, CancellationToken ct) =>
{
    var b = (string)http.Items[BearerAuthFilter.BucketIdItem]!;
    var page = await store.ChangesSinceAsync(b, since, limit ?? 100, ct);
    return Results.Ok(new ChangesResponse(page.Entries.Select(EntryDto.From).ToList(), page.NextSince, page.HasMore));
});
bucket.MapGet("/stream", async (HttpContext http, SyncService sync, CancellationToken ct) =>
{
    var b = (string)http.Items[BearerAuthFilter.BucketIdItem]!;
    long since = ParseLastEventId(http.Request);
    http.Response.Headers.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";
    http.Response.Headers["X-Accel-Buffering"] = "no";
    await http.Response.StartAsync(ct); // flush headers now so the client connects & we subscribe promptly
    try
    {
        await foreach (var seq in sync.StreamAsync(b, since, ct))
        {
            await http.Response.WriteAsync($"id: {seq}\ndata: {seq}\n\n", ct);
            await http.Response.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException) { /* client disconnected */ }
});

app.Run();

// Consume the challenge, verify the BIP-340 proof, and yield the credential (or null on any failure).
static async Task<VerifiedCredential?> VerifySchnorr(SchnorrAuthRequest req, IChallengeStore challenges, SchnorrAuthScheme scheme, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(req.Pubkey) || string.IsNullOrWhiteSpace(req.Nonce) || string.IsNullOrWhiteSpace(req.Signature))
        return null;
    var challenge = await challenges.ConsumeAsync(req.Nonce, ct);
    if (challenge is null || challenge.Scheme != "schnorr") return null;
    byte[] sig;
    try { sig = Convert.FromHexString(req.Signature); }
    catch (FormatException) { return null; }
    return scheme.Verify(req.Pubkey, challenge, sig);
}

// Optional bearer: the bucket id of a valid session, or null if absent/invalid (used by register).
static async Task<string?> BucketFromBearer(HttpContext http, ISessionStore sessions)
{
    var token = BearerAuthFilter.ExtractBearer(http.Request);
    if (token is null) return null;
    var session = await sessions.ValidateAsync(token, http.RequestAborted);
    return session?.BucketId;
}

static long ParseLastEventId(HttpRequest req)
{
    if (long.TryParse(req.Headers["Last-Event-ID"].ToString(), out var v)) return v;
    if (long.TryParse(req.Query["since"], out var q)) return q;
    return 0;
}

public partial class Program { }
