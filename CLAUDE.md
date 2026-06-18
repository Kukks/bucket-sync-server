# wallet-sync-server

Generic, end-to-end-encrypted backup / restore / sync server for **Arkade (Ark) Bitcoin wallets**. Interface-driven C#. The server only ever stores **opaque ciphertext** — it never reads value contents.

## Source of truth

The design spec is authoritative — **read it before any work**:
`docs/superpowers/specs/2026-06-17-wallet-sync-server-design.md`

## Status

Design **and Phase 1 plan complete**. Authoritative plan: `docs/superpowers/plans/2026-06-17-wallet-sync-server-phase-1.md` (26 tasks — the 06-17 sync-engine plan with the `cse-v1` reference envelope + e2e grafted in; a duplicate 06-18 draft was discarded). **Execution is underway via `superpowers:subagent-driven-development`** (fresh subagent + review per task). Do NOT re-brainstorm or re-plan — the design and plan are settled. Progress is tracked in the SDD ledger (`.git/sdd/progress.md`); after any compaction, trust it and `git log` over recollection.

## Locked decisions (rationale in the spec)

- Optimistic **CAS per key** (plaintext `version`, ciphertext value) — **not** last-write-wins.
- Diff via **monotonic per-bucket sequence cursor** (doubles as SSE `Last-Event-ID`) + a content-hash for integrity audits.
- **One bucket per identity** (pubkey); keys namespaced by prefix.
- Auth: challenge → **BIP-340 Schnorr / secp256k1** signature → **opaque bearer + session store**; pluggable `IAuthenticator` (verify with NBitcoin).
- Transport: **REST/JSON + SSE** (cursor = `Last-Event-ID`).
- Batch: **all-or-nothing transaction** = one cursor bump = one SSE event.
- Backend: **Postgres** behind `IBucketStore` (advisory-lock-per-bucket commit); in-memory double for tests.
- Server is **schema-agnostic** (per-app buckets); each SDK client owns its own key scheme + value format.

## Scope

- **Phase 1 (build):** Postgres sync engine (CAS + cursor + atomic batch + SSE) + Schnorr challenge auth + `cse-v1` client-side-encrypted envelope.
- **Deferred (do NOT build now):** [`ArkLabsHQ/enclave`](https://github.com/ArkLabsHQ/enclave) TEE integration (AWS Nitro) + ECDH `ecdh-tee-v1` + external-auth recovery; multi-node SSE fan-out (Redis/NATS); cross-SDK canonical schema; rate-limiting on `/v1/auth/challenge`.
- **Rejected (do not reintroduce):** last-write-wins; the server exposing typed entity interfaces.

## Reference repos (siblings under `c:/git` — DO NOT modify)

Target clients: `arkade-os/wallet`, and SDKs `ts-sdk`, `dotnet-sdk` ("NArk", C# — the natural home for the C# sync client via its `IVtxoStorage`/`IWalletStorage` + `SetMetadataValue` cursor slot), `go-sdk`, `rust-sdk`. Identity is uniform across all SDKs (secp256k1 / BIP-340 Schnorr / BIP-32). **`bark` (gitlab `ark-bitcoin/bark`) is NOT a target** — it was only reference material.
