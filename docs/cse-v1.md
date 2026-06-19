# `cse-v1` envelope format

Client-side encryption for bucket values. The server stores the envelope **opaquely** and never
decrypts it — it only reads the entry's `scheme` tag (`"cse-v1"`). This doc is the wire format so
non-C# clients can interoperate. Reference implementation: `src/WalletSync.Cse/CseV1Envelope.cs`.

## TL;DR

- A random **per-record DEK** (32-byte AES-256 key) encrypts the value with **AES-256-GCM**.
- The DEK is **wrapped** (also AES-256-GCM) to one or more **recipients**. Phase 1 = one `owner`
  recipient, wrapped under the client's 32-byte **key-wrapping key (KWK)**.
- The **KWK is NOT the BIP-340 signing key** — derive a separate key from the seed (spec §7).
- The envelope is a JSON object, UTF-8 encoded. That UTF-8 byte string is the value the client
  base64-encodes into a commit op's `value` field. (So the API `value` is `base64(envelope-JSON-bytes)`.)
- GCM auth tags are **16 bytes**; all nonces/IVs are **12 bytes**. **No AAD** is used.
- All `byte[]` fields below are **standard base64 strings** (RFC 4648, with `=` padding — not base64url).

## JSON shape

```json
{
  "v": "cse-v1",
  "alg": "AES-256-GCM",
  "recipients": [
    { "type": "owner", "wrappedDek": "<b64>", "nonce": "<b64>", "tag": "<b64>" }
  ],
  "iv": "<b64>",
  "ct": "<b64>",
  "tag": "<b64>"
}
```

| Field | Type | Bytes | Meaning |
|---|---|---|---|
| `v` | string | — | Scheme tag, always `"cse-v1"`. |
| `alg` | string | — | `"AES-256-GCM"` (data + key-wrap algorithm). |
| `recipients[]` | array | — | DEK wrapped per recipient. Phase 1: exactly one, `type:"owner"`. |
| `recipients[].type` | string | — | `"owner"` now; a TEE recipient is the Phase-2 (`ecdh-tee-v1`) seam. |
| `recipients[].wrappedDek` | b64 | 32 | DEK encrypted under the KWK. |
| `recipients[].nonce` | b64 | 12 | GCM nonce for the DEK wrap. |
| `recipients[].tag` | b64 | 16 | GCM tag for the DEK wrap. |
| `iv` | b64 | 12 | GCM nonce for the data encryption. |
| `ct` | b64 | = plaintext len | Ciphertext (GCM is a stream cipher: same length as plaintext). |
| `tag` | b64 | 16 | GCM tag for the data encryption. |

## Seal (plaintext + KWK → envelope)

1. `dek = CSPRNG(32)`; `iv = CSPRNG(12)`.
2. `ct, tag = AES-256-GCM.Encrypt(key=dek, nonce=iv, plaintext, aad=none)` → `ct` (= plaintext length), `tag` (16).
3. `wrapNonce = CSPRNG(12)`.
4. `wrappedDek, wrapTag = AES-256-GCM.Encrypt(key=KWK, nonce=wrapNonce, plaintext=dek, aad=none)` → `wrappedDek` (32), `wrapTag` (16).
5. Assemble the JSON above with `recipients=[{type:"owner", wrappedDek, nonce:wrapNonce, tag:wrapTag}]`, `iv`, `ct`, `tag`.
6. UTF-8 encode the JSON → that byte string is the envelope. (Zero the DEK from memory after use.)

## Open (envelope + KWK → plaintext)

1. Parse JSON; require `v == "cse-v1"` (reject otherwise).
2. Pick the recipient with `type == "owner"`.
3. `dek = AES-256-GCM.Decrypt(key=KWK, nonce=recipient.nonce, ciphertext=recipient.wrappedDek, tag=recipient.tag)` — **throws on a wrong key or tampering** (GCM auth).
4. `plaintext = AES-256-GCM.Decrypt(key=dek, nonce=iv, ciphertext=ct, tag=tag)` — throws on tampering.
5. Return `plaintext`. (Zero the DEK.)

## Notes & guarantees

- **Integrity / tamper-evidence** comes from GCM auth tags — flipping any ciphertext byte makes `Open` throw. There is no separate signature on the envelope.
- **No AAD**: `cse-v1` does not bind the bucket key/version into the GCM AAD. A future scheme could; clients must not assume it.
- **DEK granularity** (per-record vs per-bucket) is a client choice — the server is agnostic; it only ever sees the opaque bytes.
- **Forward path**: `ecdh-tee-v1` (Phase 2) adds a second `recipients[]` entry (the enclave's ECDH-wrapped DEK) so recovery is possible without the seed; the record shape doesn't change, so the server needs no migration.
