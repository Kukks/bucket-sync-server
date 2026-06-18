using Npgsql;
using WalletSync.Api;
using WalletSync.Auth;
using WalletSync.Core;
using WalletSync.Postgres;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IAuthenticator, SchnorrAuthenticator>();
builder.Services.AddSingleton<IChangeNotifier, InProcChangeNotifier>();
builder.Services.AddSingleton<SyncService>();

var backend = builder.Configuration["Backend"] ?? "InMemory";
if (string.Equals(backend, "Postgres", StringComparison.OrdinalIgnoreCase))
{
    var cs = builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres required when Backend=Postgres");
    builder.Services.AddSingleton(NpgsqlDataSource.Create(cs));
    builder.Services.AddSingleton<IBucketStore, PostgresBucketStore>();
    builder.Services.AddSingleton<IChallengeStore, PostgresChallengeStore>();
    builder.Services.AddSingleton<ISessionStore, PostgresSessionStore>();
}
else
{
    builder.Services.AddSingleton<IBucketStore, InMemoryBucketStore>();
    builder.Services.AddSingleton<IChallengeStore, InMemoryChallengeStore>();
    builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();
}

var app = builder.Build();

if (string.Equals(backend, "Postgres", StringComparison.OrdinalIgnoreCase))
    await Migrations.ApplyAsync(app.Services.GetRequiredService<NpgsqlDataSource>());

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// ---- auth (Task 19) ----
var auth = app.MapGroup("/v1/auth");
auth.MapPost("/challenge", async (ChallengeRequest req, IChallengeStore challenges, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Pubkey)) return Results.BadRequest();
    var c = await challenges.IssueAsync(req.Pubkey, ct);
    return Results.Ok(new ChallengeResponse(c.Nonce, c.ExpiresAt));
});
auth.MapPost("/verify", async (VerifyRequest req, IChallengeStore challenges, IAuthenticator authenticator,
    ISessionStore sessions, IBucketStore store, CancellationToken ct) =>
{
    var challenge = await challenges.ConsumeAsync(req.Nonce, ct);
    if (challenge is null || !string.Equals(challenge.Pubkey, req.Pubkey, StringComparison.Ordinal))
        return Results.Unauthorized();

    byte[] sig;
    try { sig = Convert.FromHexString(req.Signature); }
    catch (FormatException) { return Results.Unauthorized(); }

    var principal = await authenticator.VerifyAsync(challenge, sig, ct);
    if (principal is null) return Results.Unauthorized();

    await store.EnsureBucketAsync(principal.BucketId, principal.Pubkey, ct); // TOFU provision
    var session = await sessions.CreateAsync(principal, req.Device, ct);
    return Results.Ok(new VerifyResponse(session.Token, session.ExpiresAt));
});
auth.MapDelete("/session", async (HttpContext http, ISessionStore sessions, CancellationToken ct) =>
{
    var token = BearerAuthFilter.ExtractBearer(http.Request)!; // filter guarantees non-null
    await sessions.RevokeAsync(token, ct);
    return Results.NoContent();
}).AddEndpointFilter<BearerAuthFilter>();

// ---- bucket (Tasks 21–22) ----
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
        : Results.Json(body, statusCode: StatusCodes.Status409Conflict);
});
bucket.MapGet("/diff", async (HttpContext http, long since, int? limit, IBucketStore store, CancellationToken ct) =>
{
    var b = (string)http.Items[BearerAuthFilter.BucketIdItem]!;
    var page = await store.DiffAsync(b, since, limit ?? 100, ct);
    return Results.Ok(new DiffResponse(page.Entries.Select(EntryDto.From).ToList(), page.NextSeq, page.HasMore));
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

static long ParseLastEventId(HttpRequest req)
{
    if (long.TryParse(req.Headers["Last-Event-ID"].ToString(), out var v)) return v;
    if (long.TryParse(req.Query["since"], out var q)) return q;
    return 0;
}

public partial class Program { }
