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
using Umbrella.Wallet.Core.Seed;
using Umbrella.Wallet.Core.Utxo;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// The activity feed: local events, on-chain history, the filters over them, and explorer links.
///
/// Split out of MainViewModel.cs (roadmap §8.3.1) as a partial class: the code is unchanged
/// and still one type, so nothing about behaviour moved with it — only the file it lives in.
/// </summary>
public partial class MainViewModel
{
    private void PushActivity(string kind, string asset, string amount, string counter, string when,
        string? explorer = null, string status = "Confirmed",
        string? retryTo = null, string? retryAmount = null, string? retryChain = null)
    {
        // UI-preference changes (theme, currency, language, layout, animations…) are not wallet events —
        // logging them turned the activity feed into a developer log. Only real events (transactions,
        // security actions) belong here, so Settings-category entries are dropped.
        if (string.Equals(kind, "Settings", StringComparison.OrdinalIgnoreCase)) return;

        // Real timestamp so persisted history reads correctly after a restart (callers pass "now").
        var isNow = string.Equals(when, "now", StringComparison.OrdinalIgnoreCase);
        var stamp = isNow
            ? DateTime.Now.ToString("MMM d · HH:mm", Fx.Culture)
            : when;
        var unixMs = isNow ? DateTimeOffset.Now.ToUnixTimeMilliseconds() : 0;
        Activity.Insert(0, new ActivityRowViewModel(kind, asset, amount, counter, stamp, explorer,
            status, unixMs, retryTo, retryAmount, retryChain));
        while (Activity.Count > 60) Activity.RemoveAt(Activity.Count - 1);

        RebuildRecentActivity();
        RebuildActivityAssets();
        RebuildFilteredActivity();
        RebuildTransactions();
        OnPropertyChanged(nameof(HasActivity));
        PersistActivity();
    }

    /// <summary>The Portfolio rail's five most-recent events — from the MERGED feed (local + on-chain),
    /// so real transactions surface there too, not just in-app actions.</summary>
    private void RebuildRecentActivity()
    {
        RecentActivity.Clear();
        foreach (var row in MergedActivity().Take(5)) RecentActivity.Add(row);
        OnPropertyChanged(nameof(HasRecentActivity));
    }

    /// <summary>False until there is something to list in the home screen's recent transactions.</summary>
    public bool HasRecentActivity => RecentActivity.Count > 0;

    private void PersistActivity() =>
        _activityStore.Save(Activity.Select(a =>
            new ActivityStore.Entry(a.Kind, a.Asset, a.Amount, a.Counterparty, a.When, a.Explorer, a.Status)));

    /// <summary>Loads the saved activity/transaction history from the data folder into the feeds.</summary>
    private void LoadActivity()
    {
        Activity.Clear();
        var droppedNoise = false;
        ActivityStore.Entry? prev = null;
        foreach (var e in _activityStore.Load())
        {
            // Purge legacy UI-preference noise that older builds logged ("Theme Appearance changed",
            // "Settings … changed") so the feed reads as real wallet events, not a developer log.
            if (string.Equals(e.Kind, "Theme", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.Kind, "Settings", StringComparison.OrdinalIgnoreCase))
            {
                droppedNoise = true;
                continue;
            }
            // Collapse a run of identical LOCAL events (e.g. repeated "Vault unlocked" from each launch)
            // into a single row. On-chain rows carry an explorer link, so distinct transactions with the
            // same amount are never merged.
            if (prev is not null
                && string.IsNullOrEmpty(e.Explorer) && string.IsNullOrEmpty(prev.Explorer)
                && string.Equals(prev.Kind, e.Kind, StringComparison.OrdinalIgnoreCase)
                && string.Equals(prev.Asset, e.Asset, StringComparison.OrdinalIgnoreCase)
                && string.Equals(prev.Amount, e.Amount, StringComparison.OrdinalIgnoreCase)
                && string.Equals(prev.Counterparty, e.Counterparty, StringComparison.OrdinalIgnoreCase))
            {
                droppedNoise = true;
                prev = e;
                continue;
            }
            Activity.Add(new ActivityRowViewModel(e.Kind, e.Asset, e.Amount, e.Counterparty, e.When, e.Explorer, e.Status));
            prev = e;
        }
        if (droppedNoise) PersistActivity(); // rewrite the cleaned history so the noise never returns
        RebuildRecentActivity();
        RebuildActivityAssets();
        RebuildFilteredActivity();
        RebuildTransactions();
        OnPropertyChanged(nameof(HasActivity));
    }

    public bool HasFilteredActivity => FilteredActivity.Count > 0;

    // --- Honest coverage (roadmap P1.10) --------------------------------------------------------
    // The feed shows what this wallet did plus whatever history an explorer will give us. For a chain
    // with no history reader, a transaction made anywhere else — or before this wallet existed — is
    // simply not here. An empty feed then reads as "nothing happened" when it means "nobody asked",
    // which is the same shape of lie as a zero balance on an unreachable explorer.

    /// <summary>"DOGE, ZEC" — the coins this wallet holds whose history is not read.</summary>
    [ObservableProperty] private string _historyGapCoins = string.Empty;

    public bool HasHistoryGaps => HistoryGapCoins.Length > 0;

    /// <summary>The sentence shown under the Activity header, naming those coins.</summary>
    public string HistoryGapNote =>
        HasHistoryGaps ? string.Format(Loc.Instance["activity.partial"], HistoryGapCoins) : string.Empty;

    partial void OnHistoryGapCoinsChanged(string value)
    {
        OnPropertyChanged(nameof(HasHistoryGaps));
        OnPropertyChanged(nameof(HistoryGapNote));
    }

    /// <summary>
    /// Recomputes which held coins have no history reader, from the capability catalog — so this can
    /// never claim coverage the code does not have, and never keep warning about a chain once one is
    /// wired up.
    /// </summary>
    private void RefreshHistoryCoverage()
    {
        var held = Accounts
            .Where(a => a.SupportStatus is "Ready" or "Receive only" && IsRealAddress(a.Address))
            .Select(a => a.Symbol);

        HistoryGapCoins = string.Join(", ", HistoryCoverage.WithoutHistory(held));
    }

    /// <summary>The merged feed (roadmap §6): local events plus real on-chain history, deduped by explorer
    /// link, newest first — the single source the Activity screen renders and every filter narrows.</summary>
    private IEnumerable<ActivityRowViewModel> MergedActivity()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Activity)
        {
            if (row.Explorer is { Length: > 0 } ex) seen.Add(ex);
            yield return row;
        }
        foreach (var row in _onChainRows)
        {
            if (row.Explorer is { Length: > 0 } ex && !seen.Add(ex)) continue;
            yield return row;
        }
    }

    /// <summary>Rebuilds the asset dropdown from whatever assets the feed currently holds, keeping "All"
    /// first and dropping a selection that no longer exists.</summary>
    private void RebuildActivityAssets()
    {
        var assets = MergedActivity()
            .Where(a => a.IsTransaction && !string.IsNullOrWhiteSpace(a.Asset))
            .Select(a => a.Asset)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ActivityAssets.Clear();
        ActivityAssets.Add("All");
        foreach (var a in assets) ActivityAssets.Add(a);
        if (!ActivityAssets.Contains(ActivityAssetFilter, StringComparer.OrdinalIgnoreCase))
            ActivityAssetFilter = "All";
    }

    private void RebuildFilteredActivity()
    {
        // Date cutoff (unix ms). Rows with an unknown timestamp (0) are always kept — never hide history
        // just because it predates the timestamped format.
        long cutoff = ActivityDateFilter switch
        {
            "Last 24h" => DateTimeOffset.Now.AddDays(-1).ToUnixTimeMilliseconds(),
            "Last 7 days" => DateTimeOffset.Now.AddDays(-7).ToUnixTimeMilliseconds(),
            "Last 30 days" => DateTimeOffset.Now.AddDays(-30).ToUnixTimeMilliseconds(),
            _ => 0,
        };

        FilteredActivity.Clear();
        foreach (var row in MergedActivity())
        {
            if (ActivityFilter != "All" && row.Category != ActivityFilter) continue;
            if (ActivityAssetFilter != "All" &&
                !string.Equals(row.Asset, ActivityAssetFilter, StringComparison.OrdinalIgnoreCase)) continue;
            if (ActivityStatusFilter != "All" &&
                !string.Equals(row.Status, ActivityStatusFilter, StringComparison.OrdinalIgnoreCase)) continue;
            if (cutoff > 0 && row.UnixMs > 0 && row.UnixMs < cutoff) continue;
            FilteredActivity.Add(row);
        }
        OnPropertyChanged(nameof(HasFilteredActivity));
    }

    private void RebuildTransactions()
    {
        Transactions.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Activity.Where(a => a.IsTransaction))
        {
            Transactions.Add(row);
            if (row.Explorer is { Length: > 0 } ex) seen.Add(ex);
        }
        // Real on-chain history for the user's own addresses — skip any already logged locally.
        foreach (var row in _onChainRows)
        {
            if (row.Explorer is { Length: > 0 } ex && !seen.Add(ex)) continue;
            Transactions.Add(row);
        }
        OnPropertyChanged(nameof(HasTransactions));
    }

    /// <summary>Fetches real on-chain transaction history for the user's own addresses, so transactions
    /// made before the wallet was opened still appear. Covers BTC, LTC and BCH across EVERY issued receive
    /// address (not just #0, so funds received on a rotated address still show), plus ETH and TRON
    /// (TRC-20 incl. USDT). Best-effort and keyless; runs through the same Tor/proxy route as balances,
    /// and is deduped by explorer link so a tx seen on two of the user's addresses appears once.</summary>
    private async Task LoadOnChainHistoryAsync()
    {
        if (string.IsNullOrEmpty(_unlockedMnemonic)) return;
        // Decrypt this wallet's private transaction notes first, so each row is built with its note.
        await LoadTxNotesAsync();
        HistoryLoading = true;
        RefreshHistoryCoverage();                       // say up front which coins are not being read
        OnPropertyChanged(nameof(HasFilteredActivity)); // let the "loading" state show immediately
        try
        {
            var rows = new List<(long Ts, ActivityRowViewModel Row)>();
            var walletId = _registry.Active?.Id ?? "default";

            // BTC / LTC / BCH: every address the wallet has used — receive AND change, every branch
            // (Taproot included) — each transaction judged against the whole set, so change coming back
            // is netted out of "sent" and a spend funded only by change still appears (roadmap P0.1).
            // Capped per branch so a huge index never fans out into hundreds of calls.
            foreach (var (sym, chain) in new[] { ("BTC", ChainId.Btc), ("LTC", ChainId.Ltc), ("BCH", ChainId.Bch) })
            {
                HistoryAddressPlan plan;
                try
                {
                    var floors = _addrIndex.FloorsFor(walletId, sym);
                    var own = Umbrella.Wallet.Core.Psbt.OwnScripts.For(_deriver, _unlockedMnemonic!, chain, floors);
                    var (_, _, network, _) = HdAddressDeriver.BitcoinLikeParams(chain);
                    plan = HistoryAddresses.Plan(own, floors, network);
                }
                catch
                {
                    continue;
                }

                foreach (var addr in plan.Query)
                {
                    var txs = sym switch
                    {
                        "BTC" => await _history.GetBitcoinAsync(addr, plan.Own),
                        "LTC" => await _history.GetLitecoinAsync(addr, plan.Own),
                        _ => await _history.GetBitcoinCashAsync(addr, plan.Own),
                    };
                    foreach (var t in txs) rows.Add((t.UnixMs, ToActivityRow(t)));
                }
            }

            // ETH / TRON: single-address chains in this wallet.
            string? tron = null, eth = null;
            try { tron = _deriver.DeriveReceiveAddress(_unlockedMnemonic!, ChainId.Tron).Address; } catch { }
            try { eth = _deriver.DeriveReceiveAddress(_unlockedMnemonic!, ChainId.Eth).Address; } catch { }

            if (!string.IsNullOrEmpty(tron))
            {
                foreach (var t in await _history.GetTronTrc20Async(tron!))
                    rows.Add((t.UnixMs, ToActivityRow(t)));
                foreach (var t in await _history.GetTronNativeAsync(tron!))
                    rows.Add((t.UnixMs, ToActivityRow(t)));
            }

            if (!string.IsNullOrEmpty(eth))
                foreach (var t in await _history.GetEthereumAsync(eth!))
                    rows.Add((t.UnixMs, ToActivityRow(t)));

            // TON: single-address chain (wallet v4R2), history via toncenter.
            string? ton = null;
            try { ton = _deriver.DeriveReceiveAddress(_unlockedMnemonic!, ChainId.Ton).Address; } catch { }
            if (!string.IsNullOrEmpty(ton))
                foreach (var t in await _history.GetTonAsync(ton!))
                    rows.Add((t.UnixMs, ToActivityRow(t)));

            // ADA: single-address chain, history via Koios (keyless).
            string? ada = null;
            try { ada = _deriver.DeriveReceiveAddress(_unlockedMnemonic!, ChainId.Ada).Address; } catch { }
            if (!string.IsNullOrEmpty(ada))
                foreach (var t in await _history.GetCardanoAsync(ada!))
                    rows.Add((t.UnixMs, ToActivityRow(t)));

            // SOL: single-address chain, best-effort history via the public Solana RPC.
            string? sol = null;
            try { sol = _deriver.DeriveReceiveAddress(_unlockedMnemonic!, ChainId.Sol).Address; } catch { }
            if (!string.IsNullOrEmpty(sol))
                foreach (var t in await _history.GetSolanaAsync(sol!))
                    rows.Add((t.UnixMs, ToActivityRow(t)));

            // Dedupe by explorer URL (a tx that touches two of the user's own addresses is one event).
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _onChainRows.Clear();
            foreach (var r in rows.OrderByDescending(r => r.Ts))
            {
                if (r.Row.Explorer is { Length: > 0 } ex && !seen.Add(ex)) continue;
                _onChainRows.Add(r.Row);
            }
            LastHistorySync = DateTime.Now.ToString("MMM d · HH:mm", Fx.Culture);
            RebuildActivityAssets();
            RebuildFilteredActivity();
            RebuildRecentActivity(); // on-chain history just arrived — refresh the Portfolio rail too
            RebuildTransactions();
        }
        catch
        {
            // History is a read-only nicety — never let it disrupt the wallet.
        }
        finally
        {
            HistoryLoading = false;
            HistorySynced = true;
        }
    }

    /// <summary>User-triggered re-fetch of on-chain history, so the Activity feed and its last-sync
    /// stamp can be refreshed on demand (roadmap §6). Best-effort; failures leave the feed untouched.</summary>
    [RelayCommand]
    private async Task RefreshHistory()
    {
        StatusMessage = Loc.Instance["status.refreshingHistory"];
        await LoadOnChainHistoryAsync();
        ShowToast(Loc.Instance["activity.synced"], isError: false);
    }

    /// <summary>Exports the transaction history to a CSV at a location the user picks — for taxes, records
    /// or a spreadsheet. Read-only and fully local: nothing is uploaded; the wallet writes only the file
    /// the user chose. Covers the merged money movements (local + on-chain, deduped), newest first.
    /// Best-effort — a cancelled dialog or a write error just shows a toast, never disrupts the wallet.</summary>
    [RelayCommand]
    private async Task ExportHistoryCsvAsync()
    {
        if (PickFileAsync is null) return;

        var rows = MergedActivity()
            .Where(r => r.IsTransaction)
            .Select(r => new HistoryCsvRow(
                r.When, r.Kind, r.Asset, r.Amount, r.Counterparty, r.Status, r.Explorer ?? string.Empty))
            .ToList();

        if (rows.Count == 0)
        {
            ShowToast(Loc.Instance["activity.exportEmpty"], isError: true);
            return;
        }

        var path = await PickFileAsync(HistoryCsv.SuggestedFileName(), true, "csv");
        if (string.IsNullOrWhiteSpace(path)) return; // the user cancelled the dialog

        try
        {
            var csv = HistoryCsv.Build(rows);
            // UTF-8 with BOM so a spreadsheet (Excel especially) reads non-ASCII labels correctly.
            await System.IO.File.WriteAllTextAsync(path, csv, new System.Text.UTF8Encoding(true));
            ShowToast(Loc.Instance["activity.exportOk"], isError: false);
            StatusMessage = string.Format(Loc.Instance["activity.exportDone"], rows.Count);
        }
        catch
        {
            ShowToast(Loc.Instance["activity.exportFail"], isError: true);
        }
    }

    /// <summary>Re-attempts a failed send. The broadcast never left the device, so this only re-opens the
    /// Send screen pre-filled with the original destination and amount — it deliberately does NOT
    /// auto-broadcast, so a transaction that actually went through can never be sent twice.</summary>
    [RelayCommand]
    private void RetrySend(ActivityRowViewModel? row)
    {
        if (row is null || !row.CanRetry) return;

        var asset = SendableAssets.FirstOrDefault(a =>
            string.Equals(a.Symbol, row.RetryChain, StringComparison.OrdinalIgnoreCase));
        if (asset is not null) SelectedSendAsset = asset;

        SendTo = row.RetryTo ?? string.Empty;
        SendAmount = row.RetryAmount ?? string.Empty;
        SendError = string.Empty;
        HasSendQuote = false;
        SelectSection("Send");
        StatusMessage = Loc.Instance["status.retryPrefilled"];
    }

    private ActivityRowViewModel ToActivityRow(ChainTx t)
    {
        var when = t.UnixMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(t.UnixMs).LocalDateTime.ToString("MMM d, HH:mm", Fx.Culture)
            : "";
        var counter = t.Counterparty.Length > 16
            ? $"{t.Counterparty[..8]}…{t.Counterparty[^6..]}"
            : t.Counterparty;
        // Signed number only; the asset shows in its own column now that Activity is merged.
        var amount = t.Kind == "Sent" ? $"-{t.Amount}" : $"+{t.Amount}";
        // Explorer history is fetched with only_confirmed, so these are settled — Status "Confirmed".
        // TxId carries the transaction hash so a private (encrypted) note can be attached to this row.
        return new ActivityRowViewModel(t.Kind, t.Asset, amount, counter, when, t.Explorer, "Confirmed", t.UnixMs,
            TxId: t.Hash, Note: TxNoteFor(t.Hash));
    }

    /// <summary>Copy a transaction's explorer link to the clipboard — deliberately not opened in
    /// the system browser, which would bypass Tor. Paste it into Tor Browser to view.</summary>
    [RelayCommand]
    private async Task CopyActivityLink(ActivityRowViewModel? row)
    {
        if (row?.Explorer is not { Length: > 0 } url) return;
        await CopyTextAsync(url);
        StatusMessage = Loc.Instance["status.explorerLinkCopied"];
        ShowToast(Loc.Instance["toast.linkCopied"], isError: false);
    }
}
