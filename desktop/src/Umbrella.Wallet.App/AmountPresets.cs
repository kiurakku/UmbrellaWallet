using System.Globalization;

namespace Umbrella.Wallet.App;

/// <summary>
/// Quick "send a share of my balance" amounts for the Send screen (25% / 50%). A percentage of the full
/// balance is always comfortably below the fee-aware maximum, so a preset can never leave too little for
/// the fee — that is what the separate Max button is for. Pure and offline: it only formats a number the
/// user still confirms as the coin amount, and it uses the same 8-dp precision as Max.
/// </summary>
public static class AmountPresets
{
    /// <summary><paramref name="percent"/>% of <paramref name="balance"/>, formatted to coin precision;
    /// "" when the balance is empty or the percentage is out of range, so the field is left untouched.</summary>
    public static string Of(decimal balance, int percent)
    {
        if (balance <= 0m || percent is <= 0 or > 100) return string.Empty;
        var amount = balance * percent / 100m;
        if (amount <= 0m) return string.Empty;
        return amount.ToString("0.########", CultureInfo.InvariantCulture);
    }
}
