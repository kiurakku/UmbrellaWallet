using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.App.ViewModels;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Roadmap P1.10 — the Activity feed must say which coins it is not reading, and P1.5 — every UTXO
/// chain gets the same send tooling.
///
/// Both are the same failure in different places: silence presented as an answer. An empty feed reads
/// as "nothing happened" when it means "nobody asked", and a missing coin-control panel reads as
/// "there is nothing to choose here" when it means "this chain was left off a list".
/// </summary>
public sealed class HistoryCoverageTests
{
    [Fact]
    public void A_held_coin_with_no_history_reader_is_named()
    {
        // Dogecoin has no history reader today; the catalog is the source of truth, so if one is
        // wired up later this test stops expecting the warning rather than enforcing a stale one.
        var doge = ChainCatalog.All.First(c => c.Symbol == "DOGE");
        var gaps = HistoryCoverage.WithoutHistory(["BTC", "DOGE", "ETH"]);

        Assert.Equal(doge.HasHistory, !gaps.Contains("DOGE"));
    }

    [Fact]
    public void A_coin_whose_history_is_read_is_never_named()
    {
        var gaps = HistoryCoverage.WithoutHistory(["BTC", "LTC", "ETH", "BCH"]);

        Assert.DoesNotContain("BTC", gaps);
        Assert.DoesNotContain("LTC", gaps);
        Assert.DoesNotContain("ETH", gaps);
        Assert.DoesNotContain("BCH", gaps);   // BCH history landed in 4.6; the warning must not linger
    }

    [Fact]
    public void Coins_the_wallet_does_not_hold_are_not_named()
    {
        // The warning is about what is on screen. Naming a chain the user does not hold would be
        // noise, and noise is how a warning stops being read.
        Assert.Empty(HistoryCoverage.WithoutHistory(["BTC", "ETH"]));
        Assert.Empty(HistoryCoverage.WithoutHistory([]));
    }

    [Fact]
    public void An_unknown_symbol_is_left_alone_rather_than_guessed_at()
    {
        Assert.Empty(HistoryCoverage.WithoutHistory(["NOT-A-COIN", "WEN"]));
    }

    /// <summary>
    /// P1.5. Coin control, the fee selector and the balance scan must cover the same chains. They had
    /// drifted — the scan walked BCH, the fee selector offered it, and coin control did not, so on
    /// Bitcoin Cash the panel that lets you avoid linking your own addresses was simply absent while
    /// the linkage was exactly as real as on Bitcoin.
    /// </summary>
    [Fact]
    public void Every_scanned_utxo_chain_gets_the_same_send_tools()
    {
        foreach (var symbol in MainViewModel.UtxoScanChains)
        {
            Assert.Contains(symbol, MainViewModel.SendableSymbols);

            var chain = ChainCatalog.All.First(
                c => c.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));

            // A chain the wallet scans across every address and can spend from is a chain where
            // choosing the coins matters. Anything else here means one of the lists moved alone.
            Assert.True(chain.CanSend, $"{symbol} is scanned for spending but the catalog says it cannot send");
        }
    }
}
