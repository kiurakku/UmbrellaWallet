using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Psbt;
using Umbrella.Wallet.Core.Utxo;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// PSBT in both directions (roadmap H.1).
///
/// Out: the reviewed Bitcoin payment as an unsigned PSBT, for checking in another wallet or signing
/// elsewhere. In: a PSBT from somewhere else, reviewed against what this wallet has seen on-chain
/// itself, with only this wallet's own coins signed — at the values its scan read, never at values
/// the PSBT states.
/// </summary>
public partial class MainViewModel
{
    private HdUtxoSpender? _psbtSpenderCache;
    private HdUtxoSpender PsbtSpender => _psbtSpenderCache ??= new HdUtxoSpender(_deriver);

    // --- export (Send review) -----------------------------------------------------------------------

    private PSBT? _exportedPsbt;

    [ObservableProperty] private string _psbtExportText = string.Empty;

    public bool HasPsbtExport => PsbtExportText.Length > 0;

    partial void OnPsbtExportTextChanged(string value) => OnPropertyChanged(nameof(HasPsbtExport));

    /// <summary>Only a Bitcoin quote can be exported; PSBT is Bitcoin's format.</summary>
    public bool CanExportSendPsbt =>
        HasSendQuote && _btcQuote is not null && _btcPlan is not null && _btcRequest is not null &&
        string.Equals(_btcPlanSymbol, "BTC", StringComparison.OrdinalIgnoreCase);

    partial void OnHasSendQuoteChanged(bool value)
    {
        OnPropertyChanged(nameof(CanExportSendPsbt));
        if (!value)
        {
            _exportedPsbt = null;
            PsbtExportText = string.Empty;
        }
    }

    [RelayCommand]
    private void ExportSendPsbt()
    {
        if (!CanExportSendPsbt || _unlockedMnemonic is null) return;

        var walletId = _registry.Active?.Id ?? "default";
        var (psbt, error) = _btcSender.ExportUnsignedPsbt(
            _unlockedMnemonic, walletId, _addrIndex, "BTC", _btcPlan!, _btcRequest!);

        if (psbt is null)
        {
            SendError = error ?? Loc.Instance["send.errPrepareFailed"];
            return;
        }

        _exportedPsbt = psbt;
        PsbtExportText = psbt.ToBase64();
        StatusMessage = Loc.Instance["send.exportPsbtDone"];
    }

    [RelayCommand]
    private async Task CopyPsbtExportAsync()
    {
        if (HasPsbtExport) await CopyTextAsync(PsbtExportText);
    }

    [RelayCommand]
    private async Task SavePsbtExportAsync()
    {
        if (_exportedPsbt is null) return;
        await SavePsbtAsync(_exportedPsbt, "umbrella-payment.psbt");
    }

    // --- import, review, sign (Security) ------------------------------------------------------------

    private PSBT? _psbtParsed;
    private PsbtReview? _psbtReview;
    private PSBT? _psbtSigned;
    private UtxoScanResult? _psbtScan;

    [ObservableProperty] private string _psbtInput = string.Empty;
    [ObservableProperty] private string _psbtError = string.Empty;
    [ObservableProperty] private string _psbtSummary = string.Empty;
    [ObservableProperty] private string _psbtResultText = string.Empty;
    [ObservableProperty] private string _psbtResultNote = string.Empty;
    [ObservableProperty] private bool _psbtComplete;

    public ObservableCollection<string> PsbtLines { get; } = new();
    public ObservableCollection<string> PsbtProblems { get; } = new();
    public ObservableCollection<string> PsbtWarnings { get; } = new();

    public bool HasPsbtReview => _psbtReview is not null;
    public bool CanSignPsbt => _psbtReview is { CanSign: true } && _psbtSigned is null;
    public bool HasPsbtResult => PsbtResultText.Length > 0;
    public bool HasPsbtProblems => PsbtProblems.Count > 0;
    public bool HasPsbtWarnings => PsbtWarnings.Count > 0;

    partial void OnPsbtInputChanged(string value) => ResetPsbtReview();

    partial void OnPsbtResultTextChanged(string value) => OnPropertyChanged(nameof(HasPsbtResult));

    private void ResetPsbtReview()
    {
        _psbtParsed = null;
        _psbtReview = null;
        _psbtSigned = null;
        PsbtLines.Clear();
        PsbtProblems.Clear();
        PsbtWarnings.Clear();
        PsbtSummary = string.Empty;
        PsbtResultText = string.Empty;
        PsbtResultNote = string.Empty;
        PsbtComplete = false;
        NotifyPsbtState();
    }

    private void NotifyPsbtState()
    {
        OnPropertyChanged(nameof(HasPsbtReview));
        OnPropertyChanged(nameof(CanSignPsbt));
        OnPropertyChanged(nameof(HasPsbtProblems));
        OnPropertyChanged(nameof(HasPsbtWarnings));
    }

    [RelayCommand]
    private async Task LoadPsbtFileAsync()
    {
        if (PickFileAsync is null) return;
        PsbtError = string.Empty;

        var path = await PickFileAsync(string.Empty, false, "psbt");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            var info = new FileInfo(path);
            if (info.Length > PsbtCodec.MaxBytes)
            {
                PsbtError = "The file is too large to be a PSBT.";
                return;
            }

            if (!PsbtCodec.TryRead(await File.ReadAllBytesAsync(path), out var psbt, out var error) || psbt is null)
            {
                PsbtError = error ?? "This is not a PSBT.";
                return;
            }

            PsbtInput = psbt.ToBase64();
            await ReviewPsbtAsync();
        }
        catch (Exception ex)
        {
            PsbtError = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ReviewPsbtAsync()
    {
        var input = PsbtInput;
        ResetPsbtReview();
        PsbtError = string.Empty;

        if (!IsUnlocked || _unlockedMnemonic is null)
        {
            PsbtError = Loc.Instance["send.errUnlock"];
            return;
        }

        if (!PsbtCodec.TryRead(input, out var psbt, out var error) || psbt is null)
        {
            PsbtError = error ?? "This is not a PSBT.";
            return;
        }

        // The wallet signs only what its own scan has seen, so it needs a complete one.
        var walletId = _registry.Active?.Id ?? "default";
        if (!_utxoScans.TryGetValue("BTC", out var scan) || scan is null || scan.Partial)
        {
            try
            {
                scan = await _utxoScanner.ScanAsync(
                    _unlockedMnemonic, ChainId.Btc, UtxoExplorerFor("BTC"), _addrIndex.FloorsFor(walletId, "BTC"));
                if (!scan.Partial) _utxoScans["BTC"] = scan;
            }
            catch
            {
                scan = null;
            }
        }

        if (scan is null || scan.Partial)
        {
            PsbtError = Loc.Instance["psbt.errNoScan"];
            return;
        }

        var own = OwnScripts.For(_deriver, _unlockedMnemonic, ChainId.Btc, _addrIndex.FloorsFor(walletId, "BTC"));
        var review = PsbtReviewer.Review(psbt, scan.Utxos, own, Network.Main);

        _psbtParsed = psbt;
        _psbtReview = review;
        _psbtScan = scan;

        foreach (var i in review.Inputs)
        {
            var value = i.ValueSat is { } v ? $"{Fmt(v / 100_000_000m)} BTC" : Loc.Instance["psbt.unknownValue"];
            var whose = i.Ours ? Loc.Instance["psbt.yours"] : Loc.Instance["psbt.notYours"];
            PsbtLines.Add($"{Loc.Instance["psbt.in"]}   {value}   {i.Address ?? i.Outpoint.ToString()}   ({whose})");
        }

        foreach (var o in review.Outputs)
        {
            var whose = o.Ours ? $"   ({Loc.Instance["psbt.backToYou"]})" : string.Empty;
            PsbtLines.Add($"{Loc.Instance["psbt.out"]}   {Fmt(o.ValueSat / 100_000_000m)} BTC   {o.Destination}{whose}");
        }

        var summary = string.Format(Loc.Instance["psbt.cost"], Fmt(review.NetCostSat / 100_000_000m));
        if (review.FeeSat is { } fee)
        {
            summary += "\n" + (review.FeeRateSatPerVByte is { } rate
                ? string.Format(Loc.Instance["psbt.fee"], fee, rate.ToString("0.#"))
                : string.Format(Loc.Instance["psbt.feeOnly"], fee));
        }

        if (review.SignableInputs == 0 && review.Problems.Count == 0)
            summary += "\n" + Loc.Instance["psbt.nothingToSign"];

        PsbtSummary = summary;
        foreach (var p in review.Problems) PsbtProblems.Add(p);
        foreach (var w in review.Warnings) PsbtWarnings.Add(w);
        NotifyPsbtState();
    }

    [RelayCommand]
    private void SignPsbt()
    {
        if (_psbtParsed is null || _psbtReview is not { CanSign: true } || _psbtScan is null || _unlockedMnemonic is null)
            return;

        var (signed, count, complete, error) = PsbtSpender.SignOwnInputs(_unlockedMnemonic, _psbtParsed, _psbtScan.Utxos);
        if (signed is null)
        {
            PsbtError = error ?? "Signing failed.";
            return;
        }

        _psbtSigned = signed;
        PsbtComplete = complete;
        PsbtResultText = signed.ToBase64();
        PsbtResultNote = string.Format(Loc.Instance["psbt.signed"], count) + " " +
                         Loc.Instance[complete ? "psbt.complete" : "psbt.incomplete"];
        NotifyPsbtState();
    }

    [RelayCommand]
    private async Task BroadcastPsbtAsync()
    {
        if (_psbtSigned is null || !PsbtComplete) return;

        // The same route check as every send: a broadcast is the request that ties an IP to coins.
        if (TransportGateError() is { } gate)
        {
            PsbtError = gate;
            return;
        }

        Transaction tx;
        try { tx = _psbtSigned.ExtractTransaction(); }
        catch (Exception ex) { PsbtError = ex.Message; return; }

        await RunBusyAsync(async () =>
        {
            var (ok, txid, error) = await _btcSender.BroadcastSignedAsync("BTC", tx);
            if (ok)
            {
                _utxoScans.Remove("BTC");
                _lastUtxoScan.Remove("BTC");
                PsbtResultNote = string.Format(Loc.Instance["psbt.broadcastDone"], txid);
                PsbtComplete = false;   // done: no second broadcast button
                StatusMessage = Loc.Instance["status.txBroadcast"];
            }
            else
            {
                PsbtError = error ?? Loc.Instance["send.errBroadcast"];
            }
        });
    }

    [RelayCommand]
    private async Task CopyPsbtResultAsync()
    {
        if (HasPsbtResult) await CopyTextAsync(PsbtResultText);
    }

    [RelayCommand]
    private async Task SavePsbtResultAsync()
    {
        if (_psbtSigned is null) return;
        await SavePsbtAsync(_psbtSigned, "umbrella-signed.psbt");
    }

    /// <summary>Writes the BIP-174 binary form, which every PSBT-aware wallet opens.</summary>
    private async Task SavePsbtAsync(PSBT psbt, string suggestedName)
    {
        if (PickFileAsync is null) return;

        var path = await PickFileAsync(suggestedName, true, "psbt");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            await File.WriteAllBytesAsync(path, psbt.ToBytes());
            StatusMessage = Loc.Instance["psbt.saved"];
        }
        catch (Exception ex)
        {
            PsbtError = ex.Message;
        }
    }
}
