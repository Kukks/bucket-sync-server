using System.Security.Cryptography;
using System.Text;
using WalletSync.Cse;

namespace WalletSync.Tests;

public class CseV1EnvelopeTests
{
    private static byte[] Kwk() => RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void Seal_then_Open_round_trips()
    {
        var kwk = Kwk();
        var plaintext = Encoding.UTF8.GetBytes("""{"vtxo":"abc","amount":1000}""");
        var envelope = CseV1Envelope.Seal(plaintext, kwk);

        Assert.NotEqual(plaintext, envelope);
        Assert.Equal(plaintext, CseV1Envelope.Open(envelope, kwk));
    }

    [Fact]
    public void Open_with_wrong_key_throws()
    {
        var envelope = CseV1Envelope.Seal(Encoding.UTF8.GetBytes("secret"), Kwk());
        Assert.ThrowsAny<CryptographicException>(() => CseV1Envelope.Open(envelope, Kwk()));
    }

    [Fact]
    public void Tampered_ciphertext_throws()
    {
        var kwk = Kwk();
        var envelope = CseV1Envelope.Seal(Encoding.UTF8.GetBytes("secret"), kwk);
        envelope[^1] ^= 0xFF;
        Assert.ThrowsAny<Exception>(() => CseV1Envelope.Open(envelope, kwk));
    }

    [Fact]
    public void Envelope_advertises_scheme()
    {
        var json = Encoding.UTF8.GetString(CseV1Envelope.Seal(Encoding.UTF8.GetBytes("x"), Kwk()));
        Assert.Contains("cse-v1", json);
    }
}
