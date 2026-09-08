namespace Umbrella.Wallet.Core.Chains;

/// <summary>How a pasted destination compares with the network it is about to be sent on.</summary>
public enum AddressShape
{
    /// <summary>This wallet has no shape rule for that ticker, so nothing can be claimed either way.</summary>
    Unknown = 0,

    /// <summary>The address has the shape addresses on that chain have.</summary>
    Matches = 1,

    /// <summary>The address does NOT look like that chain's — very likely the wrong network.</summary>
    Mismatch = 2,
}

/// <summary>
/// A cheap shape check of a destination address against the chain it is about to be sent on, so a
/// coin is not sent to an address for a different network (roadmap §6.3). Sending to the wrong chain
/// is one of the most common ways people lose funds, and it is unrecoverable.
///
/// Deliberately a heuristic and deliberately advisory: it runs on every keystroke, so it must be pure
/// and instant, and it can only ever *warn*. The authoritative validation stays in the per-chain
/// preparation path, which parses the address properly before anything is signed. Anything this does
/// not have a rule for is <see cref="AddressShape.Unknown"/> — it never guesses "wrong".
///
/// Lifted out of the view model (roadmap §8.3.2) so the rules are testable on their own; the rules
/// themselves are unchanged.
/// </summary>
public static class DestinationAddressCheck
{
    public static AddressShape Check(string? symbol, string? address)
    {
        var addr = (address ?? string.Empty).Trim();
        var sym = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (addr.Length == 0 || sym.Length == 0) return AddressShape.Unknown;

        bool? matches = sym switch
        {
            "BTC" => addr.StartsWith("bc1") || addr.StartsWith("1") || addr.StartsWith("3"),
            "LTC" => addr.StartsWith("ltc1") || addr.StartsWith("L") || addr.StartsWith("M"),
            "DOGE" => addr.StartsWith("D") || addr.StartsWith("A"),
            "ETH" or "BNB" or "MATIC" or "AVAX" or "FTM" or "CRO" => addr.StartsWith("0x") && addr.Length == 42,
            "TRX" or "USDT" => addr.StartsWith("T") && addr.Length == 34,
            "SOL" => !addr.StartsWith("0x") && addr.Length is >= 32 and <= 44,
            "TON" => addr.StartsWith("UQ") || addr.StartsWith("EQ") || addr.StartsWith("0:"),
            "ADA" => addr.StartsWith("addr1"),
            "XMR" => addr.Length is >= 90 and <= 106 && (addr.StartsWith("4") || addr.StartsWith("8")),
            _ => null, // no rule for this ticker — say nothing rather than something wrong
        };

        return matches switch
        {
            true => AddressShape.Matches,
            false => AddressShape.Mismatch,
            _ => AddressShape.Unknown,
        };
    }

    /// <summary>True only when the shape is positively wrong — the one case worth warning about.</summary>
    public static bool IsProbablyWrongNetwork(string? symbol, string? address) =>
        Check(symbol, address) == AddressShape.Mismatch;
}
