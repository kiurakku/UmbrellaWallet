using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// "Private send" as one switch instead of a checklist nobody remembers.
///
/// Today a careful send means remembering to start Tor, remembering the kill-switch, waiting for
/// bootstrap, and thinking about which coins fund it. Most people do none of that, so most sends are
/// less private than this wallet could have made them. This turns that into a switch.
///
/// The second half of the card is what keeps it honest. Tor hides your IP from a block explorer; it
/// does not make Bitcoin private — the amount and both addresses sit on a public ledger forever, and
/// whoever you are paying still knows they were paid by you. So the limits are shown next to the
/// steps, always, and "nothing left to switch on" never renders as "nothing left to know".
///
/// Nothing here touches the money path: the switch flips settings the user could already flip by
/// hand, and the planner is pure. What gets signed is unchanged.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The switch itself. Off by default — turning it on applies the plan.</summary>
    [ObservableProperty] private bool _privateSendOn;

    /// <summary>What the wallet will do, in the order it has to happen.</summary>
    public ObservableCollection<string> PrivateSendSteps { get; } = new();

    /// <summary>What no switch changes. Never empty.</summary>
    public ObservableCollection<string> PrivateSendLimits { get; } = new();

    /// <summary>True when the steps list is empty — every switch is already on for this send.</summary>
    [ObservableProperty] private bool _privateSendReady;

    /// <summary>True while something in the plan is still waiting to be applied.</summary>
    public bool PrivateSendHasSteps => PrivateSendSteps.Count > 0;

    private bool _applyingPrivateSend;

    /// <summary>Current wallet + chain state, as the planner wants it.</summary>
    private PrivateSendContext CurrentPrivateSendContext()
    {
        var symbol = (SelectedSendAsset?.Symbol ?? SendChain ?? string.Empty).Trim().ToUpperInvariant();

        return new PrivateSendContext(
            TorEnabled: TorEnabled,
            TorBootstrapped: _tor.IsRunning && _tor.BootstrapPercent >= 100,
            KillSwitchEnabled: TorOnly,
            // Monero is the only chain here that hides the amount. Everything else is a public ledger,
            // and saying otherwise is the one mistake this feature must never make.
            ChainHidesAmounts: symbol == "XMR",
            IsUtxoChain: IsUtxoSendChain(symbol),
            // How many of the user's addresses this spend would draw on. Before a quote exists the
            // planner has not chosen inputs yet, so 1 is the honest floor: it under-claims linkage
            // rather than inventing it, and the review recomputes from the real plan.
            InputAddressCount: _btcPlan is not null && _btcPlanSymbol == symbol
                ? _btcPlan.Inputs.Select(i => i.Address).Distinct().Count()
                : 1,
            // Whether the DESTINATION has a history is something only its chain can answer, and this
            // runs offline. Claiming reuse without evidence would be a lie in the expensive direction,
            // so it stays false until the wallet actually knows.
            RecipientAddressReused: false);
    }

    /// <summary>Rebuilds the plan from current state. Pure and offline — safe to call on any change.</summary>
    public void RefreshPrivateSendPlan()
    {
        var plan = PrivateSendPlanner.Plan(CurrentPrivateSendContext());
        var L = Loc.Instance;

        PrivateSendSteps.Clear();
        foreach (var step in plan.Steps)
        {
            PrivateSendSteps.Add(L[step switch
            {
                PrivateSendStep.EnableTor => "psend.stepTor",
                PrivateSendStep.WaitForTor => "psend.stepWait",
                PrivateSendStep.EnableKillSwitch => "psend.stepKill",
                PrivateSendStep.NarrowInputs => "psend.stepInputs",
                PrivateSendStep.FreshChangeAddress => "psend.stepChange",
                _ => "psend.stepChange",
            }]);
        }

        PrivateSendLimits.Clear();
        foreach (var limit in plan.Limits)
        {
            PrivateSendLimits.Add(L[limit switch
            {
                PrivateSendLimit.LedgerIsPublicForever => "psend.limPublic",
                PrivateSendLimit.InputsLinkAddresses => "psend.limLink",
                PrivateSendLimit.RecipientAddressIsReused => "psend.limReused",
                PrivateSendLimit.RecipientStillLearnsWhoPaid => "psend.limRecipient",
                _ => "psend.limRecipient",
            }]);
        }

        PrivateSendReady = plan.AlreadyAsPrivateAsPossible;
        OnPropertyChanged(nameof(PrivateSendHasSteps));
    }

    /// <summary>Flipping the switch on applies whatever the plan can apply by itself.</summary>
    partial void OnPrivateSendOnChanged(bool value)
    {
        RefreshPrivateSendPlan();
        if (value && !_applyingPrivateSend) _ = ApplyPrivateSendAsync();
    }

    /// <summary>
    /// Turns on the two things the wallet can switch on for the user: Tor, then the kill-switch.
    /// Order matters — the kill-switch with Tor down blocks every request, so Tor goes first and the
    /// kill-switch only follows once Tor actually came up.
    ///
    /// The rest of the plan is not a setting. Fresh change addresses are what the UTXO planner already
    /// does on every send, and narrowing inputs is the user's own choice about which coins to spend,
    /// so those are stated rather than silently performed.
    /// </summary>
    [RelayCommand]
    private async Task ApplyPrivateSendAsync()
    {
        if (_applyingPrivateSend) return;
        _applyingPrivateSend = true;
        try
        {
            PrivateSendOn = true;

            if (!TorEnabled)
            {
                TorEnabled = true;
                await ApplyTorAsync();
            }

            // ApplyTorAsync puts TorEnabled back to false if the bundle is missing or Tor failed to
            // start. Turning the kill-switch on in that state would block the wallet's own traffic
            // without adding privacy, so it is only armed on a Tor that actually came up.
            if (TorEnabled && !TorOnly) TorOnly = true;

            RefreshPrivateSendPlan();
        }
        finally
        {
            _applyingPrivateSend = false;
        }
    }
}
