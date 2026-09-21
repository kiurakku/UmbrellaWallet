using System.Numerics;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Digests;
using Umbrella.Wallet.Core.Codecs;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Core.Cardano;

/// <summary>
/// Builds and signs a Cardano (Shelley) payment transaction: the CBOR transaction body, the
/// BIP32-Ed25519 signature over its blake2b-256 hash, and the assembled signed transaction ready to
/// submit. ADA uses an extended-key ed25519 (the scalar and nonce prefix come from the BIP32 key, not
/// from hashing a seed), so signing is implemented from the group operations rather than a stock
/// ed25519 signer.
///
/// Every step is pinned in <c>AdaTransferTests</c> to vectors from Emurgo's cardano-serialization-lib
/// for a fixed key and transaction — the body CBOR, its hash, the signature and the final signed tx
/// all match byte-for-byte, so a single wrong byte fails the test before any ADA can move.
/// </summary>
public static class AdaTransfer
{
    /// <summary>The ed25519 group order L = 2^252 + 27742317777372353535851937790883648493.</summary>
    private static readonly BigInteger L =
        BigInteger.Parse("7237005577332262213973186563042994240857116359379907606001950938285454250989");

    public sealed record TxInput(byte[] TxHash, ulong Index);

    public sealed record TxOutput(byte[] Address, ulong Coin);

    /// <summary>
    /// CBOR transaction body: a definite-length map { 0: inputs, 1: outputs, 2: fee, 3: ttl }, matching
    /// the canonical key order cardano-serialization-lib emits.
    /// </summary>
    public static byte[] BuildBody(
        IReadOnlyList<TxInput> inputs, IReadOnlyList<TxOutput> outputs, ulong fee, ulong ttl)
    {
        var w = new CborWriter();
        w.MapHeader(4);

        w.UInt(0); // inputs
        w.ArrayHeader(inputs.Count);
        foreach (var i in inputs)
        {
            w.ArrayHeader(2);
            w.Bytes(i.TxHash);
            w.UInt(i.Index);
        }

        w.UInt(1); // outputs
        w.ArrayHeader(outputs.Count);
        foreach (var o in outputs)
        {
            w.ArrayHeader(2);
            w.Bytes(o.Address);
            w.UInt(o.Coin);
        }

        w.UInt(2); // fee
        w.UInt(fee);
        w.UInt(3); // ttl
        w.UInt(ttl);

        return w.ToArray();
    }

    /// <summary>blake2b-256 of the transaction body — the message that is signed and the tx id.</summary>
    public static byte[] HashBody(byte[] body)
    {
        var digest = new Blake2bDigest(256);
        digest.BlockUpdate(body, 0, body.Length);
        var hash = new byte[32];
        digest.DoFinal(hash, 0);
        return hash;
    }

    /// <summary>
    /// BIP32-Ed25519 signature of a 32-byte message with a 96-byte extended key (kL | kR | chaincode).
    ///   r = H(kR ‖ M) mod L ;  R = [r]·B ;  k = H(R ‖ A ‖ M) mod L ;  S = (r + k·kL) mod L ;  sig = R ‖ S.
    /// </summary>
    public static byte[] Sign(byte[] extendedKey, byte[] message)
    {
        var kL = extendedKey[..32];
        var kR = extendedKey[32..64];
        var a = LeInt(kL);                                  // scalar (already clamped by MasterKey)
        var pub = AdaKeys.PublicKey(extendedKey);           // A = [kL]·B

        var r = LeInt(Sha512(Concat(kR, message))) % L;
        var rPoint = AdaKeys.ScalarMultBase(ToLe32(r));     // R = [r]·B
        var k = LeInt(Sha512(Concat(rPoint, pub, message))) % L;
        var s = (r + (k * a)) % L;                          // S = r + k·a mod L

        return Concat(rPoint, ToLe32(s));                   // 64-byte signature
    }

    /// <summary>
    /// Assembles the signed transaction: [ body, { 0: [[vkey, signature]] }, true, null ] — the
    /// four-element Alonzo+ form (body, witness set, is_valid, auxiliary data) submitted to the chain.
    /// </summary>
    public static byte[] BuildSignedTx(byte[] body, byte[] publicKey, byte[] signature)
    {
        var w = new CborWriter();
        w.ArrayHeader(4);
        w.Raw(body);                    // the exact body bytes that were hashed and signed
        w.MapHeader(1);                 // witness set
        w.UInt(0);                      // vkey witnesses
        w.ArrayHeader(1);
        w.ArrayHeader(2);
        w.Bytes(publicKey);
        w.Bytes(signature);
        w.Bool(true);                   // is_valid
        w.Null();                       // no auxiliary data
        return w.ToArray();
    }

    /// <summary>Cardano addresses are longer than BIP-173's 90 characters; CIP-5 tooling allows this.</summary>
    private const int MaxAddressLength = 1023;

    /// <summary>
    /// Decodes a mainnet Cardano address (addr1…) to its raw bytes (header ‖ key hashes), VERIFYING
    /// its bech32 checksum first.
    ///
    /// This used to drop the six checksum characters unread. A single mistyped character therefore
    /// decoded to different key hashes — a structurally valid address that no one holds — and the
    /// send path would have paid it. Now a typo is refused before anything is built.
    /// </summary>
    public static byte[] DecodeAddress(string bech32)
    {
        if (!Bech32.TryDecode(bech32?.Trim(), out var hrp, out var data, MaxAddressLength))
            throw new FormatException("That is not a valid Cardano address (the checksum does not match).");
        if (hrp != "addr")
            throw new FormatException("That is not a Cardano MAINNET address.");
        if (data.Length < 29)
            throw new FormatException("That Cardano address is too short to hold a key hash.");
        return data;
    }

    /// <summary>True for a mainnet Cardano address whose checksum is correct.</summary>
    public static bool IsValidAddress(string? address)
    {
        try { DecodeAddress(address ?? ""); return true; }
        catch (FormatException) { return false; }
    }

    // --- helpers ---------------------------------------------------------------------------------

    private static byte[] Sha512(byte[] data)
    {
        using var sha = SHA512.Create();
        return sha.ComputeHash(data);
    }

    private static BigInteger LeInt(byte[] le) => new(le, isUnsigned: true, isBigEndian: false);

    private static byte[] ToLe32(BigInteger value)
    {
        var raw = value.ToByteArray(isUnsigned: true, isBigEndian: false);
        var le = new byte[32];
        Array.Copy(raw, le, Math.Min(raw.Length, 32));
        return le;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var p in parts) { p.CopyTo(result, offset); offset += p.Length; }
        return result;
    }

    /// <summary>A minimal deterministic CBOR writer — just the definite-length items a Shelley tx needs.</summary>
    private sealed class CborWriter
    {
        private readonly List<byte> _b = [];

        public byte[] ToArray() => _b.ToArray();

        public void Raw(byte[] bytes) => _b.AddRange(bytes);

        public void Bool(bool value) => _b.Add(value ? (byte)0xF5 : (byte)0xF4);

        public void Null() => _b.Add(0xF6);

        public void UInt(ulong value) => Head(0, value);

        public void Bytes(byte[] value)
        {
            Head(2, (ulong)value.Length);
            _b.AddRange(value);
        }

        public void ArrayHeader(int count) => Head(4, (ulong)count);

        public void MapHeader(int count) => Head(5, (ulong)count);

        /// <summary>Writes a CBOR head: major type in the top 3 bits, minimal-length argument encoding.</summary>
        private void Head(int major, ulong value)
        {
            var m = (byte)(major << 5);
            if (value < 24)
            {
                _b.Add((byte)(m | (byte)value));
            }
            else if (value <= byte.MaxValue)
            {
                _b.Add((byte)(m | 24));
                _b.Add((byte)value);
            }
            else if (value <= ushort.MaxValue)
            {
                _b.Add((byte)(m | 25));
                _b.Add((byte)(value >> 8));
                _b.Add((byte)value);
            }
            else if (value <= uint.MaxValue)
            {
                _b.Add((byte)(m | 26));
                for (var i = 3; i >= 0; i--) _b.Add((byte)(value >> (8 * i)));
            }
            else
            {
                _b.Add((byte)(m | 27));
                for (var i = 7; i >= 0; i--) _b.Add((byte)(value >> (8 * i)));
            }
        }
    }
}
