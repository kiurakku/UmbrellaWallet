using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>One line of the post-send report: what happened, and which way it cut.</summary>
public sealed record LeakFindingVm(string Text, bool IsProtected)
{
    public string Glyph => IsProtected ? "✓" : "•";
    public string Color => IsProtected ? "#8FCB9B" : "#E7CA83";
}

/// <summary>
/// Roadmap P1.12 — "what leaked?", said right after the send instead of never.
///
/// The Security Center tells the user what they configured. It cannot tell them what the transfer
/// they just made actually cost, and those are different questions: Tor can be on and the spend can
/// still have tied four of their addresses together, permanently and in public, because that is what
/// spending from four addresses does on a transparent chain.
///
/// So the report is about THAT transfer, in both directions, while it can still change what they do
/// next: pick different coins, turn Tor on first, stop handing out an address they already used.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The lines of the report for the last completed send.</summary>
    public ObservableCollection<LeakFindingVm> SendLeakFindings { get; } = new();

    [ObservableProperty] private bool _hasSendLeakReport;

    /// <summary>True when at least one line was something the user could have done differently.</summary>
    [ObservableProperty] private bool _sendLeakHasAvoidable;

    [RelayCommand]
    private void DismissSendLeakReport()
    {
        SendLeakFindings.Clear();
        HasSendLeakReport = false;
    }

    /// <summary>
    /// Builds the report from what the wallet actually knows about the send it has just broadcast.
    ///
    /// Every signal here is a fact, not an estimate. Where the wallet does not know something it says
    /// nothing rather than guessing — a made-up number in a privacy report is worse than no report,
    /// because it is a number somebody will act on.
    /// </summary>
    private void BuildSendLeakReport(string symbol)
    {
        var sym = (symbol ?? string.Empty).Trim().ToUpperInvariant();

        // How many of the user's own addresses funded this spend. Known exactly for the UTXO chains
        // (the plan names its inputs); an account chain spends from the one address it has.
        var inputAddresses = _btcPlan is not null && string.Equals(_btcPlanSymbol, sym, StringComparison.OrdinalIgnoreCase)
            ? _btcPlan.Inputs.Select(i => i.Address).Distinct().Count()
            : 1;

        var isUtxo = UtxoScanChains.Contains(sym, StringComparer.OrdinalIgnoreCase);

        var signals = new SendLeakSignals(
            RoutedThroughTor: !string.IsNullOrEmpty(Umbrella.Wallet.Infrastructure.Network.PublicHttp.ActiveProxy),
            KillSwitchArmed: TorOnly,
            InputAddressCount: inputAddresses,
            ChainHidesAmounts: sym == "XMR",
            // Change goes to a fresh internal address on every UTXO spend that needs one; on an
            // account chain there is no change output at all, so there is nothing to reuse.
            FreshChangeUsed: !isUtxo || _btcPlan is null || _btcPlan.NeedsChange,
            CoinControlUsed: CoinControlOn && isUtxo);

        var findings = SendLeakReport.Build(signals);

        SendLeakFindings.Clear();
        foreach (var f in findings)
        {
            var text = Loc.Instance["leak." + f.Code];
            // The linkage line carries the number, because "several" is exactly the vagueness that
            // lets somebody assume it was two when it was nine.
            if (f.Count > 0 && text.Contains("{0}", StringComparison.Ordinal))
                text = string.Format(text, f.Count);

            SendLeakFindings.Add(new LeakFindingVm(text, f.IsProtected));
        }

        SendLeakHasAvoidable = SendLeakReport.HasAvoidableExposure(findings);
        HasSendLeakReport = SendLeakFindings.Count > 0;
    }
}
