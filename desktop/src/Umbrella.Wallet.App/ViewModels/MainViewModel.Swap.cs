using Avalonia.Controls;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QRCoder;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Amounts;
using Umbrella.Wallet.Core.Seed;
using Umbrella.Wallet.Core.Utxo;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// Swapping over THORChain: quote, re-quote, and the non-custodial vault deposit.
///
/// Split out of MainViewModel.cs (roadmap §8.3.1) as a partial class: the code is unchanged
/// and still one type, so nothing about behaviour moved with it — only the file it lives in.
/// </summary>
public partial class MainViewModel
{
    // ===== Swap (THORChain — decentralised, non-custodial cross-chain) =============================
    // The wallet never holds the funds: it sends the source coin to a THORChain inbound vault with a
    // memo (OP_RETURN), and the network delivers the target coin to the user's own receive address.

    private readonly ThorchainSwapClient _thorchain = new();
    private SwapQuote? _swapQuote;

    public ObservableCollection<string> SwapFromOptions { get; } = new(ThorchainSwapClient.SendableFrom);
    // "To" excludes whatever "From" is (you can't swap a coin for itself), rebuilt when From changes.
    public ObservableCollection<string> SwapToOptions { get; } =
        new(ThorchainSwapClient.ReceivableTo.Where(s => !string.Equals(s, "BTC", StringComparison.OrdinalIgnoreCase)));

    [ObservableProperty] private string _swapFromSymbol = "BTC";
    [ObservableProperty] private string _swapToSymbol = "ETH";
    [ObservableProperty] private string _swapAmount = string.Empty;
    [ObservableProperty] private bool _hasSwapQuote;
    [ObservableProperty] private bool _swapBusy;
    [ObservableProperty] private string _swapError = string.Empty;
    [ObservableProperty] private string _swapSuccess = string.Empty;
    [ObservableProperty] private string _swapExpectedOut = string.Empty;
    [ObservableProperty] private string _swapRateText = string.Empty;
    [ObservableProperty] private string _swapFeeText = string.Empty;
    [ObservableProperty] private string _swapEtaText = string.Empty;
    [ObservableProperty] private string _swapDestination = string.Empty;
    [ObservableProperty] private string _swapExpiryText = string.Empty;
    [ObservableProperty] private string _swapWarning = string.Empty;

    partial void OnSwapFromSymbolChanged(string value)
    {
        RebuildSwapToOptions();
        InvalidateSwap();
    }
    partial void OnSwapToSymbolChanged(string value) => InvalidateSwap();
    partial void OnSwapAmountChanged(string value) => InvalidateSwap();

    /// <summary>Swaps the From and To coins — the ⇅ button between them, like every DEX swap widget.
    /// Only flips when the current "To" is a valid "From" (some receive-only coins can't be paid from).</summary>
    [RelayCommand]
    private void FlipSwap()
    {
        var newFrom = SwapToSymbol;
        var newTo = SwapFromSymbol;
        if (string.IsNullOrEmpty(newFrom) ||
            !ThorchainSwapClient.SendableFrom.Contains(newFrom, StringComparer.OrdinalIgnoreCase))
            return; // the target coin can't be a source — leave the pair as it is

        SwapFromSymbol = newFrom;   // this rebuilds the To options…
        SwapToSymbol = newTo;       // …then pin To to the old From
    }

    /// <summary>Keeps the "To" list to the receivable assets minus the current "From", so an
    /// impossible same-coin pair can't be selected. Fixes the selection if it becomes invalid.</summary>
    private void RebuildSwapToOptions()
    {
        var wanted = ThorchainSwapClient.ReceivableTo
            .Where(s => !string.Equals(s, SwapFromSymbol, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (!SwapToOptions.SequenceEqual(wanted, StringComparer.OrdinalIgnoreCase))
        {
            SwapToOptions.Clear();
            foreach (var s in wanted) SwapToOptions.Add(s);
        }
        if (!SwapToOptions.Contains(SwapToSymbol, StringComparer.OrdinalIgnoreCase))
            SwapToSymbol = SwapToOptions.FirstOrDefault() ?? string.Empty;
    }

    private void InvalidateSwap()
    {
        HasSwapQuote = false;
        _swapQuote = null;
        SwapSuccess = string.Empty;
    }

    private static ChainId? SwapChainId(string symbol) => symbol.ToUpperInvariant() switch
    {
        "BTC" => ChainId.Btc,
        "LTC" => ChainId.Ltc,
        "ETH" => ChainId.Eth,
        "DOGE" => ChainId.Doge,
        "BCH" => ChainId.Bch, // swap TARGET only — THORChain delivers BCH to the wallet's own receive address
        // AVAX and BNB (BSC), plus the ERC-20 stablecoins USDC/USDT, are all delivered to the SAME 0x
        // address as ETH (shared key), so the destination is derived as the ETH address. Targets only.
        "AVAX" or "BNB" or "USDC" or "USDT" => ChainId.Eth,
        _ => null,
    };

    /// <summary>Step 1: fetch a live, non-binding THORChain quote for the chosen pair and amount.</summary>
    [RelayCommand]
    private async Task GetSwapQuoteAsync()
    {
        SwapError = string.Empty;
        SwapSuccess = string.Empty;
        SwapWarning = string.Empty;
        InvalidateSwap();

        if (_unlockedMnemonic is null) { SwapError = "Unlock the wallet first."; return; }
        var from = (SwapFromSymbol ?? "").ToUpperInvariant();
        var to = (SwapToSymbol ?? "").ToUpperInvariant();
        if (from == to) { SwapError = "Choose two different assets."; return; }
        // Same reason as the send field: a comma decimal must not be read as a group separator.
        if (!AmountInput.TryParsePositive(SwapAmount, out var amount))
        {
            SwapError = "Enter a valid amount.";
            return;
        }

        var toChain = SwapChainId(to);
        if (toChain is null) { SwapError = $"Cannot receive {to}."; return; }
        var destination = _deriver.DeriveReceiveAddress(_unlockedMnemonic!, toChain.Value).Address;

        SwapBusy = true;
        try
        {
            var (quote, error) = await _thorchain.GetQuoteAsync(from, to, amount, destination);
            if (quote is null) { SwapError = error ?? "Could not get a quote."; return; }

            _swapQuote = quote;
            SwapExpectedOut = $"{Fmt(quote.ExpectedOut)} {to}";
            SwapRateText = $"1 {from} ≈ {Fmt(quote.ExpectedOut / amount)} {to}";
            SwapFeeText = $"{Fmt(quote.TotalFee)} {to} · {quote.TotalBps / 100.0:0.##}%";
            SwapEtaText = quote.EtaSeconds >= 60 ? $"~{quote.EtaSeconds / 60} min" : $"~{quote.EtaSeconds} s";
            SwapDestination = Shorten(destination);
            var mins = Math.Max(0, (int)(quote.Expiry - DateTimeOffset.UtcNow).TotalMinutes);
            SwapExpiryText = $"quote valid ~{mins} min";
            SwapWarning = quote.BelowMinimum
                ? $"Below the recommended minimum (~{Fmt(quote.RecommendedMinIn)} {from}) — the rate will be poor and the swap may refund."
                : string.Empty;
            HasSwapQuote = true;
        }
        finally
        {
            SwapBusy = false;
        }
    }

    /// <summary>Step 2: re-quote for safety, then sign and broadcast the deposit to THORChain's vault.</summary>
    [RelayCommand]
    private async Task ConfirmSwapAsync()
    {
        if (_unlockedMnemonic is null || _swapQuote is null) { SwapError = "Get a quote first."; return; }
        var shown = _swapQuote;
        var from = shown.FromSymbol;
        var to = shown.ToSymbol;

        if (!ThorchainSwapClient.SendableFrom.Contains(from)) { SwapError = $"Swapping from {from} isn't supported yet."; return; }
        var fromChain = SwapChainId(from);
        var toChain = SwapChainId(to);
        if (fromChain is null || toChain is null) { SwapError = "Unsupported asset."; return; }

        await RunBusyAsync(async () =>
        {
            SwapError = string.Empty;
            StatusMessage = Loc.Instance["status.refreshingQuote"];

            // A fresh quote immediately before sending: THORChain vaults rotate and quotes expire, so a
            // stale inbound address or memo would send the deposit into the void.
            var destination = _deriver.DeriveReceiveAddress(_unlockedMnemonic!, toChain.Value).Address;
            var (fresh, error) = await _thorchain.GetQuoteAsync(from, to, shown.AmountIn, destination);
            if (fresh is null) { SwapError = error ?? "Could not refresh the quote."; return; }
            if (fresh.IsExpired) { SwapError = "The quote expired — get a new one."; return; }

            // Refuse if the rate moved materially against the user since they saw it (>3%).
            if (fresh.ExpectedOut < shown.ExpectedOut * 0.97m)
            {
                _swapQuote = fresh;
                SwapExpectedOut = $"{Fmt(fresh.ExpectedOut)} {to}";
                SwapError = "The rate moved against you — review the updated quote and confirm again.";
                StatusMessage = Loc.Instance["status.swapRateChanged"];
                return;
            }

            StatusMessage = Loc.Instance["status.signingSwap"];
            var fromAddr = _deriver.DeriveReceiveAddress(_unlockedMnemonic!, fromChain.Value).Address;
            var walletId = _registry.Active?.Id ?? "default";

            bool ok;
            string? txid, sendErr;

            if (string.Equals(from, "ETH", StringComparison.OrdinalIgnoreCase))
            {
                // ETH-from: a router.depositWithExpiry call carrying the ETH as value and the swap memo
                // as calldata. THORChain gives us the router + inbound vault in the quote.
                if (string.IsNullOrWhiteSpace(fresh.Router))
                {
                    SwapError = "THORChain didn’t return a router for this ETH swap — try again."; return;
                }
                var expiry = fresh.ExpiryUnix > 0
                    ? new System.Numerics.BigInteger(fresh.ExpiryUnix)
                    : new System.Numerics.BigInteger(DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds());
                var (eq, eErr) = await _ethSender.PrepareSwapAsync(
                    fromAddr, fresh.Router!, fresh.InboundAddress, shown.AmountIn, fresh.Memo, expiry);
                if (eq is null) { SwapError = eErr ?? "Could not build the ETH swap."; return; }

                var priv = _deriver.DeriveEthereumPrivateKey(_unlockedMnemonic!);
                try
                {
                    var res = await _ethSender.SignAndBroadcastSwapAsync(eq, priv);
                    (ok, txid, sendErr) = (res.Ok, res.TxHash, res.Error);
                }
                finally
                {
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(priv);
                }
            }
            else
            {
                // Same multisource HD path as a normal send: gather UTXOs from every owned address, then
                // sign each with its own key. The THORChain memo rides as an OP_RETURN in the same tx.
                if (!_utxoScans.TryGetValue(from, out var scan) || scan is null)
                {
                    var st = _addrIndex.GetState(walletId, from);
                    var fl = new UtxoScanFloors(
                        st.LastIssuedExternalIndex, st.LastSeenUsedExternalIndex,
                        st.LastIssuedInternalIndex, st.LastSeenUsedInternalIndex);
                    scan = await _utxoScanner.ScanAsync(_unlockedMnemonic!, fromChain.Value, UtxoExplorerFor(from), fl);
                    if (!scan.Partial) _utxoScans[from] = scan;
                }
                if (scan.Partial) { SwapError = "Balance isn’t fully synced yet — try again in a moment."; return; }

                // THORChain returns the BCH inbound vault as a bare CashAddr (no "bitcoincash:" scheme);
                // NBitcoin's BCash parser needs the prefix, so restore it before we build the deposit.
                var inbound = fresh.InboundAddress;
                if (string.Equals(from, "BCH", StringComparison.OrdinalIgnoreCase) &&
                    !inbound.StartsWith("bitcoincash:", StringComparison.OrdinalIgnoreCase))
                {
                    inbound = "bitcoincash:" + inbound;
                }

                var (quote, plan, request, prepErr) = await _btcSender.PrepareHdAsync(
                    from, scan.Utxos, fromAddr, inbound, shown.AmountIn, memo: fresh.Memo);
                if (quote is null || plan is null || request is null)
                {
                    SwapError = prepErr ?? "Could not build the swap deposit."; return;
                }

                (ok, txid, sendErr) = await _btcSender.SignAndBroadcastHdAsync(
                    _unlockedMnemonic!, walletId, _addrIndex, from, plan, request);
                _utxoScans.Remove(from);
            }
            if (ok && txid is not null)
            {
                var track = ThorchainSwapClient.TrackUrl(txid);
                SwapSuccess = $"Swap sent ✓  {txid}\nTHORChain will deliver ~{Fmt(fresh.ExpectedOut)} {to} to your wallet.\nTrack: {track}";
                StatusMessage = Loc.Instance["status.swapBroadcast"];
                InvalidateSwap();
                SwapAmount = string.Empty;
                PushActivity("Swap", $"{from}→{to}", $"-{Fmt(shown.AmountIn)}", Shorten(fresh.InboundAddress), "now", track);
                await RefreshLiveDataAsync();
            }
            else
            {
                SwapError = sendErr ?? "Broadcast failed.";
                StatusMessage = Loc.Instance["status.swapFailed"];
            }
        });
    }

    [RelayCommand]
    private void CancelSwap()
    {
        InvalidateSwap();
        SwapError = string.Empty;
        SwapWarning = string.Empty;
        StatusMessage = Loc.Instance["status.swapCancelled"];
    }
}
