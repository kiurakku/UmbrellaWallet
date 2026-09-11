namespace Umbrella.Wallet.Core.Safety;

/// <summary>What a third party on the other end of a request can work out about you.</summary>
[Flags]
public enum CounterpartyLearns
{
    Nothing = 0,

    /// <summary>The IP that connected — your home address, or an exit node when Tor is on.</summary>
    YourIpAddress = 1 << 0,

    /// <summary>
    /// Your actual wallet addresses. This is the heavy one: on a transparent chain, whoever answers
    /// "what is the balance of bc1q…" now knows that address is in somebody's wallet, and can tie every
    /// address you ask about in one session to the same person.
    /// </summary>
    YourWalletAddresses = 1 << 1,

    /// <summary>Which coins or tokens you hold or opened, from the symbols asked about.</summary>
    WhichCoinsYouHold = 1 << 2,

    /// <summary>That this wallet was running, and when.</summary>
    WhenYouAreOnline = 1 << 3,

    /// <summary>That a transaction entered the network through your connection. Not who you paid on a
    /// private chain — but on a transparent one, the transaction itself says that.</summary>
    WhereYourTransactionEntered = 1 << 4,

    /// <summary>Credentials you chose to give it, because it is your account on that service.</summary>
    YourAccountWithThem = 1 << 5,
}

/// <summary>When, and whether, the wallet reaches out on its own.</summary>
public enum CounterpartyContact
{
    /// <summary>Contacted by the wallet without being asked — on unlock, on a refresh, on a timer.</summary>
    Automatic,

    /// <summary>Only when you do the specific thing: send, swap, check for an update.</summary>
    OnDemand,

    /// <summary>Only if you connect that account yourself. Nothing is sent otherwise.</summary>
    OptIn,

    /// <summary>Never contacted by the wallet. It is a link; your browser goes there if you click it.</summary>
    LinkOnly,
}

/// <summary>What the wallet uses a counterparty for.</summary>
public enum CounterpartyPurpose
{
    /// <summary>Reading balances and unspent coins.</summary>
    Balances,
    /// <summary>Reading transaction history.</summary>
    History,
    /// <summary>Handing a signed transaction to the network.</summary>
    Broadcast,
    /// <summary>Prices and fiat rates.</summary>
    Prices,
    /// <summary>Swap quotes and routing.</summary>
    Swaps,
    /// <summary>Your own exchange account, if you connect one.</summary>
    ExchangeAccount,
    /// <summary>Checking whether a newer Umbrella exists.</summary>
    Updates,
    /// <summary>Confirming Tor is actually carrying the traffic.</summary>
    TorCheck,
    /// <summary>A block explorer opened in your browser.</summary>
    ExplorerLink,
}

/// <summary>
/// One server the wallet can talk to, and what it learns when it does.
///
/// A self-custody wallet holds the keys locally, which is the part everyone says out loud. The part
/// usually left unsaid is that it still has to ASK somebody what is on the chain — and on a
/// transparent chain, asking means handing over the very addresses the chain publishes. The node
/// picker for Monero made that choice visible for one coin. This makes it visible for all of them.
/// </summary>
public sealed record NetworkCounterparty(
    string Host,
    string Operator,
    CounterpartyPurpose Purpose,
    CounterpartyContact Contact,
    CounterpartyLearns Learns,
    string Chains = "")
{
    /// <summary>True when Tor removes the IP from what this party sees. It removes nothing else —
    /// an address handed to an explorer over Tor is still that address.</summary>
    public bool TorHelps => Learns.HasFlag(CounterpartyLearns.YourIpAddress);

    /// <summary>True when this party is handed your actual wallet addresses — the leak Tor does not
    /// fix and the one most worth choosing deliberately.</summary>
    public bool SeesAddresses => Learns.HasFlag(CounterpartyLearns.YourWalletAddresses);
}
