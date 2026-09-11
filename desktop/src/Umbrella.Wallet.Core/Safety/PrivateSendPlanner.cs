namespace Umbrella.Wallet.Core.Safety;

/// <summary>Something the wallet can actually do to make this send more private.</summary>
public enum PrivateSendStep
{
    /// <summary>Start the bundled Tor client so explorers and RPCs see an exit node, not your IP.</summary>
    EnableTor,
    /// <summary>Wait for Tor to finish bootstrapping before anything is sent.</summary>
    WaitForTor,
    /// <summary>Turn on the kill-switch so a Tor failure cannot fall back to a direct connection.</summary>
    EnableKillSwitch,
    /// <summary>Narrow the inputs so the spend links fewer of the user's addresses together.</summary>
    NarrowInputs,
    /// <summary>Send the change to a freshly derived address rather than back to the source.</summary>
    FreshChangeAddress,
}

/// <summary>Something that stays true no matter what the wallet does.</summary>
public enum PrivateSendLimit
{
    /// <summary>A transparent chain publishes the amount and both addresses, permanently.</summary>
    LedgerIsPublicForever,
    /// <summary>This spend joins addresses that were not previously known to be one wallet.</summary>
    InputsLinkAddresses,
    /// <summary>The receiving address has been used before, so it already has a history.</summary>
    RecipientAddressIsReused,
    /// <summary>Tor hides the IP from the explorer; it hides nothing from whoever is being paid.</summary>
    RecipientStillLearnsWhoPaid,
}

/// <summary>Everything relevant about the send and the wallet's current settings.</summary>
public readonly record struct PrivateSendContext(
    bool TorEnabled,
    bool TorBootstrapped,
    bool KillSwitchEnabled,
    bool ChainHidesAmounts,
    bool IsUtxoChain,
    int InputAddressCount,
    bool RecipientAddressReused);

public sealed record PrivateSendPlan(
    IReadOnlyList<PrivateSendStep> Steps,
    IReadOnlyList<PrivateSendLimit> Limits)
{
    /// <summary>True when the wallet has nothing left to switch on — it is as private as it gets here.</summary>
    public bool AlreadyAsPrivateAsPossible => Steps.Count == 0;
}

/// <summary>
/// Works out what "send this privately" actually means right now, as an ordered list of things the
/// wallet will do — and, just as importantly, a list of things it cannot.
///
/// The point is to replace a chore with a switch. Today a careful send means remembering to turn on
/// Tor, remembering the kill-switch, waiting for bootstrap, and thinking about which coins fund it.
/// Most people do not, so most sends are less private than the wallet could make them.
///
/// The second list is what keeps this honest. Turning on Tor hides your IP from a block explorer. It
/// does not make Bitcoin private: the amount and both addresses are on a public ledger forever, and
/// whoever you are paying still knows they were paid by you. A feature that implied otherwise would be
/// worse than none, because somebody would rely on it.
/// </summary>
public static class PrivateSendPlanner
{
    /// <summary>More inputs than this and the spend is joining a meaningful number of addresses.</summary>
    public const int InputsWorthNarrowing = 2;

    public static PrivateSendPlan Plan(PrivateSendContext context)
    {
        var steps = new List<PrivateSendStep>();
        var limits = new List<PrivateSendLimit>();

        // Ordered as they must happen: Tor first, because nothing else matters if the request that
        // fetches the fee has already gone out over clearnet.
        if (!context.TorEnabled)
        {
            steps.Add(PrivateSendStep.EnableTor);
            steps.Add(PrivateSendStep.WaitForTor);
        }
        else if (!context.TorBootstrapped)
        {
            steps.Add(PrivateSendStep.WaitForTor);
        }

        if (!context.KillSwitchEnabled) steps.Add(PrivateSendStep.EnableKillSwitch);

        if (context.IsUtxoChain)
        {
            if (context.InputAddressCount > InputsWorthNarrowing) steps.Add(PrivateSendStep.NarrowInputs);
            steps.Add(PrivateSendStep.FreshChangeAddress);
        }

        // What cannot be fixed by any switch.
        if (!context.ChainHidesAmounts)
        {
            limits.Add(PrivateSendLimit.LedgerIsPublicForever);

            if (context.IsUtxoChain && context.InputAddressCount > 1)
                limits.Add(PrivateSendLimit.InputsLinkAddresses);

            if (context.RecipientAddressReused)
                limits.Add(PrivateSendLimit.RecipientAddressIsReused);
        }

        // True on every chain, including Monero: the person being paid knows who paid them.
        limits.Add(PrivateSendLimit.RecipientStillLearnsWhoPaid);

        return new PrivateSendPlan(steps, limits);
    }
}
