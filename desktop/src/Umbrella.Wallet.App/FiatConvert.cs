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

    /// <summary>
    /// The reverse of <see cref="FiatToCoinAmount"/>: what a typed coin amount is worth in USD, for the
    /// "≈ $42.10" hint next to an amount field. Returns "" when there is no positive amount or no usable
    /// price. Amounts under a cent keep full precision instead of rounding to "0.00", so a real value is
    /// never displayed as zero (no-silent-zero rule).
    /// </summary>
    public static string CoinToFiatText(string? coinText, decimal priceUsd)
    {
        if (priceUsd <= 0) return string.Empty;
        if (!AmountInput.TryParsePositive(coinText ?? string.Empty, out var coin)) return string.Empty;
        var fiat = coin * priceUsd;
        if (fiat <= 0) return string.Empty;
        return fiat >= 0.01m
            ? fiat.ToString("0.00", CultureInfo.InvariantCulture)
            : fiat.ToString("0.########", CultureInfo.InvariantCulture);
    }
}
