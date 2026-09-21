using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using NBitcoin;
using Org.BouncyCastle.Crypto.Digests;

namespace Umbrella.Wallet.Core.Derivation;

/// <summary>
/// Cardano (Shelley) base-address derivation from a BIP39 phrase, using the Icarus / CIP-1852
/// scheme: PBKDF2 master key → BIP32-Ed25519 (V2) derivation → blake2b-224 key hashes → bech32.
///
/// This is Cardano's own bespoke elliptic-curve derivation (not SLIP-0010), so every stage is
/// pinned in <c>AdaKeysTests</c> against the reference produced by Emurgo's
/// cardano-serialization-lib — a single wrong byte changes the address and would strand funds.
/// Only the receive address is produced; ADA transaction building is not implemented, so ADA is
/// exposed as receive-only, recoverable from the same phrase in any CIP-1852 wallet (Eternl, etc.).
/// </summary>
public static class AdaKeys
{
    private static readonly MethodInfo ScalarMultBaseEncoded =
        typeof(Org.BouncyCastle.Math.EC.Rfc8032.Ed25519).GetMethod(
            "ScalarMultBaseEncoded",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
            null,
            [typeof(byte[]), typeof(byte[]), typeof(int)],
            null)
        ?? throw new InvalidOperationException(
            "BouncyCastle no longer exposes Ed25519.ScalarMultBaseEncoded — Cardano support must be re-verified.");

    private const uint Hardened = 0x80000000;

    /// <summary>Mainnet base address for the account-0 payment/stake keys of this phrase.</summary>
    public static string BaseAddress(string mnemonic, bool mainnet = true)
    {
        var master = MasterKey(EntropyFromMnemonic(mnemonic));
        // m / 1852' / 1815' / 0'
        var account = Derive(Derive(Derive(master, 1852 | Hardened), 1815 | Hardened), 0 | Hardened);
        var payment = PublicKey(Derive(Derive(account, 0), 0)); // .../0/0 external payment key
        var stake = PublicKey(Derive(Derive(account, 2), 0));    // .../2/0 stake key

        // Shelley base address: header (type 0 base | network) | blake2b224(payment) | blake2b224(stake).
        var addr = new byte[57];
        addr[0] = (byte)(mainnet ? 0x01 : 0x00);
        Blake2b224(payment).CopyTo(addr, 1);
        Blake2b224(stake).CopyTo(addr, 29);
        return Bech32Encode("addr", addr);
    }

    /// <summary>The extended payment private key (m/1852'/1815'/0'/0/0, 96 bytes) for signing.</summary>
    public static byte[] PaymentKey(string mnemonic)
    {
        var master = MasterKey(EntropyFromMnemonic(mnemonic));
        var account = Derive(Derive(Derive(master, 1852 | Hardened), 1815 | Hardened), 0 | Hardened);
        return Derive(Derive(account, 0), 0);
    }

    /// <summary>Icarus master key from BIP39 entropy: PBKDF2-HMAC-SHA512, then the ed25519 clamp.</summary>
    public static byte[] MasterKey(byte[] entropy)
    {
        var xprv = Rfc2898DeriveBytes.Pbkdf2(
            Array.Empty<byte>(), entropy, 4096, HashAlgorithmName.SHA512, 96);
        xprv[0] &= 0xF8;
        xprv[31] &= 0x1F;
        xprv[31] |= 0x40;
        return xprv; // kL(32) | kR(32) | chaincode(32)
    }

    /// <summary>One BIP32-Ed25519 (V2) private-derivation step, hardened when index ≥ 2^31.</summary>
    public static byte[] Derive(byte[] xprv, uint index)
    {
        var kL = xprv[..32];
        var kR = xprv[32..64];
        var cc = xprv[64..96];
        var idx = new[] { (byte)index, (byte)(index >> 8), (byte)(index >> 16), (byte)(index >> 24) };

        byte[] zData, cData;
        if (index >= Hardened)
        {
            zData = Concat(new byte[] { 0x00 }, kL, kR, idx);
            cData = Concat(new byte[] { 0x01 }, kL, kR, idx);
        }
        else
        {
            var a = PublicKey(xprv);
            zData = Concat(new byte[] { 0x02 }, a, idx);
            cData = Concat(new byte[] { 0x03 }, a, idx);
        }

        var z = Hmac512(cc, zData);
        var childCc = Hmac512(cc, cData)[32..64];

        // kL' = kL + 8·ZL (ZL = first 28 bytes of Z); kR' = (kR + ZR) mod 2^256 (ZR = Z[32..64]).
        var kLnew = ToLe32(LeInt(kL) + LeInt(z[..28]) * 8);
        var kRnew = ToLe32(LeInt(kR) + LeInt(z[32..64]));

        return Concat(kLnew, kRnew, childCc);
    }

    /// <summary>The ed25519 public key of an extended private key: [kL]·B, 32 bytes.</summary>
    public static byte[] PublicKey(byte[] xprv) => ScalarMultBase(xprv[..32]);

    /// <summary>[scalar]·B on ed25519, encoded to 32 bytes. <paramref name="scalar32"/> is little-endian.
    /// Used both for public keys ([kL]·B) and the signature nonce point ([r]·B).</summary>
    public static byte[] ScalarMultBase(byte[] scalar32)
    {
        var result = new byte[32];
        ScalarMultBaseEncoded.Invoke(null, [scalar32, result, 0]);
        return result;
    }

    private static byte[] Blake2b224(byte[] data)
    {
        var digest = new Blake2bDigest(224);
        digest.BlockUpdate(data, 0, data.Length);
        var hash = new byte[28];
        digest.DoFinal(hash, 0);
        return hash;
    }

    private static byte[] Hmac512(byte[] key, byte[] data)
    {
        using var hmac = new HMACSHA512(key);
        return hmac.ComputeHash(data);
    }

    /// <summary>BIP39 entropy from a mnemonic: reverse the wordlist (11 bits/word), drop the checksum.</summary>
    public static byte[] EntropyFromMnemonic(string mnemonic)
    {
        var words = mnemonic.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wordlist = Wordlist.English;
        var bits = new bool[words.Length * 11];
        for (var i = 0; i < words.Length; i++)
        {
            if (!wordlist.WordExists(words[i], out var index))
                throw new ArgumentException($"'{words[i]}' is not a BIP39 word.", nameof(mnemonic));
            for (var b = 0; b < 11; b++)
                bits[(i * 11) + b] = ((index >> (10 - b)) & 1) == 1;
        }

        var entropyBits = words.Length * 11 * 32 / 33; // strip the checksum (1 bit per 32 entropy bits)
        var entropy = new byte[entropyBits / 8];
        for (var i = 0; i < entropyBits; i++)
            if (bits[i]) entropy[i / 8] |= (byte)(1 << (7 - (i % 8)));
        return entropy;
    }

    // --- little-endian bigint helpers (BIP32-Ed25519 keeps scalars as 256-bit LE) ---
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
        var total = parts.Sum(p => p.Length);
        var result = new byte[total];
        var offset = 0;
        foreach (var p in parts)
        {
            p.CopyTo(result, offset);
            offset += p.Length;
        }

        return result;
    }

    // --- bech32 (BIP173, as used by Cardano Shelley addresses) — one shared, verified codec ---
    private static string Bech32Encode(string hrp, byte[] data) => Codecs.Bech32.Encode(hrp, data);
}
