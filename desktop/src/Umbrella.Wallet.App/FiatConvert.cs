using System.Globalization;
using Umbrella.Wallet.Core.Amounts;

namespace Umbrella.Wallet.App;

/// <summary>
/// Converts a typed fiat (USD) amount into the coin amount to send. A convenience for the Send screen —
/// the coin field stays the authoritative value that is actually signed; this only fills it in. Pure and
/// offline so it can be unit-tested, and it parses through <see cref="AmountInput"/> exactly like the
/// send path (so "0,5" can never be misread as 5, roadmap amount-parsing rule).
/// </summary>
public static class FiatConvert
{
    /// <summary>
    /// fiat / price, formatted to the 8-dp coin precision the send field uses (same "0.########" as the
    /// Max button writes). Returns "" when there is no positive amount or no usable price, so the UI shows
    /// nothing rather than a misleading 0.
    /// </summary>
    public static string FiatToCoinAmount(string? fiatText, decimal priceUsd)
    {
        if (priceUsd <= 0) return string.Empty;
        if (!AmountInput.TryParsePositive(fiatText ?? string.Empty, out var fiat)) return string.Empty;
        var coin = fiat / priceUsd;
        if (coin <= 0) return string.Empty;
        return coin.ToString("0.########", CultureInfo.InvariantCulture);
    }
}
