using System.Numerics;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Digests;

namespace Umbrella.Wallet.Core.Polkadot;

/// <summary>
/// Polkadot's key derivation, the way Polkadot.js, Talisman, SubWallet and Nova do it (roadmap N.8).
///
/// It is NOT BIP-44. substrate-bip39 feeds the phrase's ENTROPY (not its words) to PBKDF2 to get a
/// 32-byte "mini secret"; schnorrkel expands that into an sr25519 key — SHA-512, the ed25519 clamp,
/// then a division by the cofactor — and the public key is that scalar times the base point, encoded as
/// ristretto255. A wallet that used BIP-44 here would show an address the user's other wallets never
/// see. Pinned to subkey's own documented output.
///
/// Public-key derivation only: this wallet does not sign Polkadot transactions.
/// </summary>
public static class PolkadotKeys
{
    /// <summary>substrate-bip39 <c>mini_secret_from_entropy</c>: PBKDF2-HMAC-SHA512(entropy, "mnemonic"
    /// + passphrase, 2048), first 32 bytes.</summary>
    public static byte[] MiniSecretFromEntropy(byte[] entropy, string passphrase = "")
    {
        if (entropy.Length is < 16 or > 32 || entropy.Length % 4 != 0)
            throw new ArgumentException("BIP39 entropy is 16–32 bytes in steps of 4.", nameof(entropy));

        var salt = System.Text.Encoding.UTF8.GetBytes("mnemonic" + passphrase);
        var seed = Rfc2898DeriveBytes.Pbkdf2(entropy, salt, 2048, HashAlgorithmName.SHA512, 64);
        var mini = seed[..32];
        CryptographicOperations.ZeroMemory(seed);
        return mini;
    }

    /// <summary>schnorrkel <c>MiniSecretKey::expand(ExpansionMode::Ed25519)</c>, then the public key.</summary>
    public static byte[] PublicKeyFromMiniSecret(byte[] miniSecret)
    {
        if (miniSecret.Length != 32) throw new ArgumentException("A mini secret is 32 bytes.", nameof(miniSecret));

        var h = SHA512.HashData(miniSecret);
        var key = h[..32];
        CryptographicOperations.ZeroMemory(h);

        key[0] &= 248;
        key[31] &= 63;
        key[31] |= 64;

        // divide_scalar_bytes_by_cofactor: the clamp cleared the low three bits, so this is exact.
        var scalar = new BigInteger(key, isUnsigned: true, isBigEndian: false) >> 3;
        CryptographicOperations.ZeroMemory(key);

        return Ristretto255.EncodeBaseMultiple(scalar);
    }
}

/// <summary>
/// SS58 addresses: base58 of prefix ‖ public key ‖ the first two bytes of
/// BLAKE2b-512("SS58PRE" ‖ prefix ‖ public key). Prefix 0 is Polkadot ("1…"), 2 is Kusama, 42 is the
/// generic Substrate format ("5…"). Pinned to Polkadot.js's own encode tests.
/// </summary>
public static class Ss58
{
    public const byte PolkadotPrefix = 0;
    private static readonly byte[] Context = "SS58PRE"u8.ToArray();

    public static string Encode(ReadOnlySpan<byte> publicKey, byte prefix = PolkadotPrefix)
    {
        if (publicKey.Length != 32) throw new ArgumentException("An account id is 32 bytes.", nameof(publicKey));
        if (prefix >= 64) throw new ArgumentOutOfRangeException(nameof(prefix), "Only single-byte prefixes are used here.");

        var body = new byte[33];
        body[0] = prefix;
        publicKey.CopyTo(body.AsSpan(1));

        var full = new byte[35];
        body.CopyTo(full, 0);
        Checksum(body).AsSpan(0, 2).CopyTo(full.AsSpan(33));
        return NBitcoin.DataEncoders.Encoders.Base58.EncodeData(full);
    }

    /// <summary>Decodes a single-byte-prefix SS58 address, verifying its checksum.</summary>
    public static bool TryDecode(string? address, out byte prefix, out byte[] publicKey)
    {
        prefix = 0;
        publicKey = [];
        if (string.IsNullOrWhiteSpace(address)) return false;

        byte[] raw;
        try { raw = NBitcoin.DataEncoders.Encoders.Base58.DecodeData(address.Trim()); }
        catch { return false; }

        if (raw.Length != 35 || raw[0] >= 64) return false;

        var check = Checksum(raw.AsSpan(0, 33));
        if (raw[33] != check[0] || raw[34] != check[1]) return false;

        prefix = raw[0];
        publicKey = raw[1..33];
        return true;
    }

    public static bool IsPolkadotAddress(string? address) =>
        TryDecode(address, out var prefix, out _) && prefix == PolkadotPrefix;

    private static byte[] Checksum(ReadOnlySpan<byte> body)
    {
        var digest = new Blake2bDigest(512);
        digest.BlockUpdate(Context, 0, Context.Length);
        var b = body.ToArray();
        digest.BlockUpdate(b, 0, b.Length);
        var output = new byte[64];
        digest.DoFinal(output, 0);
        return output;
    }
}

/// <summary>
/// Reading a Polkadot account's balance straight from chain storage (roadmap N.8) — no indexer, no API
/// key: the <c>System.Account</c> entry for the account, SCALE-encoded.
///
/// Since the 2025 Asset Hub migration most DOT lives on Polkadot Asset Hub, not the relay chain; the
/// same entry is read on both and the two are added. Checked live before this was written: the
/// treasury's entry held 24.3 M DOT on Asset Hub against 2.7 k on the relay.
/// </summary>
public static class PolkadotAccounts
{
    /// <summary>twox128("System") ‖ twox128("Account") — the fixed prefix of every System.Account key.</summary>
    private const string SystemAccountPrefix = "26aa394eea5630e07c48ae0c9558cef7b99d880ec681799c0cf30e8886371da9";

    /// <summary>DOT has ten decimals ("planck").</summary>
    public const decimal PlanckPerDot = 10_000_000_000m;

    /// <summary>The storage key: prefix ‖ blake2_128(account) ‖ account (the Blake2_128Concat hasher).</summary>
    public static string SystemAccountKey(ReadOnlySpan<byte> accountId)
    {
        if (accountId.Length != 32) throw new ArgumentException("An account id is 32 bytes.", nameof(accountId));

        var digest = new Blake2bDigest(128);
        var id = accountId.ToArray();
        digest.BlockUpdate(id, 0, id.Length);
        var hash = new byte[16];
        digest.DoFinal(hash, 0);

        return "0x" + SystemAccountPrefix + Convert.ToHexString(hash).ToLowerInvariant() + Convert.ToHexString(id).ToLowerInvariant();
    }

    /// <summary>
    /// DOT held on the account — free plus reserved — from the SCALE-encoded <c>AccountInfo</c> returned
    /// by <c>state_getStorage</c>. A missing entry (JSON null) is an account that does not exist on that
    /// chain: a real zero. Anything else that is not an 80-byte record is unknown.
    ///
    /// AccountInfo = nonce, consumers, providers, sufficients (4 × u32) ‖ AccountData = free, reserved,
    /// frozen, flags (4 × u128), all little-endian. "Free" includes anything locked for staking or
    /// governance; "reserved" includes held funds. Together they are what the account owns.
    /// </summary>
    public static decimal? ParseAccountInfo(string? scaleHex, bool entryMissing)
    {
        if (entryMissing) return 0m;
        if (scaleHex is null || !scaleHex.StartsWith("0x", StringComparison.Ordinal) || scaleHex.Length != 2 + 160)
            return null;

        byte[] bytes;
        try { bytes = Convert.FromHexString(scaleHex[2..]); }
        catch (FormatException) { return null; }

        var free = new BigInteger(bytes.AsSpan(16, 16), isUnsigned: true, isBigEndian: false);
        var reserved = new BigInteger(bytes.AsSpan(32, 16), isUnsigned: true, isBigEndian: false);
        var planck = free + reserved;

        // Total supply is ~1.5e19 planck, far inside decimal; anything past that is not a real balance.
        return planck > new BigInteger(decimal.MaxValue) ? null : (decimal)planck / PlanckPerDot;
    }
}
