using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WalletSync.Cse;

/// <summary>
/// cse-v1 client-side encryption envelope (reference implementation).
/// Data is sealed under a random per-record DEK (AES-256-GCM); the DEK is wrapped
/// (also AES-256-GCM) to one or more recipients. Phase 1 wraps to the wallet key only.
/// The whole envelope is JSON, UTF-8 encoded — opaque to the server, which reads only the Scheme tag.
/// </summary>
public static class CseV1Envelope
{
    public const string Scheme = "cse-v1";

    private sealed record Recipient(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("wrappedDek")] byte[] WrappedDek,
        [property: JsonPropertyName("nonce")] byte[] Nonce,
        [property: JsonPropertyName("tag")] byte[] Tag);

    private sealed record Envelope(
        [property: JsonPropertyName("v")] string V,
        [property: JsonPropertyName("alg")] string Alg,
        [property: JsonPropertyName("recipients")] List<Recipient> Recipients,
        [property: JsonPropertyName("iv")] byte[] Iv,
        [property: JsonPropertyName("ct")] byte[] Ct,
        [property: JsonPropertyName("tag")] byte[] Tag);

    public static byte[] Seal(byte[] plaintext, byte[] keyWrappingKey)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        if (keyWrappingKey is not { Length: 32 })
            throw new ArgumentException("key-wrapping key must be 32 bytes", nameof(keyWrappingKey));

        var dek = RandomNumberGenerator.GetBytes(32);
        try
        {
            var iv = RandomNumberGenerator.GetBytes(12);
            var ct = new byte[plaintext.Length];
            var dataTag = new byte[16];
            using (var gcm = new AesGcm(dek, 16))
                gcm.Encrypt(iv, plaintext, ct, dataTag);

            var wrapNonce = RandomNumberGenerator.GetBytes(12);
            var wrappedDek = new byte[dek.Length];
            var wrapTag = new byte[16];
            using (var gcm = new AesGcm(keyWrappingKey, 16))
                gcm.Encrypt(wrapNonce, dek, wrappedDek, wrapTag);

            var envelope = new Envelope(
                V: Scheme, Alg: "AES-256-GCM",
                Recipients: new List<Recipient> { new("wallet", wrappedDek, wrapNonce, wrapTag) },
                Iv: iv, Ct: ct, Tag: dataTag);

            return JsonSerializer.SerializeToUtf8Bytes(envelope);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    public static byte[] Open(byte[] envelopeBytes, byte[] keyWrappingKey)
    {
        ArgumentNullException.ThrowIfNull(envelopeBytes);
        var envelope = JsonSerializer.Deserialize<Envelope>(envelopeBytes)
                       ?? throw new CryptographicException("malformed cse-v1 envelope");
        if (envelope.V != Scheme)
            throw new CryptographicException($"unexpected scheme: {envelope.V}");

        var recipient = envelope.Recipients.FirstOrDefault(r => r.Type == "wallet")
                        ?? throw new CryptographicException("no wallet recipient");

        var dek = new byte[32]; // DEK is always 32 bytes; AesGcm rejects a wrong-size key if the envelope is malformed
        try
        {
            using (var gcm = new AesGcm(keyWrappingKey, 16))
                gcm.Decrypt(recipient.Nonce, recipient.WrappedDek, recipient.Tag, dek); // throws on wrong key

            var plaintext = new byte[envelope.Ct.Length];
            using (var gcm = new AesGcm(dek, 16))
                gcm.Decrypt(envelope.Iv, envelope.Ct, envelope.Tag, plaintext);
            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }
}
