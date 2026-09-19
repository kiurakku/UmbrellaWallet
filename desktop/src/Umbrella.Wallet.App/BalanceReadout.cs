namespace Umbrella.Wallet.App;

/// <summary>
/// How much the wallet actually knows about a balance it is about to show.
///
/// "We could not ask" and "there is nothing there" are different facts, and a wallet that renders
/// them identically — as a confident <c>0.0000</c> — tells somebody their money is gone every time an
/// explorer rate-limits it (MANIFESTO §4, roadmap P0.6).
/// </summary>
public enum BalanceRead
{
    /// <summary>Read from the chain during this session. The number is current.</summary>
    Live,

    /// <summary>Shown from the device-local cache of the last successful read; not confirmed since.</summary>
    Cached,

    /// <summary>Never read successfully, and the last attempt failed. The amount is not known at all.</summary>
    Unknown,
}

/// <summary>
/// The one place that decides what a balance row knows, and what it is therefore allowed to say.
///
/// Kept pure and separate from the view model so the rule can be proved by breaking it: a failed
/// read must never become a zero, and a zero the chain actually reported must never be hidden behind
/// "unavailable" either — an empty address is a real answer.
/// </summary>
public static class BalanceReadout
{
    /// <summary>
    /// Folds one read attempt into what the row already knew.
    ///
    /// <paramref name="read"/> is null when the call failed (unreachable server, rate limit, refused
    /// by the kill-switch) — never when the chain answered "zero". The previous amount is kept rather
    /// than blanked, because a number that was true a minute ago is more useful than no number, as
    /// long as the row stops claiming it is current.
    /// </summary>
    public static (double Amount, BalanceRead State) Apply(
        decimal? read, double previousAmount, BalanceRead previousState) =>
        read is { } value
            ? ((double)value, BalanceRead.Live)
            : (previousAmount, previousState switch
            {
                // Had a real reading at some point: it is now stale, not unknown.
                BalanceRead.Live or BalanceRead.Cached => BalanceRead.Cached,
                _ => BalanceRead.Unknown,
            });

    /// <summary>
    /// The amount as a row should render it: a dash when the wallet has no reading at all, so an
    /// unanswered question never looks like an empty wallet.
    /// </summary>
    public static string AmountText(double amount, BalanceRead state, string symbol) =>
        state == BalanceRead.Unknown
            ? $"— {symbol}"
            : $"{amount.ToString("N6", System.Globalization.CultureInfo.InvariantCulture)} {symbol}";

    /// <summary>Only a live or cached reading may contribute to the portfolio total.</summary>
    public static bool CountsTowardsTotal(BalanceRead state) => state != BalanceRead.Unknown;
}
