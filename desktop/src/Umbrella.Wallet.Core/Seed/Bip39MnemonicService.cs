using System.Text.RegularExpressions;
using NBitcoin;

namespace Umbrella.Wallet.Core.Seed;

/// <summary>
/// Generates and validates 24-word BIP39 English mnemonics.
/// Seed material stays on the calling side; this type does not persist secrets.
/// </summary>
public sealed class Bip39MnemonicService
{
    /// <summary>New wallets are generated with the strongest length (256-bit entropy).</summary>
    public const int GeneratedWordCount = 24;

    /// <summary>Every valid BIP39 length. Import must accept all of them, not only 24.</summary>
    private static readonly int[] AllowedWordCounts = [12, 15, 18, 21, 24];

    /// <summary>
    /// Creates a new cryptographically random 24-word English mnemonic.
    /// </summary>
    public string Generate()
    {
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.TwentyFour);
        return mnemonic.ToString();
    }

    /// <summary>
    /// Validates a candidate mnemonic: English BIP39 wordlist, a valid word count
    /// (12/15/18/21/24), and a valid checksum.
    /// </summary>
    public MnemonicValidationResult Validate(string? mnemonic)
    {
        if (string.IsNullOrWhiteSpace(mnemonic))
        {
            return MnemonicValidationResult.Fail("Recovery phrase is required.");
        }

        // Robust extraction: BIP39 English words are pure a–z, so pull out every run of letters.
        // This tolerates how wallets present a phrase on export — numbered lists ("1. ship 2. subway"),
        // commas, tabs, line breaks or multiple spaces all normalise to a clean word list.
        var words = Regex.Matches(mnemonic.ToLowerInvariant(), "[a-z]+")
            .Select(m => m.Value)
            .ToArray();

        if (words.Length == 0)
        {
            return MnemonicValidationResult.Fail("Recovery phrase is required.");
        }

        if (!AllowedWordCounts.Contains(words.Length))
        {
            return MnemonicValidationResult.Fail(
                $"A recovery phrase has 12, 15, 18, 21, or 24 words — found {words.Length}. " +
                "Paste the whole phrase (Kraken Wallet and most wallets use 12 or 24 words).");
        }

        // Name the first word that isn't in the BIP39 list — usually a typo or autocorrect, and far
        // more useful than a blanket "invalid".
        foreach (var w in words)
        {
            if (!Wordlist.English.WordExists(w, out _))
            {
                return MnemonicValidationResult.Fail(
                    $"“{w}” isn't a valid recovery word — check for a typo or autocorrect. Every word " +
                    "must be from the BIP39 English list.");
            }
        }

        var normalized = string.Join(' ', words);

        try
        {
            var parsed = new Mnemonic(normalized, Wordlist.English);
            if (!parsed.IsValidChecksum)
            {
                // Reaching here means every word IS in the BIP39 English list, but the checksum
                // doesn't match. That is either a typo / wrong order, or — very commonly — a phrase
                // from a wallet that isn't BIP39. Telegram Wallet, Tonkeeper and TON Space use the
                // same wordlist but the TON mnemonic standard, which fails a BIP39 checksum by design.
                return MnemonicValidationResult.Fail(
                    "Recovery phrase checksum is invalid. Every word is spelled correctly, so either a " +
                    "word is out of order, or this phrase is from a non-BIP39 wallet. Telegram Wallet / " +
                    "Tonkeeper (TON) use their own 24-word standard that can't be imported here — your " +
                    "funds stay safe in that wallet.");
            }

            return MnemonicValidationResult.Success(string.Join(' ', parsed.Words));
        }
        catch (Exception ex)
        {
            return MnemonicValidationResult.Fail($"Recovery phrase is invalid: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses a validated mnemonic into an NBitcoin <see cref="Mnemonic"/>.
    /// </summary>
    internal static Mnemonic ParseValidated(string normalizedMnemonic) =>
        new(normalizedMnemonic, Wordlist.English);
}
