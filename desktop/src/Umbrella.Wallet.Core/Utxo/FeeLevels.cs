namespace Umbrella.Wallet.Core.Utxo;

/// <summary>
/// How urgently the user wants a UTXO transaction to confirm. The wallet fetches one "standard" rate
/// from the network (the economical estimate it has always used) and scales it to the other levels, so
/// Standard is byte-for-byte the fee the wallet charged before this selector existed.
/// </summary>
public enum FeeLevel
{
    /// <summary>Cheapest — confirms more slowly. Roughly half the standard rate, never below the floor.</summary>
    Economy,
    /// <summary>The wallet's long-standing economical default.</summary>
    Standard,
    /// <summary>Fastest — pays more to confirm sooner. Roughly double the standard rate, up to the cap.</summary>
    Priority,
}

/// <summary>
/// Turns the network's "standard" fee rate into the rate for a chosen <see cref="FeeLevel"/>. Pure and
/// deterministic so it can be unit-tested without the network.
///
/// The scaling always stays inside <c>[min, max]</c> — the same safe band the fetcher already clamps the
/// standard rate to for that chain — so a level can never produce a fee below the network's relay floor
/// (which would strand the transaction) or an absurd overpayment above the cap. Standard is exactly the
/// input rate (factor 1.0), which is why enabling the selector changes nothing for a user who leaves it
/// on Standard.
/// </summary>
public static class FeeLevels
{
    /// <summary>Economy targets ~half the standard rate.</summary>
    public const double EconomyFactor = 0.5;

    /// <summary>Priority targets ~double the standard rate.</summary>
    public const double PriorityFactor = 2.0;

    public static double Adjust(double standardRate, FeeLevel level, double min, double max)
    {
        var factor = level switch
        {
            FeeLevel.Economy => EconomyFactor,
            FeeLevel.Priority => PriorityFactor,
            _ => 1.0,
        };
        return Math.Clamp(standardRate * factor, min, max);
    }
}
