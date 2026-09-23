using System.Numerics;
using System.Security.Cryptography;

namespace Umbrella.Wallet.Core.Polkadot;

/// <summary>
/// sr25519 signatures — schnorrkel's Schnorr signatures on ristretto255, which is what a Polkadot
/// transaction from a Polkadot.js-style account is signed with (roadmap N.8, send).
///
/// Signing, as schnorrkel does it: the message goes into a Merlin transcript under the "substrate"
/// signing context; the public key and a nonce commitment R = r·B follow; the challenge k comes out of
/// the transcript; s = k·key + r. The nonce r is drawn from the transcript itself, rekeyed with the
/// key's secret nonce and fresh randomness — so it is bound to the message and the key, and a weak
/// random source alone cannot expose the key.
///
/// Signatures are randomized, so they cannot be pinned byte for byte. What is pinned is VERIFY, against
/// signatures Polkadot.js produced (<c>Sr25519Tests</c>) — a verifier that accepts those computes the
/// same transcript, challenge and group arithmetic the signer uses — and every signature made here is
/// verified before it is used.
/// </summary>
public static class Sr25519
{
    /// <summary>The signing context Substrate chains use for everything.</summary>
    public static readonly byte[] SubstrateContext = "substrate"u8.ToArray();

    /// <summary>An expanded sr25519 secret: the key scalar and the 32-byte nonce seed, and the public key.</summary>
    public sealed class Keypair : IDisposable
    {
        internal Keypair(BigInteger key, byte[] nonce, byte[] publicKey)
        {
            Key = key;
            Nonce = nonce;
            PublicKey = publicKey;
        }

        internal BigInteger Key { get; private set; }
        internal byte[] Nonce { get; }
        public byte[] PublicKey { get; }

        public void Dispose()
        {
            Key = BigInteger.Zero;
            CryptographicOperations.ZeroMemory(Nonce);
        }
    }

    /// <summary>schnorrkel <c>MiniSecretKey::expand_to_keypair(ExpansionMode::Ed25519)</c>.</summary>
    public static Keypair FromMiniSecret(byte[] miniSecret)
    {
        if (miniSecret.Length != 32) throw new ArgumentException("A mini secret is 32 bytes.", nameof(miniSecret));

        var h = SHA512.HashData(miniSecret);
        var key = h[..32];
        var nonce = h[32..];
        CryptographicOperations.ZeroMemory(h);

        key[0] &= 248;
        key[31] &= 63;
        key[31] |= 64;
        var scalar = new BigInteger(key, isUnsigned: true, isBigEndian: false) >> 3;   // divide by the cofactor
        CryptographicOperations.ZeroMemory(key);

        return new Keypair(BigInteger.Remainder(scalar, Ristretto255.Order), nonce, Ristretto255.EncodeBaseMultiple(scalar));
    }

    /// <summary>Signs a message under a context: R ‖ s, 64 bytes, with schnorrkel's marker bit set.</summary>
    public static byte[] Sign(Keypair pair, ReadOnlySpan<byte> message, byte[]? context = null, byte[]? randomness = null)
    {
        var t = SigningTranscript(context ?? SubstrateContext, message);
        t.AppendMessage("proto-name"u8, "Schnorr-sig"u8);
        t.AppendMessage("sign:pk"u8, pair.PublicKey);

        var witness = t.WitnessBytes("signing"u8, [pair.Nonce], 64, randomness);
        var r = WideScalar(witness);
        CryptographicOperations.ZeroMemory(witness);

        var bigR = Ristretto255.EncodeBaseMultiple(r);
        t.AppendMessage("sign:R"u8, bigR);
        var k = WideScalar(t.ChallengeBytes("sign:c"u8, 64));
        var s = BigInteger.Remainder((k * pair.Key) + r, Ristretto255.Order);

        var signature = new byte[64];
        bigR.CopyTo(signature, 0);
        s.ToByteArray(isUnsigned: true, isBigEndian: false).CopyTo(signature, 32);
        signature[63] |= 0x80;
        return signature;
    }

    /// <summary>schnorrkel's verify: the marker bit, a canonical s, and s·B − k·A == R.</summary>
    public static bool Verify(ReadOnlySpan<byte> signature, ReadOnlySpan<byte> message, ReadOnlySpan<byte> publicKey, byte[]? context = null)
    {
        if (signature.Length != 64 || publicKey.Length != 32) return false;
        if ((signature[63] & 0x80) == 0) return false;   // not a schnorrkel signature

        var sBytes = signature[32..].ToArray();
        sBytes[31] &= 0x7F;
        var s = new BigInteger(sBytes, isUnsigned: true, isBigEndian: false);
        if (s >= Ristretto255.Order) return false;
        if (!Ristretto255.IsValidEncoding(signature[..32])) return false;

        var t = SigningTranscript(context ?? SubstrateContext, message);
        t.AppendMessage("proto-name"u8, "Schnorr-sig"u8);
        t.AppendMessage("sign:pk"u8, publicKey);
        t.AppendMessage("sign:R"u8, signature[..32]);
        var k = WideScalar(t.ChallengeBytes("sign:c"u8, 64));

        var computed = Ristretto255.EncodeBaseMinusMultiple(s, k, publicKey);
        return computed is not null && signature[..32].SequenceEqual(computed);
    }

    /// <summary>schnorrkel <c>signing_context(ctx).bytes(msg)</c>.</summary>
    private static MerlinTranscript SigningTranscript(byte[] context, ReadOnlySpan<byte> message)
    {
        var t = new MerlinTranscript("SigningContext");
        t.AppendMessage(""u8, context);
        t.AppendMessage("sign-bytes"u8, message);
        return t;
    }

    /// <summary>64 bytes as a scalar mod ℓ (curve25519-dalek <c>from_bytes_mod_order_wide</c>).</summary>
    private static BigInteger WideScalar(byte[] bytes) =>
        BigInteger.Remainder(new BigInteger(bytes, isUnsigned: true, isBigEndian: false), Ristretto255.Order);
}
