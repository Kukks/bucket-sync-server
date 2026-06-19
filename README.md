# wallet-sync-server

Generic, end-to-end-encrypted backup / restore / sync server — a schema-agnostic, encrypted
key-value **bucket sync protocol**. Interface-driven C# (.NET 10). The server stores **opaque
ciphertext** only — it never reads value contents, so it stays agnostic to whatever a client stores.
See the design spec in `docs/superpowers/specs/` and the Phase 1 plan in `docs/superpowers/plans/`.

## Build & test

```bash
dotnet build
dotnet test                               # all projects (Postgres + e2e tests need Docker running)
dotnet test tests/BucketSync.Tests        # fast, in-memory only (no Docker)
```

## Run

In-memory backend (no database; state is lost on restart — for local dev only):

```bash
dotnet run --project src/BucketSync.Api
```

Postgres backend:

```bash
Backend=Postgres \
ConnectionStrings__Postgres="Host=localhost;Username=postgres;Password=postgres;Database=bucketsync" \
dotnet run --project src/BucketSync.Api
```

Migrations are applied automatically at startup when `Backend=Postgres`.

## Docker

Two images are provided. Both listen on `:8080`, run as a non-root user, and default to the
in-memory backend (`GET /health` → 200).

```bash
# Framework-dependent (fast to build)
docker build -t bucketsync .
docker run -p 8080:8080 bucketsync

# NativeAOT (self-contained native binary — smaller image, faster cold start; slower ILC build)
docker build -f Dockerfile.aot -t bucketsync-aot .
docker run -p 8080:8080 bucketsync-aot
```

Postgres backend (either image) — pass config as environment variables:

```bash
docker run -p 8080:8080 \
  -e Backend=Postgres \
  -e ConnectionStrings__Postgres="Host=host.docker.internal;Username=postgres;Password=postgres;Database=bucketsync" \
  bucketsync
```

CI builds and tests on every push and publishes the **NativeAOT** image to GHCR on `main`.

## Protocol (v1)

The full machine-readable spec is generated at build time to [`docs/openapi.json`](docs/openapi.json)
(OpenAPI 3.1). There is no runtime `/openapi` endpoint — runtime schema generation isn't
NativeAOT-safe, so the document is produced at build instead.

| Method | Route | Auth | Purpose |
|---|---|---|---|
| POST | `/v1/auth/challenge` | none | `{pubkey}` → `{nonce, expiresAt}` |
| POST | `/v1/auth/verify` | none | `{pubkey, nonce, signature, device?}` → `{token, expiresAt}`; provisions bucket (TOFU) |
| DELETE | `/v1/auth/session` | bearer | revoke current session |
| GET | `/v1/bucket/head` | bearer | → `{currentSeq, contentHash}` |
| POST | `/v1/bucket/get` | bearer | `{keys[]}` → `{entries[]}` |
| POST | `/v1/bucket/commit` | bearer | `{ops[]}` → `CommitResponse`; **409** on CAS conflict |
| GET | `/v1/bucket/diff?since=N&limit=M` | bearer | → `{entries[], nextSeq, hasMore}` (`limit` = max commits) |
| GET | `/v1/bucket/stream` | bearer | SSE; `Last-Event-ID: N` resumes; emits new `seq` values |

`value` fields are base64 in JSON and **opaque** to the server. A reference client-side envelope library (`src/BucketSync.Cse`, `CseV1Envelope.Seal/Open`) produces these `cse-v1` values; the server runtime never references it. The wire format is documented in [`docs/cse-v1.md`](docs/cse-v1.md) for non-C# SDKs.

### Auth signature scheme

Bearer auth is challenge → BIP-340 Schnorr signature. The signed 32-byte message is
`SHA-256( "bucket-sync:auth:v1" || nonceBytes )`. `pubkey` is the 32-byte x-only key (hex),
`signature` is 64-byte BIP-340 (hex). The server verifies with NBitcoin and derives
`bucketId = SHA-256(pubkey)` — never client-supplied.

### Client sync loop

`head` → if behind, `diff(since=cursor)` until caught up → open `stream` to tail →
on local change `commit`; on `409` re-`diff`, merge, retry.
