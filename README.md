# wallet-sync-server

Generic, end-to-end-encrypted backup / restore / sync server for Arkade (Ark) Bitcoin wallets.
Interface-driven C# (.NET 10). The server stores **opaque ciphertext** only — it never reads value contents.
See the design spec in `docs/superpowers/specs/` and the Phase 1 plan in `docs/superpowers/plans/`.

## Build & test

```bash
dotnet build
dotnet test                               # all projects (Postgres + e2e tests need Docker running)
dotnet test tests/WalletSync.Tests        # fast, in-memory only (no Docker)
```

## Run

In-memory backend (no database; state is lost on restart — for local dev only):

```bash
dotnet run --project src/WalletSync.Api
```

Postgres backend:

```bash
Backend=Postgres \
ConnectionStrings__Postgres="Host=localhost;Username=postgres;Password=postgres;Database=walletsync" \
dotnet run --project src/WalletSync.Api
```

Migrations are applied automatically at startup when `Backend=Postgres`.

## Protocol (v1)

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

`value` fields are base64 in JSON and **opaque** to the server. A reference client-side envelope library (`src/WalletSync.Cse`, `CseV1Envelope.Seal/Open`) produces these `cse-v1` values; the server runtime never references it.

### Auth signature scheme

Bearer auth is challenge → BIP-340 Schnorr signature. The signed 32-byte message is
`SHA-256( "arkade-wallet-sync:auth:v1" || nonceBytes )`. `pubkey` is the 32-byte x-only key (hex),
`signature` is 64-byte BIP-340 (hex). The server verifies with NBitcoin and derives
`bucketId = SHA-256(pubkey)` — never client-supplied.

### Client sync loop

`head` → if behind, `diff(since=cursor)` until caught up → open `stream` to tail →
on local change `commit`; on `409` re-`diff`, merge, retry.
