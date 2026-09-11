using System.Collections.ObjectModel;
using System.Linq;
using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>One server on the "who this wallet talks to" list, already turned into readable text.</summary>
public sealed record CounterpartyRowVm(
    string Host,
    string Operator,
    string Purpose,
    string Contact,
    string Learns,
    string Chains,
    bool SeesAddresses)
{
    public bool HasChains => !string.IsNullOrWhiteSpace(Chains);

    /// <summary>Amber for anything handed your actual addresses — the exposure Tor does not fix — and
    /// muted for the rest, so the list has a shape rather than sixty identical rows.</summary>
    public string Accent => SeesAddresses ? "#E7CA83" : "#8A9099";
}

/// <summary>
/// The list of every server this wallet can talk to, and what each one learns.
///
/// "Your keys never leave your device" is true and every wallet says it. The part usually left unsaid
/// is that the wallet still has to ASK somebody what is on the chain — and on a public chain, asking
/// means handing over the address. The Monero node picker made that visible for one coin. This makes
/// it visible for all of them, including the ones where the answer is uncomfortable.
///
/// The catalog is checked against the source by <c>NetworkCounterpartyTests</c>, so a new endpoint
/// cannot be added without appearing here. A transparency page that can silently fall behind the code
/// is worse than none.
/// </summary>
public partial class MainViewModel
{
    public ObservableCollection<CounterpartyRowVm> Counterparties { get; } = new();

    /// <summary>How many servers the wallet reaches out to on its own.</summary>
    public int AutomaticCounterpartyCount =>
        NetworkCounterpartyCatalog.Automatic.Count();

    /// <summary>How many are handed your actual wallet addresses.</summary>
    public int AddressSeeingCounterpartyCount =>
        NetworkCounterpartyCatalog.SeeingAddresses.Count();

    /// <summary>Rebuilds the list in the current language. Pure and offline — it reads a static table.</summary>
    public void BuildCounterparties()
    {
        var L = Loc.Instance;

        Counterparties.Clear();
        foreach (var party in NetworkCounterpartyCatalog.All
                     .OrderByDescending(c => c.SeesAddresses)
                     .ThenBy(c => c.Contact)
                     .ThenBy(c => c.Host, StringComparer.Ordinal))
        {
            Counterparties.Add(new CounterpartyRowVm(
                NetworkCounterpartyCatalog.HostOf(party),
                party.Operator,
                L[PurposeKey(party.Purpose)],
                L[ContactKey(party.Contact)],
                LearnsText(party.Learns, L),
                party.Chains,
                party.SeesAddresses));
        }

        OnPropertyChanged(nameof(AutomaticCounterpartyCount));
        OnPropertyChanged(nameof(AddressSeeingCounterpartyCount));
    }

    private static string PurposeKey(CounterpartyPurpose purpose) => purpose switch
    {
        CounterpartyPurpose.Balances => "who.p.balances",
        CounterpartyPurpose.History => "who.p.history",
        CounterpartyPurpose.Broadcast => "who.p.broadcast",
        CounterpartyPurpose.Prices => "who.p.prices",
        CounterpartyPurpose.Swaps => "who.p.swaps",
        CounterpartyPurpose.ExchangeAccount => "who.p.exchange",
        CounterpartyPurpose.Updates => "who.p.updates",
        CounterpartyPurpose.TorCheck => "who.p.torCheck",
        _ => "who.p.explorer",
    };

    private static string ContactKey(CounterpartyContact contact) => contact switch
    {
        CounterpartyContact.Automatic => "who.auto",
        CounterpartyContact.OnDemand => "who.onDemand",
        CounterpartyContact.OptIn => "who.optIn",
        _ => "who.linkOnly",
    };

    /// <summary>The flags as one readable sentence fragment, most consequential first.</summary>
    private static string LearnsText(CounterpartyLearns learns, Loc L)
    {
        if (learns == CounterpartyLearns.Nothing) return L["who.l.nothing"];

        var parts = new List<string>();
        // Addresses lead: on a public chain that is the whole exposure, and it is the one Tor cannot
        // take back.
        if (learns.HasFlag(CounterpartyLearns.YourWalletAddresses)) parts.Add(L["who.l.addresses"]);
        if (learns.HasFlag(CounterpartyLearns.YourAccountWithThem)) parts.Add(L["who.l.account"]);
        if (learns.HasFlag(CounterpartyLearns.WhichCoinsYouHold)) parts.Add(L["who.l.coins"]);
        if (learns.HasFlag(CounterpartyLearns.WhereYourTransactionEntered)) parts.Add(L["who.l.txOrigin"]);
        if (learns.HasFlag(CounterpartyLearns.YourIpAddress)) parts.Add(L["who.l.ip"]);
        if (learns.HasFlag(CounterpartyLearns.WhenYouAreOnline)) parts.Add(L["who.l.online"]);

        return string.Join(" · ", parts);
    }
}
