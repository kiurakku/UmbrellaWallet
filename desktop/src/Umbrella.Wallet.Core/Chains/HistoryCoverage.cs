namespace Umbrella.Wallet.Core.Chains;

/// <summary>
/// Which of the coins a wallet actually holds have their on-chain history read, and which do not
/// (roadmap P1.10).
///
/// The Activity feed shows what this wallet did plus whatever history it can pull from an explorer.
/// For a chain with no history reader — Dogecoin and transparent Zcash today — a transaction made
/// anywhere else, or before this wallet existed, simply is not there. The feed does not look empty
/// because nothing happened; it looks empty because nobody asked.
///
/// That is the same shape of lie as a balance of zero on an unreachable explorer (P0.6): silence
/// presented as an answer. So the screen names the coins it is not reading, and this decides which
/// ones those are — from the capability catalog, so it can never drift from what the code does.
/// </summary>
public static class HistoryCoverage
{
    /// <summary>
    /// The symbols among <paramref name="heldSymbols"/> whose chain has no history reader, in
    /// catalog order and without duplicates. Unknown symbols (tokens, watch-only rows for chains
    /// this build does not model) are left out rather than guessed at.
    /// </summary>
    public static IReadOnlyList<string> WithoutHistory(IEnumerable<string> heldSymbols)
    {
        var held = new HashSet<string>(heldSymbols ?? [], StringComparer.OrdinalIgnoreCase);

        return ChainCatalog.All
            .Where(c => !c.HasHistory && c.CanReceive && held.Contains(c.Symbol))
            .Select(c => c.Symbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
