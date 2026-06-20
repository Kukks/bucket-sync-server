# bucket-sync-server

Generic, end-to-end-encrypted backup / restore / sync server — a schema-agnostic, multi-device, encrypted key-value **bucket sync protocol**. Interface-driven C#. The server only ever stores **opaque ciphertext** — it never reads value contents, so it stays agnostic to whatever any client chooses to store.

## Source of truth

The design spec is authoritative — **read it before any work**:
`docs/superpowers/specs/2026-06-17-bucket-sync-server-design.md`

*Historical note: the spec is written against the original motivating use case (encrypted
Bitcoin-wallet state sync). The protocol and server are fully generic — opaque buckets, no
domain-specific types — so treat that wallet framing as **one example client**, not a coupling.*

## Status

Phase 1 **implemented** (see `docs/superpowers/plans/2026-06-17-bucket-sync-server-phase-1.md`):
Postgres + in-memory sync engine (CAS + cursor + atomic batch + SSE), **pluggable auth schemes**
(schnorr + passkey, behind `IAuthScheme`), `cse-v1` opaque envelopes. All tests green. Phase 2
(TEE/`ecdh-tee-v1` recovery) remains deferred behind the existing `IAuthScheme` / envelope-scheme seams.

**Build & deploy (added after Phase 1, on `main`):** `Dockerfile` (framework-dependent) and
`Dockerfile.aot` (NativeAOT — the server IS AOT-compatible via `CreateSlimBuilder` + the Request
Delegate Generator + a source-generated `JsonSerializerContext`); GitHub Actions CI
(`.github/workflows/ci.yml`) builds/tests and publishes the AOT image to GHCR on `main`.
**Reproducible build:** per-project `packages.lock.json` (locked-mode in CI), exact SDK pin
(`global.json`), digest-pinned base images, deterministic IL — verified byte-identical across clean
rebuilds. Full suite **118/0** (Docker required for the Postgres/e2e tests). Pushed to
**`github.com/Kukks/bucket-sync-server`** (public; CI green). Deferred-by-choice (YAGNI, not blocking):
incremental bucket content-hash, `challenges` cleanup/index, `last_seen` write throttling.

## Locked decisions (rationale in the spec)

- Optimistic **CAS per key** (plaintext `version`, ciphertext value) — **not** last-write-wins.
- Diff via **monotonic per-bucket sequence cursor** (doubles as SSE `Last-Event-ID`) + a content-hash for integrity audits.
- A bucket is reachable by a **set of credentials** of any scheme (a `(scheme, credentialId)` → bucket registry); `bucketId` is a random opaque id, never client-supplied. Keys namespaced by prefix.
- Auth: **pluggable schemes** (`IAuthScheme` + `VerifiedCredential` + `AuthService`) → **opaque bearer + session store**. Ships **schnorr** (BIP-340/secp256k1 via NBitcoin) + **passkey** (WebAuthn ES256, hand-rolled; `System.Formats.Cbor`). `register` creates-or-adds a credential; `verify` authenticates.
- Transport: **REST/JSON + SSE** (cursor = `Last-Event-ID`).
- Batch: **all-or-nothing transaction** = one cursor bump = one SSE event.
- Backend: **Postgres** behind `IBucketStore` (advisory-lock-per-bucket commit); in-memory double for tests.
- Server is **schema-agnostic** (per-app buckets); each client owns its own key scheme + value format.

## Scope

- **Phase 1 (build):** Postgres sync engine (CAS + cursor + atomic batch + SSE) + Schnorr challenge auth + `cse-v1` client-side-encrypted envelope.
- **Deferred (do NOT build now):** [`ArkLabsHQ/enclave`](https://github.com/ArkLabsHQ/enclave) TEE integration (AWS Nitro) + ECDH `ecdh-tee-v1` + external-auth recovery; multi-node SSE fan-out (Redis/NATS); cross-client canonical schema; rate-limiting on `/v1/auth/challenge`.
- **Rejected (do not reintroduce):** last-write-wins; the server exposing typed entity interfaces.

## Clients

Any app that adopts the protocol is a client; the server is schema-agnostic and never sees plaintext. Client **identity is uniform**: secp256k1 / BIP-340 Schnorr / BIP-32. Each client owns its own key namespace + value format and runs the client-side envelope (reference: `docs/cse-v1.md`, `src/BucketSync.Cse`). Any sibling reference repos under `c:/git` are **read-only — DO NOT modify**.
