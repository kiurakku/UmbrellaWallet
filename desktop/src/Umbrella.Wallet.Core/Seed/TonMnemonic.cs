using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NBitcoin;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Core.Seed;

/// <summary>
/// TON-native 24-word mnemonics — the standard used by Telegram Wallet, Tonkeeper and TON Space.
/// They share the BIP39 English wordlist but NOT the BIP39 checksum: validity and key derivation use
/// TON's own HMAC-SHA512 + PBKDF2-SHA512 scheme, so these phrases fail a BIP39 checksum by design.
///
/// The full chain (mnemonic → 32-byte ed25519 seed → public key → wallet v4R2 address) is pinned
/// byte-for-byte against @ton/crypto + @ton/ton by a unit test, so an imported address matches exactly
/// what Tonkeeper / Telegram Wallet show.
/// </summary>
public static class TonMnemonic
{
    private const int PbkdfIterations = 100_000;

    /// <summary>True when the phrase is a valid TON mnemonic (24 wordlist words whose TON "basic seed"
    /// check passes). Standard wallets use no mnemonic password, which is what this validates.</summary>
    public static bool IsTonMnemonic(string? phrase)
    {
        var words = Split(phrase);
        if (words.Length != 24) return false;
        foreach (var w in words)
        {
            if (!Wordlist.English.WordExists(w, out _)) return false;
        }
        return IsBasicSeed(Entropy(words));
    }

    /// <summary>The 32-byte ed25519 seed for the standard TON wallet (v4R2), from the mnemonic.</summary>
    public static byte[] ToSeed(string phrase)
    {
        var words = Split(phrase);
        var seed = Rfc2898DeriveBytes.Pbkdf2(
            Entropy(words),
            Encoding.UTF8.GetBytes("TON default seed"),
            PbkdfIterations,
            HashAlgorithmName.SHA512,
            64);
        return seed[..32];
    }

    /// <summary>The wallet-v4R2 receive address (non-bounceable UQ form) and public key for the phrase.</summary>
    public static (string Address, byte[] PublicKey) DeriveWallet(string phrase)
    {
        var pub = Slip10Ed25519.PublicKey(ToSeed(phrase));
        return (TonKeys.WalletV4R2Address(pub), pub);
    }

    /// <summary>Normalises a pasted TON phrase to clean space-separated lowercase words.</summary>
    public static string Normalize(string phrase) => string.Join(' ', Split(phrase));

    // entropy = HMAC-SHA512(key = mnemonic phrase, message = password[empty for standard wallets]).
    private static byte[] Entropy(string[] words)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(string.Join(' ', words)));
        return hmac.ComputeHash(Array.Empty<byte>());
    }

    // isBasicSeed: PBKDF2-SHA512(entropy, "TON seed version", floor(100000/256)) first byte == 0.
    private static bool IsBasicSeed(byte[] entropy)
    {
        var seed = Rfc2898DeriveBytes.Pbkdf2(
            entropy,
            Encoding.UTF8.GetBytes("TON seed version"),
            Math.Max(1, PbkdfIterations / 256),
            HashAlgorithmName.SHA512,
            64);
        return seed[0] == 0;
    }

    private static string[] Split(string? phrase) =>
        string.IsNullOrEmpty(phrase)
            ? Array.Empty<string>()
            : Regex.Matches(phrase.ToLowerInvariant(), "[a-z]+").Select(m => m.Value).ToArray();
}
