using System.Numerics;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Digests;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>What a Nano account holds: pocketed, and sent to it but not pocketed yet.</summary>
/// <param name="Balance">XNO already received into the account.</param>
/// <param name="Receivable">XNO sent to the account whose receive block has not been published.</param>
public sealed record NanoBalance(decimal Balance, decimal Receivable)
{
    /// <summary>Everything that belongs to the account, pocketed or not.</summary>
    public decimal Total => Balance + Receivable;
}

/// <summary>
/// Nano (XNO) accounts from the wallet's BIP39 phrase — receive and balance.
///
/// The key is SLIP-0010 ed25519 at <c>m/44'/165'/{index}'</c>, the path Ledger, Trust Wallet and Nault's
/// BIP39 mode use, so the phrase restores in them. Nano's own twist is that its ed25519 hashes with
/// BLAKE2b-512 where standard ed25519 uses SHA-512: the public key is the base point times the clamped
/// first half of BLAKE2b-512(private key). A standard ed25519 library would produce a valid-looking key
/// nobody can sign for — so the whole pipeline is pinned to the Nano documentation's own test vector
/// (<c>NanoReceiveTests</c>).
///
/// An address is <c>nano_</c> + the key in Nano's base32 (4 zero bits of padding + 256 bits = 52
/// characters) + a 40-bit BLAKE2b checksum of the key, byte-reversed (8 characters).
///
/// Sending is not here. A Nano account only moves money by publishing signed blocks with proof of work;
/// until that path exists and is proven, the coin is shown and received, and its row says so.
/// </summary>
public static class NanoAccounts
{
    public const uint CoinType = 165;
    public const string Prefix = "nano_";
    private const string LegacyPrefix = "xrb_";
    private const string Alphabet = "13456789abcdefghijkmnopqrstuwxyz";

    /// <summary>1 XNO is 10^30 raw.</summary>
    private static readonly BigInteger RawPerXno = BigInteger.Pow(10, 30);

    /// <summary>The private key for account <paramref name="index"/> of a BIP39 seed.</summary>
    public static byte[] DerivePrivateKey(byte[] bip39Seed, uint index) =>
        Slip10Ed25519.DerivePrivateKey(bip39Seed, [44u, CoinType, index]);

    /// <summary>The public key: [clamp(BLAKE2b-512(private key)[0..32])]·B — ed25519 with Nano's hash.</summary>
    public static byte[] PublicKey(byte[] privateKey)
    {
        var h = Blake2b(privateKey, 64);
        var scalar = h[..32];
        scalar[0] &= 248;
        scalar[31] &= 127;
        scalar[31] |= 64;
        var pub = AdaKeys.ScalarMultBase(scalar);
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(h);
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(scalar);
        return pub;
    }

    /// <summary>The <c>nano_…</c> address of a 32-byte public key.</summary>
    public static string Address(byte[] publicKey)
    {
        if (publicKey.Length != 32) throw new ArgumentException("A Nano public key is 32 bytes.", nameof(publicKey));
        var checksum = Blake2b(publicKey, 5);
        Array.Reverse(checksum);
        return Prefix + Encode(publicKey, padBits: 4) + Encode(checksum, padBits: 0);
    }

    /// <summary>
    /// The public key an address names, or null when it is not a valid Nano address — wrong prefix,
    /// wrong length, a character outside Nano's alphabet, non-zero padding, or a checksum that does not
    /// match. <c>xrb_</c> addresses (the pre-rename prefix) are the same accounts and are accepted.
    /// </summary>
    public static byte[]? TryDecode(string? address)
    {
        var a = (address ?? string.Empty).Trim().ToLowerInvariant();
        string body;
        if (a.StartsWith(Prefix, StringComparison.Ordinal)) body = a[Prefix.Length..];
        else if (a.StartsWith(LegacyPrefix, StringComparison.Ordinal)) body = a[LegacyPrefix.Length..];
        else return null;
        if (body.Length != 60) return null;

        var keyBits = Decode(body[..52]);
        var sumBits = Decode(body[52..]);
        if (keyBits is null || sumBits is null) return null;

        // 52 characters carry 260 bits: 4 bits of zero padding, then the key.
        if (keyBits.Value >> 256 != BigInteger.Zero) return null;
        var key = ToFixedBytes(keyBits.Value, 32);
        var checksum = ToFixedBytes(sumBits.Value, 5);

        var expected = Blake2b(key, 5);
        Array.Reverse(expected);
        return expected.AsSpan().SequenceEqual(checksum) ? key : null;
    }

    public static bool IsValid(string? address) => TryDecode(address) is not null;

    /// <summary>The node's <c>account_balance</c> request.</summary>
    public static object AccountBalanceRequest(string address) => new { action = "account_balance", account = address };

    /// <summary>
    /// An <c>account_balance</c> answer, or null when it is not one. An account nobody has sent to
    /// answers with zeros, which is a real zero; an <c>error</c> answer is not a balance at all.
    /// Amounts are raw (10^-30 XNO) and can exceed what a decimal holds, so they are scaled as big
    /// integers first.
    /// </summary>
    public static NanoBalance? ParseAccountBalance(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("error", out _)) return null;
        if (!TryRaw(root, "balance", out var balance)) return null;

        // Nodes renamed "pending" to "receivable"; either may be present.
        var receivable = TryRaw(root, "receivable", out var r) ? r : TryRaw(root, "pending", out var p) ? p : BigInteger.Zero;
        return new NanoBalance(ToXno(balance), ToXno(receivable));
    }

    /// <summary>Raw to XNO, exact to 18 decimal places — far below anything a display rounds to.</summary>
    public static decimal ToXno(BigInteger raw) =>
        (decimal)(raw / BigInteger.Pow(10, 12)) / 1_000_000_000_000_000_000m;

    private static bool TryRaw(JsonElement root, string name, out BigInteger value)
    {
        value = BigInteger.Zero;
        return root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String &&
               BigInteger.TryParse(p.GetString(), System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture, out value) && value >= 0;
    }

    private static string Encode(byte[] bytes, int padBits)
    {
        var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        var bits = bytes.Length * 8 + padBits;
        var chars = new char[bits / 5];
        for (var i = chars.Length - 1; i >= 0; i--)
        {
            chars[i] = Alphabet[(int)(value & 31)];
            value >>= 5;
        }

        return new string(chars);
    }

    private static BigInteger? Decode(string text)
    {
        var value = BigInteger.Zero;
        foreach (var c in text)
        {
            var d = Alphabet.IndexOf(c);
            if (d < 0) return null;
            value = (value << 5) | d;
        }

        return value;
    }

    private static byte[] ToFixedBytes(BigInteger value, int length)
    {
        var raw = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (raw.Length > length) return raw[^length..];
        var padded = new byte[length];
        raw.CopyTo(padded, length - raw.Length);
        return padded;
    }

    private static byte[] Blake2b(byte[] data, int outBytes)
    {
        var digest = new Blake2bDigest(outBytes * 8);
        digest.BlockUpdate(data, 0, data.Length);
        var output = new byte[outBytes];
        digest.DoFinal(output, 0);
        return output;
    }
}
