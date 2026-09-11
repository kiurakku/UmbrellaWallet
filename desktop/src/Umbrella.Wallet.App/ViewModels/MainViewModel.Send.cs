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
/// Sending: optional coin control, the Review quote, and the Confirm/broadcast pipeline.
///
/// Split out of MainViewModel.cs (roadmap §8.3.1) as a partial class: the code is unchanged
/// and still one type, so nothing about behaviour moved with it — only the file it lives in.
/// </summary>
public partial class MainViewModel
{
    // ---- Coin control (roadmap §3.4) --------------------------------------------------------------
    // Opt-in manual UTXO selection for BTC/LTC/DOGE. OFF by default, and while off the send path is
    // byte-identical to automatic selection. When on, only the coins the user ticks may fund the
    // spend: the planner is handed exactly that subset and never reaches outside it, so a spend can
    // avoid pulling in (and thus publicly linking) coins that belong to a different identity.

    /// <summary>True only while the send picker is on a UTXO chain — drives the panel's visibility.</summary>
    [ObservableProperty] private bool _coinControlAvailable;

    /// <summary>The opt-in switch. Turning it on loads the coins for the current chain.</summary>
    [ObservableProperty] private bool _coinControlOn;

    [ObservableProperty] private bool _coinControlLoading;

    [ObservableProperty] private string _coinControlSummary = string.Empty;

    /// <summary>The coins offered for the currently-selected UTXO send chain.</summary>
    public ObservableCollection<CoinControlUtxoVm> CoinControlUtxos { get; } = new();

    // The chain the loaded coin list belongs to, so a stale list is never applied to another chain.
    private string? _coinControlChain;

    private static bool IsUtxoSendChain(string s) => s is "BTC" or "LTC" or "DOGE";

    // ---- Fee level (network speed) ----------------------------------------------------------------
    // A slow/standard/fast selector for the UTXO chains (BTC/LTC/DOGE/BCH). Standard is exactly the
    // economical rate the wallet has always used, so an untouched selector never changes the fee. Only
    // the sat/vB handed to PlanSpend changes — the signing/broadcast path is completely unaffected, and
    // every level stays inside the chain's safe fee band (never below the relay floor). See FeeLevels.

    /// <summary>True only while the send picker is on a UTXO chain — drives the fee selector's visibility.</summary>
    [ObservableProperty] private bool _feeLevelAvailable;

    /// <summary>0 = Economy, 1 = Standard, 2 = Priority. Standard by default, which equals today's fee.</summary>
    [ObservableProperty] private int _feeLevelIndex = 1;

    private static bool IsUtxoFeeChain(string s) => s is "BTC" or "LTC" or "DOGE" or "BCH";

    private FeeLevel SelectedFeeLevel => FeeLevelIndex switch
    {
        0 => FeeLevel.Economy,
        2 => FeeLevel.Priority,
        _ => FeeLevel.Standard,
    };

    /// <summary>Which fee tier is selected — for the segmented selector's checked state.</summary>
    public bool IsFeeEconomy => FeeLevelIndex == 0;
    public bool IsFeeStandard => FeeLevelIndex == 1;
    public bool IsFeePriority => FeeLevelIndex == 2;

    /// <summary>Sets the fee tier from the segmented selector ("0"/"1"/"2").</summary>
    [RelayCommand]
    private void SetFeeLevel(string? index)
    {
        if (int.TryParse(index, out var i) && i is >= 0 and <= 2) FeeLevelIndex = i;
    }

    partial void OnFeeLevelIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsFeeEconomy));
        OnPropertyChanged(nameof(IsFeeStandard));
        OnPropertyChanged(nameof(IsFeePriority));
        // Re-quote only when a quote is already on screen, so touching the selector before Review just
        // sets the level for the next quote (and never fires a "fill in the fields" error on its own).
        if (HasSendQuote) _ = PrepareSendAsync();
    }

    partial void OnCoinControlOnChanged(bool value)
    {
        if (value) _ = LoadCoinControlAsync();
        else ClearCoinControl();
    }

    private void ClearCoinControl()
    {
        foreach (var r in CoinControlUtxos) r.PropertyChanged -= OnCoinControlRowChanged;
        CoinControlUtxos.Clear();
        _coinControlChain = null;
        UpdateCoinControlSummary();
    }

    /// <summary>Reset coin control whenever the send asset changes (called from the picker hook).</summary>
    private void ResetCoinControl()
    {
        var sym = SendChain.Trim().ToUpperInvariant();
        CoinControlAvailable = IsUtxoSendChain(sym);
        FeeLevelAvailable = IsUtxoFeeChain(sym);
        if (CoinControlOn) CoinControlOn = false; // OnCoinControlOnChanged clears the list
        else ClearCoinControl();
    }

    /// <summary>Loads the confirmed coins for the current UTXO chain into the selection panel, reusing
    /// the cached balance scan when present. Every coin starts selected, so an untouched panel behaves
    /// exactly like automatic selection; ticks are preserved across a reload.</summary>
    [RelayCommand]
    private async Task LoadCoinControlAsync()
    {
        var chain = SendChain.Trim().ToUpperInvariant();
        if (!IsUtxoSendChain(chain)) return;
        if (_unlockedMnemonic is null) { SendError = Loc.Instance["send.errUnlock"]; CoinControlOn = false; return; }

        var chainId = ParseChain(chain);
        if (chainId is null) return;
        var walletId = _registry.Active?.Id ?? "default";

        CoinControlLoading = true;
        try
        {
            if (!_utxoScans.TryGetValue(chain, out var scan) || scan is null)
            {
                var state = _addrIndex.GetState(walletId, chain);
                var floors = new UtxoScanFloors(
                    state.LastIssuedExternalIndex, state.LastSeenUsedExternalIndex,
                    state.LastIssuedInternalIndex, state.LastSeenUsedInternalIndex);
                scan = await _utxoScanner.ScanAsync(_unlockedMnemonic!, chainId.Value, UtxoExplorerFor(chain), floors);
                if (!scan.Partial) _utxoScans[chain] = scan;
            }

            if (scan.Partial)
            {
                SendError = Loc.Instance["send.errNotSyncedCoins"];
                CoinControlOn = false;
                return;
            }

            // Preserve any prior ticks across a reload (match by coin identity).
            var prior = new HashSet<(string, int)>(
                CoinControlUtxos.Where(x => x.IsSelected).Select(x => (x.TxId, x.Vout)));
            var hadSelection = prior.Count > 0;

            ClearCoinControl();
            foreach (var u in scan.Utxos.Where(u => u.Confirmed).OrderByDescending(u => u.ValueSat))
            {
                var sel = !hadSelection || prior.Contains((u.TxId, u.Vout));
                var row = new CoinControlUtxoVm(u, chain, sel);
                row.PropertyChanged += OnCoinControlRowChanged;
                CoinControlUtxos.Add(row);
            }

            _coinControlChain = chain;
            UpdateCoinControlSummary();
            if (CoinControlUtxos.Count == 0)
                SendError = Loc.Instance["send.errNoCoins"];
        }
        catch (Exception ex)
        {
            SendError = string.Format(Loc.Instance["send.errLoadCoins"], ex.Message);
            CoinControlOn = false;
        }
        finally
        {
            CoinControlLoading = false;
        }
    }

    private void OnCoinControlRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CoinControlUtxoVm.IsSelected)) UpdateCoinControlSummary();
    }

    [RelayCommand] private void CoinControlSelectAll() { foreach (var r in CoinControlUtxos) r.IsSelected = true; }

    [RelayCommand] private void CoinControlSelectNone() { foreach (var r in CoinControlUtxos) r.IsSelected = false; }

    private void UpdateCoinControlSummary()
    {
        var chosen = CoinControlUtxos.Where(r => r.IsSelected).ToList();
        if (chosen.Count == 0)
        {
            CoinControlSummary = CoinControlUtxos.Count == 0
                ? string.Empty
                : "No coins selected — pick at least one to fund the send.";
            return;
        }

        var sym = _coinControlChain ?? SendChain.Trim().ToUpperInvariant();
        var total = chosen.Sum(r => r.ValueSat) / 100_000_000m;
        CoinControlSummary = $"{chosen.Count} of {CoinControlUtxos.Count} coins · {Fmt(total)} {sym} available to spend";
    }

    /// <summary>
    /// Step 1 of the send flow: validate, fetch live nonce/gas/balance, and show a quote.
    /// Nothing is signed here. ETH only — other chains refuse honestly.
    /// </summary>
    [RelayCommand]
    private async Task PrepareSendAsync()
    {
        SendError = string.Empty;
        SendSuccess = string.Empty;
        HasSendQuote = false;
        ClearSendSimulation();
        HasSendPrivacy = false;
        _sendQuote = null;
        _tonQuote = null;

        if (!IsUnlocked || _unlockedMnemonic is null)
        {
            SendError = Loc.Instance["send.errUnlock"];
            return;
        }

        if (string.IsNullOrWhiteSpace(SendTo) || string.IsNullOrWhiteSpace(SendAmount))
        {
            SendError = Loc.Instance["send.errFields"];
            return;
        }

        // AmountInput, not decimal.TryParse: with group separators allowed, "0,5" silently parses as
        // FIVE (see AmountInputTests), which in a send field is a tenfold overspend.
        if (!AmountInput.TryParsePositive(SendAmount, out var amount))
        {
            SendError = Loc.Instance["send.errAmount"];
            return;
        }

        var chain = SendChain.Trim().ToUpperInvariant();
        if (chain == "ETHEREUM") chain = "ETH";
        if (chain == "BITCOIN") chain = "BTC";
        if (chain == "LITECOIN") chain = "LTC";
        if (chain == "SOLANA") chain = "SOL";

        if (chain == "MONERO") chain = "XMR";

        // The coin actually debited. For the EVM L2 rollups (ARB/BASE/OP) the picker value names a
        // NETWORK but the coin is ETH — resolve it so the review shows "ETH", not the network key, and
        // the fiat estimate looks up the right price. For every other chain this is the symbol itself.
        var displayCoin = EthTransactionSender.Chains.TryGetValue(chain, out var evmInfo) ? evmInfo.Symbol : chain;

        // Uniform review fields (§4): full destination (never truncated — the user must verify every
        // character), the amount with its fiat estimate, and a plain statement of the total debit kept
        // separate from the network fee line.
        SendReviewTo = SendTo.Trim();
        SendReviewAmount = $"{Fmt(amount)} {displayCoin}";
        SendReviewFiat = FiatEquivalentLabel(displayCoin, amount);
        SendReviewDebit = chain is "USDT" or "USDC"
            ? string.Format(Loc.Instance["send.debitToken"], SendReviewAmount)
            : string.Format(Loc.Instance["send.debitNative"], SendReviewAmount);

        if (chain == "XMR")
        {
            if (!_monero.IsRunning)
            {
                SendError = Loc.Instance["send.errMoneroOff"];
                return;
            }

            if (!MoneroKeys.TryDecodeAddress(SendTo.Trim(), out _, out _, out _))
            {
                SendError = Loc.Instance["send.errMoneroAddr"];
                return;
            }

            _sendSymbol = "XMR";
            _moneroAmount = amount;
            _moneroTo = SendTo.Trim();

            // Developer fee as a second destination. Only kept if its address is a valid Monero
            // address — otherwise the whole transfer would fail, so the user's send comes first.
            _moneroFeeTo = null;
            _moneroFeeAmount = 0m;
            var xmrFee = _devFee.QuoteFee("XMR", amount);
            if (xmrFee is { } f && MoneroKeys.TryDecodeAddress(f.Address, out _, out _, out _))
            {
                _moneroFeeTo = f.Address;
                _moneroFeeAmount = f.Amount;
            }

            HasSendQuote = true;
            SendQuoteSummary = $"Send {Fmt(amount)} XMR  →  {Shorten(_moneroTo)}";
            SendQuoteFee = _moneroFeeTo is not null
                ? $"Network fee is set by Monero at broadcast · service fee {_devFee.FeePercent:0.##}% ≈ " +
                  $"{Fmt(_moneroFeeAmount)} XMR to the developer (same transaction)."
                : "Fee is set by the Monero network at broadcast (priority: normal).";
            StatusMessage = Loc.Instance["status.reviewTransfer"];
            return;
        }

        if (chain is "TRX" or "TRON" or "USDT" or "TRC20")
        {
            var symbol = chain is "USDT" or "TRC20" ? "USDT" : "TRX";
            var tronAccount = Accounts.FirstOrDefault(a => a.Symbol == "TRX" && a.SupportStatus == "Ready");
            if (tronAccount is null || !IsRealAddress(tronAccount.Address))
            {
                SendError = string.Format(Loc.Instance["send.errNoAccount"], "TRON");
                return;
            }

            _sendSymbol = symbol;
            await RunBusyAsync(async () =>
            {
                StatusMessage = Loc.Instance["status.buildingTron"];
                var (quote, error) = await _tronSender.PrepareAsync(
                    symbol, tronAccount.Address, SendTo.Trim(), amount);
                if (quote is null) { SendError = error ?? Loc.Instance["send.errPrepareFailed"]; return; }

                _tronQuote = quote;
                HasSendQuote = true;
                SendQuoteSummary = $"Send {Fmt(amount)} {symbol}  →  {quote.To}";
                SendQuoteFee = symbol == "USDT"
                    ? "USDT moves on the TRON network — the fee is paid in TRX (energy/bandwidth). Keep a little TRX on this address."
                    : "Fee is paid in TRX bandwidth.";
                StatusMessage = Loc.Instance["status.reviewTransfer"];
            });
            return;
        }

        // XMR / TRON / USDT were handled and returned above; anything reaching here must be a symbol
        // with a real send branch below. Drive that off the single capability set, not a hand-kept
        // list — this is exactly what let the picker offer ADA/EVM while the guard rejected them.
        if (!SendableSymbols.Contains(chain))
        {
            SendError = string.Format(Loc.Instance["send.notSupported"], chain);
            return;
        }

        var from = Accounts.FirstOrDefault(a => a.Symbol == chain && a.SupportStatus == "Ready");
        // EVM side-chains (BNB/MATIC/…) share the Ethereum key and address; if their row hasn't been
        // added by a balance refresh yet, fall back to the Ethereum account so the send still works.
        if (from is null && EthTransactionSender.Chains.ContainsKey(chain))
            from = Accounts.FirstOrDefault(a => a.Symbol == "ETH" && a.SupportStatus == "Ready");
        if (from is null || !IsRealAddress(from.Address))
        {
            SendError = string.Format(Loc.Instance["send.errNoAccount"], chain);
            return;
        }

        _sendSymbol = chain;
        await RunBusyAsync(async () =>
        {
            StatusMessage = Loc.Instance["status.fetchingFees"];
            switch (chain)
            {
                case "ETH":
                case "BNB":
                case "MATIC":
                case "AVAX":
                case "FTM":
                case "CRO":
                case "ARB":
                case "BASE":
                case "OP":
                {
                    var evm = EthTransactionSender.Chains[chain];
                    var (quote, error) = await _ethSender.PrepareAsync(from.Address, SendTo.Trim(), amount, evm);
                    if (quote is null) { SendError = error ?? Loc.Instance["send.errPrepareFailed"]; return; }
                    _sendQuote = quote;
                    SendQuoteSummary = $"Send {Fmt(quote.AmountEth)} {quote.Symbol}  →  {quote.To}";
                    SendQuoteFee =
                        $"Network fee ≈ {Fmt(quote.MaxFeeEth)} {quote.Symbol} · {evm.Name} · nonce {quote.Nonce} · via {new Uri(quote.Rpc).Host}";
                    break;
                }

                case "BTC":
                case "LTC":
                case "DOGE":
                case "BCH":
                {
                    if (_unlockedMnemonic is null) { SendError = Loc.Instance["send.errUnlock"]; return; }
                    var walletId = _registry.Active?.Id ?? "default";

                    // Reuse the balance-refresh scan (all external + internal addresses). If a send is
                    // started before the first refresh finished, scan on demand.
                    if (!_utxoScans.TryGetValue(chain, out var scan) || scan is null)
                    {
                        var chainId0 = ParseChain(chain)!.Value;
                        var state0 = _addrIndex.GetState(walletId, chain);
                        var floors0 = new UtxoScanFloors(
                            state0.LastIssuedExternalIndex, state0.LastSeenUsedExternalIndex,
                            state0.LastIssuedInternalIndex, state0.LastSeenUsedInternalIndex);
                        scan = await _utxoScanner.ScanAsync(
                            _unlockedMnemonic!, chainId0, UtxoExplorerFor(chain), floors0);
                        if (!scan.Partial) _utxoScans[chain] = scan;
                    }

                    if (scan.Partial)
                    {
                        SendError = Loc.Instance["send.errNotSynced"];
                        return;
                    }

                    // Coin control (§3.4): if it's on for THIS chain, fund the spend only from the
                    // coins the user ticked. The planner is handed exactly that subset and can never
                    // reach a coin outside it; off, it sees the full scan exactly as before.
                    IReadOnlyList<OwnedUtxo> spendable = scan.Utxos;
                    if (CoinControlOn && _coinControlChain == chain)
                    {
                        var picked = new HashSet<(string, int)>(
                            CoinControlUtxos.Where(x => x.IsSelected).Select(x => (x.TxId, x.Vout)));
                        if (picked.Count == 0)
                        {
                            SendError = Loc.Instance["send.errCoinControlNone"]; return;
                        }
                        spendable = scan.Utxos.Where(u => picked.Contains((u.TxId, u.Vout))).ToList();
                        if (spendable.Count == 0)
                        {
                            SendError = Loc.Instance["send.errCoinControlStale"]; return;
                        }
                    }

                    var devFee = _devFee.QuoteFee(chain, amount);
                    var (quote, plan, request, error) = await _btcSender.PrepareHdAsync(
                        chain, spendable, from.Address, SendTo.Trim(), amount, devFee?.Address, devFee?.Amount ?? 0m,
                        feeLevel: SelectedFeeLevel);
                    if (quote is null || plan is null || request is null)
                    {
                        SendError = error ?? Loc.Instance["send.errPrepareFailed"]; return;
                    }

                    _btcQuote = quote;
                    _btcPlan = plan;
                    _btcRequest = request;
                    _btcPlanSymbol = chain;
                    SendQuoteSummary = $"Send {Fmt(quote.Amount)} {chain}  →  {quote.To}";
                    // Disclosure is driven off the plan (the source of truth for what is actually sent).
                    SendQuoteFee = quote.DevFeeSat > 0
                        ? $"Network fee ≈ {Fmt(quote.FeeAmount)} {chain} · service fee {_devFee.FeePercent:0.##}% ≈ " +
                          $"{Fmt(quote.DevFeeSat / 100_000_000m)} {chain} to the developer · {quote.InputCount} input(s) · change to a fresh internal address"
                        : $"Network fee ≈ {Fmt(quote.FeeAmount)} {chain} · {quote.InputCount} input(s) · change returns to a fresh internal address";
                    if (CoinControlOn && _coinControlChain == chain)
                        SendQuoteFee += $" · coin control: funded from {plan.Inputs.Count} of your selected coin(s)";

                    // "What will happen", from the plan rather than the request: the plan is the source
                    // of truth for what is actually signed, including the change coming back to us.
                    var utxoSpendable = _utxoScans.TryGetValue(chain, out var scanForSim)
                        ? scanForSim.TotalSat / 100_000_000m
                        : quote.Amount + quote.FeeAmount;
                    BuildSendSimulation(
                        balance: utxoSpendable,
                        amount: quote.Amount,
                        networkFee: quote.FeeAmount,
                        symbol: chain,
                        changeReturned: plan.ChangeSat / 100_000_000m,
                        dustThreshold: 0.00000546m);   // the standard relay dust limit

                    // Privacy Radar (local): the plan's inputs are the addresses this spend links on-chain.
                    ApplySendPrivacy(plan.Inputs.Select(i => i.Address));
                    // Now that inputs are chosen, the private-send plan can name the real
                    // number of addresses this spend links rather than the safe floor of one.
                    RefreshPrivateSendPlan();
                    break;
                }

                case "SOL":
                {
                    var devFee = _devFee.QuoteFee("SOL", amount);
                    var (quote, error) = await _solSender.PrepareAsync(
                        from.Address, SendTo.Trim(), amount, devFee?.Address, devFee?.Amount ?? 0m);
                    if (quote is null) { SendError = error ?? Loc.Instance["send.errPrepareFailed"]; return; }
                    _solQuote = quote;
                    SendQuoteSummary = $"Send {Fmt(quote.AmountSol)} SOL  →  {quote.To}";
                    SendQuoteFee = quote.DevFeeLamports > 0
                        ? $"Network fee ≈ {Fmt(quote.FeeSol)} SOL · service fee {_devFee.FeePercent:0.##}% ≈ " +
                          $"{Fmt(quote.DevFeeLamports / 1_000_000_000m)} SOL to the developer (same transaction)"
                        : $"Network fee ≈ {Fmt(quote.FeeSol)} SOL";
                    BuildSendSimulation(
                        balance: (decimal)from.Amount,
                        amount: quote.AmountSol,
                        networkFee: quote.FeeSol,
                        symbol: "SOL");
                    break;
                }

                case "TON":
                {
                    var (quote, error) = await _tonSender.PrepareAsync(from.Address, SendTo.Trim(), amount);
                    if (quote is null) { SendError = error ?? Loc.Instance["send.errPrepareFailed"]; return; }
                    _tonQuote = quote;
                    SendQuoteSummary = $"Send {Fmt(quote.AmountTon)} TON  →  {quote.To}";
                    SendQuoteFee = quote.Deploy
                        ? $"Network fee ≈ {Fmt(quote.FeeTon)} TON · first send also deploys your wallet (seqno 0)"
                        : $"Network fee ≈ {Fmt(quote.FeeTon)} TON · seqno {quote.Seqno}";
                    break;
                }

                case "ADA":
                {
                    var (quote, error) = await _adaSender.PrepareAsync(from.Address, SendTo.Trim(), amount);
                    if (quote is null) { SendError = error ?? Loc.Instance["send.errPrepareFailed"]; return; }
                    _adaQuote = quote;
                    SendQuoteSummary = $"Send {Fmt(quote.Amount)} ADA  →  {quote.To}";
                    SendQuoteFee = $"Network fee ≈ {Fmt(quote.Fee / 1_000_000m)} ADA · {quote.Inputs.Count} input(s) · change returns to you";
                    break;
                }
            }

            HasSendQuote = true;
            StatusMessage = Loc.Instance["status.reviewTransfer"];
        });
    }

    /// <summary>
    /// Refreshes BTC/LTC balances by scanning every derived address (external + internal) and
    /// aggregating their UTXOs, caching the scan for the send path. A transient explorer error keeps
    /// the last good balance rather than showing a lower, wrong number (roadmap §1.10, §3.2).
    /// </summary>
    /// <summary>
    /// The UTXO chains whose balance comes from a full HD scan across every address rather than from
    /// one address, and therefore the chains on which handing out a fresh receive address is safe.
    ///
    /// These are the same list on purpose. The cardinal rule of this wallet is that it must never
    /// issue an address it cannot then find and spend — an address the scan does not walk is money the
    /// user can see arrive and never move. <c>ReceiveRotationTests</c> pins that.
    /// </summary>
    public static readonly string[] UtxoScanChains = ["BTC", "LTC", "BCH", "DOGE"];

    /// <summary>When each chain last completed a full scan, so a rate-limited explorer is not walked
    /// on every sixty-second refresh.</summary>
    private readonly Dictionary<string, DateTimeOffset> _lastUtxoScan = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The shortest gap between full gap-limit walks of a chain.
    ///
    /// A walk is one request per address, so the cost is set by whoever answers them. Esplora
    /// (BTC/LTC) tolerates a walk a minute and always has. BlockCypher's keyless tier does not —
    /// walking DOGE every minute would exhaust the hourly allowance within a few refreshes and leave
    /// the user with no balance at all, which is worse than a balance that is a few minutes old.
    ///
    /// This only delays noticing money that ARRIVED. Money that left is reflected immediately: a send
    /// drops the cached scan and clears this stamp, so the change address is picked up on the very
    /// next refresh.
    /// </summary>
    private static TimeSpan UtxoScanCooldown(string symbol) => symbol.ToUpperInvariant() switch
    {
        "DOGE" => TimeSpan.FromMinutes(10),   // BlockCypher, keyless: ~100 requests an hour
        "BCH" => TimeSpan.FromMinutes(4),     // Haskoin, more generous but still somebody's server
        _ => TimeSpan.Zero,                   // Esplora — unchanged from before
    };

    /// <summary>True when this chain should be walked now. Always true until it has been walked once:
    /// a cooldown must never be the reason a balance has never been read at all.</summary>
    private bool DueForUtxoScan(string symbol)
    {
        if (!_utxoScans.ContainsKey(symbol)) return true;
        if (!_lastUtxoScan.TryGetValue(symbol, out var last)) return true;
        return DateTimeOffset.UtcNow - last >= UtxoScanCooldown(symbol);
    }

    private async Task RefreshUtxoWalletsAsync(
        IReadOnlyDictionary<string, (decimal Usd, decimal Change24h)> prices, CancellationToken ct)
    {
        if (_unlockedMnemonic is null) return;
        var walletId = _registry.Active?.Id ?? "default";

        // Every UTXO chain the wallet spends from, scanned across ALL its addresses rather than just
        // the first one. BCH and DOGE used to be read by a single-address balance call, which meant the
        // change from a send — which lands on an internal address by design — simply vanished from the
        // shown balance until the next send re-scanned. The money was never lost; the number was wrong,
        // which on a wallet is nearly as bad.
        //
        // The scans run CONCURRENTLY: one after the other meant waiting for the sum of four full
        // gap-limit walks. Only the network phase overlaps; results are applied one at a time below, so
        // Accounts and the address index are never mutated from two places at once.
        var targets = new List<(string Symbol, WalletAccountViewModel Account, ChainId Chain)>();
        foreach (var symbol in UtxoScanChains)
        {
            if (!DueForUtxoScan(symbol)) continue;

            var account = Accounts.FirstOrDefault(a =>
                a.Symbol == symbol && a.SupportStatus == "Ready" && IsRealAddress(a.Address));
            if (account is null) continue;

            var chain = ParseChain(symbol);
            if (chain is null) continue;

            targets.Add((symbol, account, chain.Value));
        }

        async Task<UtxoScanResult?> ScanOrNullAsync(string symbol, ChainId chain)
        {
            try
            {
                var state = _addrIndex.GetState(walletId, symbol);
                var floors = new UtxoScanFloors(
                    state.LastIssuedExternalIndex, state.LastSeenUsedExternalIndex,
                    state.LastIssuedInternalIndex, state.LastSeenUsedInternalIndex);

                return await _utxoScanner.ScanAsync(
                    _unlockedMnemonic!, chain, UtxoExplorerFor(symbol), floors, ct: ct);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Leave the prior amount in place; the next refresh retries.
                return null;
            }
        }

        var scans = await Task.WhenAll(targets.Select(t => ScanOrNullAsync(t.Symbol, t.Chain)));

        for (var i = 0; i < targets.Count; i++)
        {
            var (symbol, account, _) = targets[i];
            var scan = scans[i];
            if (scan is null) continue;
            if (!scan.Partial) _lastUtxoScan[symbol] = DateTimeOffset.UtcNow;

            // A partial (network-degraded) scan must not lower a balance we already trust.
            if (scan.Partial && _utxoScans.ContainsKey(symbol)) continue;

            _utxoScans[symbol] = scan;
            if (scan.HighestUsedExternalIndex is { } he) _addrIndex.RecordSeenUsed(walletId, symbol, 0, he);
            if (scan.HighestUsedInternalIndex is { } hi) _addrIndex.RecordSeenUsed(walletId, symbol, 1, hi);

            var amount = scan.TotalSat / 100_000_000m;
            var (usd, change) = prices.GetValueOrDefault(symbol);
            var idx = Accounts.IndexOf(account);
            if (idx >= 0)
            {
                Accounts[idx] = account with
                {
                    Amount = (double)amount,
                    Price = (double)usd,
                    Change24h = (double)change,
                };
            }
        }

        RefreshHoldings();
        RecalcBalance();
    }

    private static string Fmt(decimal value) =>
        value.ToString("0.########", CultureInfo.InvariantCulture);

    /// <summary>
    /// Step 2: the user explicitly confirms — derive the key, sign locally, broadcast, zero the key.
    /// </summary>
    [RelayCommand]
    private async Task ConfirmSendAsync()
    {
        var haveQuote = _sendQuote is not null || _btcQuote is not null || _solQuote is not null
                        || _tonQuote is not null || _tronQuote is not null || _adaQuote is not null
                        || (_sendSymbol == "XMR" && _moneroAmount > 0);
        if (_unlockedMnemonic is null || !haveQuote)
        {
            SendError = Loc.Instance["send.errPrepareFirst"];
            return;
        }

        await RunBusyAsync(async () =>
        {
            StatusMessage = Loc.Instance["status.signingBroadcast"];
            switch (_sendSymbol)
            {
                case "ETH" or "BNB" or "MATIC" or "AVAX" or "FTM" or "CRO"
                     or "ARB" or "BASE" or "OP" when _sendQuote is not null:
                {
                    var quote = _sendQuote;
                    // Every EVM chain (mainnet, side-chains and L2 rollups) shares the same Ethereum key
                    // and 0x address.
                    var priv = _deriver.DeriveEthereumPrivateKey(_unlockedMnemonic!);
                    try
                    {
                        var result = await _ethSender.SignAndBroadcastAsync(quote, priv);
                        // The L2 rollups all report Symbol "ETH", so the explorer is resolved by the
                        // quote's chain id, not its symbol — otherwise an Arbitrum tx would link to
                        // etherscan (mainnet) instead of arbiscan.
                        var explorer = EthTransactionSender.ExplorerTxForChainId(quote.ChainId) + result.TxHash;
                        await FinishSendAsync(result.Ok, result.TxHash, result.Error,
                            quote.Symbol, quote.AmountEth, quote.To, explorer);
                    }
                    finally
                    {
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(priv);
                    }

                    break;
                }

                case "BTC" or "LTC" or "DOGE" or "BCH" when _btcQuote is not null && _btcPlan is not null && _btcRequest is not null:
                {
                    var quote = _btcQuote;
                    var walletId = _registry.Active?.Id ?? "default";
                    // Signs across every input address in the plan and reserves the internal change
                    // index (persisted before broadcast) — no key #0 assumption.
                    var (ok, txid, error) = await _btcSender.SignAndBroadcastHdAsync(
                        _unlockedMnemonic!, walletId, _addrIndex, _btcPlanSymbol ?? quote.Symbol, _btcPlan, _btcRequest);
                    // Force a fresh scan next time so the spent inputs and new change are reflected.
                    // The cooldown stamp goes with it: change landing on an internal address is exactly
                    // the case the user must not have to wait ten minutes to see.
                    var spentSymbol = _btcPlanSymbol ?? quote.Symbol;
                    _utxoScans.Remove(spentSymbol);
                    _lastUtxoScan.Remove(spentSymbol);
                    var explorer = _sendSymbol switch
                    {
                        "BTC" => $"blockstream.info/tx/{txid}",
                        "DOGE" => $"live.blockcypher.com/doge/tx/{txid}",
                        "BCH" => $"blockchair.com/bitcoin-cash/transaction/{txid}",
                        _ => $"litecoinspace.org/tx/{txid}",
                    };
                    await FinishSendAsync(ok, txid, error, quote.Symbol, quote.Amount, quote.To, explorer);
                    break;
                }

                case "SOL" when _solQuote is not null:
                {
                    var quote = _solQuote;
                    var priv = _deriver.DeriveSolanaPrivateKey(_unlockedMnemonic!);
                    try
                    {
                        var (ok, signature, error) = await _solSender.SignAndBroadcastAsync(quote, priv);
                        await FinishSendAsync(ok, signature, error,
                            "SOL", quote.AmountSol, quote.To, $"solscan.io/tx/{signature}");
                    }
                    finally
                    {
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(priv);
                    }

                    break;
                }

                case "TRX" or "USDT" when _tronQuote is not null:
                {
                    var quote = _tronQuote;
                    var key = _deriver.DeriveTronKey(_unlockedMnemonic!);
                    var (ok, txId, error) = await _tronSender.SignAndBroadcastAsync(quote, key);
                    await FinishSendAsync(ok, txId, error, quote.Symbol, quote.Amount, quote.To,
                        txId is null ? "" : $"tronscan.org/#/transaction/{txId}");
                    break;
                }

                case "TON" when _tonQuote is not null:
                {
                    var quote = _tonQuote;
                    // A TON-native wallet signs with the TON-mnemonic seed; a BIP39 wallet uses its
                    // m/44'/607'/0' key. Both are the 32-byte ed25519 seed the sender expects.
                    var priv = _isTonWallet
                        ? TonMnemonic.ToSeed(_unlockedMnemonic!)
                        : _deriver.DeriveTonPrivateKey(_unlockedMnemonic!);
                    try
                    {
                        var (ok, _, error) = await _tonSender.SignAndBroadcastAsync(quote, priv);
                        await FinishSendAsync(ok, ok ? quote.To : null, error,
                            "TON", quote.AmountTon, quote.To, $"tonviewer.com/{quote.From}");
                    }
                    finally
                    {
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(priv);
                    }

                    break;
                }

                case "ADA" when _adaQuote is not null:
                {
                    var quote = _adaQuote;
                    var extendedKey = AdaKeys.PaymentKey(_unlockedMnemonic!);
                    try
                    {
                        var (ok, txId, error) = await _adaSender.SignAndBroadcastAsync(quote, extendedKey);
                        await FinishSendAsync(ok, txId, error, "ADA", quote.Amount, quote.To,
                            txId is null ? "" : $"cardanoscan.io/transaction/{txId}");
                    }
                    finally
                    {
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(extendedKey);
                    }

                    break;
                }

                case "XMR":
                {
                    // monero-wallet-rpc builds, signs and relays the RingCT transaction itself.
                    // The developer fee (if any) rides along as a second destination — disclosed above.
                    var result = await _monero.SendAsync(_moneroTo, _moneroAmount, _moneroFeeTo, _moneroFeeAmount);
                    await FinishSendAsync(result.Ok, result.TxHash, result.Error,
                        "XMR", _moneroAmount, _moneroTo,
                        result.TxHash is null ? "" : $"xmrchain.net/tx/{result.TxHash}");
                    if (result.Ok)
                    {
                        _moneroAmount = 0;
                        _moneroTo = string.Empty;
                        _moneroFeeTo = null;
                        _moneroFeeAmount = 0m;
                        await RefreshMoneroAsync();
                    }

                    break;
                }

                default:
                    SendError = Loc.Instance["send.errPrepareFirst"];
                    break;
            }
        });
    }

    private async Task FinishSendAsync(
        bool ok, string? reference, string? error, string symbol, decimal amount, string to, string explorer)
    {
        if (ok && reference is not null)
        {
            ClearSendQuotes();
            SendTo = string.Empty;
            SendAmount = string.Empty;
            SendSuccess = $"Broadcast ✓  {reference}\nTrack it: {explorer}";
            StatusMessage = Loc.Instance["status.txBroadcast"];
            var link = string.IsNullOrWhiteSpace(explorer) ? null
                : explorer.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? explorer : $"https://{explorer}";
            // Just broadcast, not yet mined — mark it Pending so the feed is honest until it confirms.
            PushActivity("Sent", symbol, $"-{Fmt(amount)}", Shorten(to), "now", link, "Pending");
            await RefreshLiveDataAsync();
        }
        else
        {
            SendError = error ?? Loc.Instance["send.errBroadcast"];
            StatusMessage = Loc.Instance["status.broadcastFailed"];
            // A failed broadcast never left this device, so record it as retryable (full destination and
            // amount kept in retry context, not shown, so Retry can safely re-open a pre-filled send).
            PushActivity("Sent", symbol, $"-{Fmt(amount)}", Shorten(to), "now", null, "Failed",
                retryTo: to, retryAmount: amount.ToString(CultureInfo.InvariantCulture), retryChain: symbol);
        }
    }

    private void ClearSendQuotes()
    {
        HasSendQuote = false;
        ClearSendSimulation();
        _sendQuote = null;
        _btcQuote = null;
        _solQuote = null;
        _tronQuote = null;
        _tonQuote = null;
        _adaQuote = null;
    }

    [RelayCommand]
    private void CancelSendQuote()
    {
        ClearSendQuotes();
        SendError = string.Empty;
        StatusMessage = Loc.Instance["status.transferCancelled"];
    }
}
