using Umbrella.Wallet.App.ViewModels;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The cardinal rule: the wallet must never hand out an address it cannot then find and spend.
///
/// A fresh receive address per payment is the single most effective privacy habit on a transparent
/// chain — it stops every payment you ever received from sitting under one public heading. But the
/// same button is how funds get stranded: if the balance scan only ever looks at address #0, money
/// sent to #3 is money the user watches arrive on an explorer and can never move.
///
/// So rotation is allowed exactly where the wallet does a full HD scan AND can spend across the
/// addresses it finds. These tests pin the three lists to each other so they cannot drift — which is
/// how BCH and DOGE came to be spendable UTXO chains that were still being read one address deep,
/// losing their own change out of the displayed balance after every send.
/// </summary>
public sealed class ReceiveRotationTests
{
    [Fact]
    public void Every_scanned_chain_can_also_be_spent()
    {
        // Scanning without spending would show a balance the user cannot move. The scan list is what
        // gates the fresh-address button, so this is the rule itself.
        var cannotSpend = MainViewModel.UtxoScanChains
            .Where(c => !MainViewModel.SendableSymbols.Contains(c))
            .ToList();

        Assert.Empty(cannotSpend);
    }

    [Fact]
    public void Every_utxo_chain_the_wallet_can_spend_is_also_fully_scanned()
    {
        // The direction that actually bit. A chain that spends creates CHANGE, and change lands on an
        // internal address by design — so a spendable chain read only at address #0 loses its own
        // change out of the displayed balance the moment the user sends anything.
        var utxo = new[] { "BTC", "LTC", "BCH", "DOGE" };

        var spendableButNotScanned = utxo
            .Where(c => MainViewModel.SendableSymbols.Contains(c))
            .Where(c => !MainViewModel.UtxoScanChains.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(spendableButNotScanned);
    }

    [Fact]
    public void The_scan_list_holds_no_duplicates_and_is_not_empty()
    {
        Assert.NotEmpty(MainViewModel.UtxoScanChains);
        Assert.Equal(
            MainViewModel.UtxoScanChains.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            MainViewModel.UtxoScanChains.Length);
    }

    [Fact]
    public void No_account_model_chain_is_in_the_utxo_scan_list()
    {
        // A gap-limit walk is meaningless on an account chain — one address holds everything — and
        // running one would be a pile of pointless requests handed to an explorer for nothing.
        foreach (var accountChain in new[] { "ETH", "SOL", "TON", "ADA", "TRX", "XMR", "BNB" })
        {
            Assert.DoesNotContain(accountChain, MainViewModel.UtxoScanChains, StringComparer.OrdinalIgnoreCase);
        }
    }
}
