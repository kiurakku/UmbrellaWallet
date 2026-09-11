using System.Text.RegularExpressions;

namespace Umbrella.Wallet.Core.Safety;

/// <summary>Why a token was judged to be an unsolicited airdrop rather than an asset.</summary>
public enum SpamSignal
{
    None,
    /// <summary>The name carries a web address — the lure that makes the scam work.</summary>
    ContainsWebAddress,
    /// <summary>The name reads as an advert ("claim", "reward", "gambling"…).</summary>
    ReadsAsAdvert,
    /// <summary>The name is a sentence, not a token name.</summary>
    NameIsASentence,
}

public sealed record SpamVerdict(bool IsSuspected, SpamSignal Signal);

/// <summary>
/// Flags unsolicited airdrop tokens — the ones that arrive uninvited in every TRON and Ethereum
/// account, named things like "Hash gambling at Ha138Com" or "Visit x.io to claim 5000 USDT".
///
/// They are not merely clutter. The name IS the attack: it is a lure to a site that will ask for a
/// seed phrase or a signature. So the wallet should not present one as if it were an asset the user
/// owns.
///
/// Two rules keep this safe to act on:
///
/// 1. A token with a real market price is NEVER flagged, whatever it is called. Value is the strongest
///    possible evidence that something is a genuine asset, and wrongly hiding money is far worse than
///    showing spam.
/// 2. It flags, it does not delete. Nothing is removed from the chain or from the wallet's data — the
///    caller decides how to present a flagged row, and the user can always look at it.
/// </summary>
public static class SpamTokenInspector
{
    /// <summary>A bare domain ("ha138.com"), a scheme, or a name gluing a TLD onto a word ("Ha138Com").</summary>
    private static readonly Regex WebAddress = new(
        @"(https?://)|(www\.)|(t\.me)|(\w\.(com|net|org|io|xyz|top|vip|cc|ru|info|site|club|online|shop|live|app|fun|biz))\b|([A-Za-z0-9]{2,}(Com|Net|Org|Xyz|Vip|Top)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Words that belong to an advert, never to a ticker.</summary>
    private static readonly string[] AdvertWords =
    [
        "gambling", "casino", "lottery", "jackpot", "betting",
        "airdrop", "claim", "reward", "bonus", "voucher", "coupon", "giveaway",
        "winner", "prize", "gift", "free ", "promo", "welcome to", "congratulation",
        "visit ", "join ", "telegram", "whatsapp",
    ];

    /// <summary>
    /// Judges one token. <paramref name="hasMarketPrice"/> must be true when the wallet has a real,
    /// non-zero USD price for it — that alone makes the token exempt.
    /// </summary>
    public static SpamVerdict Inspect(string? name, string? symbol, bool hasMarketPrice)
    {
        if (hasMarketPrice) return new SpamVerdict(false, SpamSignal.None);

        var haystack = $"{name} {symbol}".Trim();
        if (haystack.Length == 0) return new SpamVerdict(false, SpamSignal.None);

        if (WebAddress.IsMatch(haystack))
            return new SpamVerdict(true, SpamSignal.ContainsWebAddress);

        var lowered = haystack.ToLowerInvariant();
        foreach (var word in AdvertWords)
        {
            if (lowered.Contains(word, StringComparison.Ordinal))
                return new SpamVerdict(true, SpamSignal.ReadsAsAdvert);
        }

        // Real token names are short. Five or more words is prose, and prose in a token name is a
        // message aimed at the reader — which is the whole point of these airdrops.
        //
        // Counted on the NAME alone: including the symbol would push a legitimate four-word name such
        // as "Wrapped Liquid Staked Ether" over the line as soon as its ticker was appended.
        var words = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 5)
            return new SpamVerdict(true, SpamSignal.NameIsASentence);

        return new SpamVerdict(false, SpamSignal.None);
    }
}
