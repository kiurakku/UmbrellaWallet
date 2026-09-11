namespace Umbrella.Wallet.Core.Chains;

/// <summary>Why a node cannot be used right now, or <see cref="None"/> when it can.</summary>
public enum MoneroNodeBlock
{
    None,
    /// <summary>An .onion node with Tor switched off. Using it is impossible, not merely unwise.</summary>
    OnionNeedsTor,
    /// <summary>A clearnet node while the Tor-only kill-switch is armed and Tor is down.</summary>
    ClearnetBlockedByKillSwitch,
}

/// <summary>
/// A Monero remote node: the machine that answers "what is on the chain" for this wallet.
///
/// This matters more on Monero than anywhere else in the wallet, and it is the one choice a Monero
/// wallet must not make silently on a user's behalf. The node does NOT learn the keys, the balance,
/// the addresses or the amounts — those stay on this machine, and Monero's amounts are encrypted on
/// the chain itself. What it does learn is real: the IP that connected to it, that the IP belongs to
/// a Monero wallet, roughly which range of blocks that wallet asked for, when it is online, and —
/// when a transaction is submitted — that this connection is where that transaction entered the
/// network. Over Tor, the first of those becomes an exit node instead of a home address.
///
/// So the honest position is not "our node is safe". It is: here is what the node can see, here is
/// what it cannot, pick one you are willing to be seen by, or point at your own.
/// </summary>
public sealed record MoneroNode(string Host, int Port, string Label, string Operator)
{
    /// <summary>Monero's standard restricted-RPC port. A node given without a port means this one.</summary>
    public const int DefaultPort = 18081;

    /// <summary>Reachable only through Tor, by construction — there is no DNS name behind it.</summary>
    public bool IsOnion => Host.EndsWith(".onion", StringComparison.OrdinalIgnoreCase);

    /// <summary>What monero-wallet-rpc wants for <c>--daemon-address</c>.</summary>
    public string Address => $"{Host}:{Port}";

    public override string ToString() => Address;

    /// <summary>
    /// Whether this node can be used under the current network settings — and if not, why.
    ///
    /// Both refusals are fail-closed on purpose. An .onion address simply does not resolve without
    /// Tor, so trying is a guaranteed failure. A clearnet node while the kill-switch is armed and Tor
    /// is down is worse than a failure: it would connect directly and hand a stranger the real IP,
    /// which is the exact outcome the kill-switch exists to prevent.
    /// </summary>
    public MoneroNodeBlock BlockedBecause(bool torConnected, bool killSwitchArmed)
    {
        if (IsOnion && !torConnected) return MoneroNodeBlock.OnionNeedsTor;
        if (!IsOnion && killSwitchArmed && !torConnected) return MoneroNodeBlock.ClearnetBlockedByKillSwitch;
        return MoneroNodeBlock.None;
    }

    public bool CanUse(bool torConnected, bool killSwitchArmed) =>
        BlockedBecause(torConnected, killSwitchArmed) == MoneroNodeBlock.None;

    /// <summary>
    /// Parses a node the user typed: <c>host</c>, <c>host:port</c>, or either with a scheme in front.
    /// Returns false rather than guessing — a mistyped node is a wallet that silently never syncs,
    /// and worse, a hostname that is not the one intended is a stranger answering for the chain.
    /// </summary>
    public static bool TryParse(string? text, out MoneroNode node, string label = "", string operatorName = "")
    {
        node = null!;
        var raw = (text ?? string.Empty).Trim();
        if (raw.Length == 0) return false;

        // Accept a pasted URL, since that is what node operators publish.
        foreach (var scheme in new[] { "http://", "https://", "tcp://" })
        {
            if (raw.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            {
                raw = raw[scheme.Length..];
                break;
            }
        }

        raw = raw.TrimEnd('/');
        if (raw.Contains('/')) return false;           // a path means this is not a bare node address
        if (raw.Contains(' ')) return false;
        if (raw.Contains('@')) return false;           // credentials do not belong in a node address

        var port = DefaultPort;
        var host = raw;

        var colon = raw.LastIndexOf(':');
        if (colon >= 0)
        {
            host = raw[..colon];
            var portText = raw[(colon + 1)..];
            if (!int.TryParse(portText, out port)) return false;
            if (port is < 1 or > 65535) return false;
        }

        if (!IsPlausibleHost(host)) return false;

        node = new MoneroNode(
            host.ToLowerInvariant(), port,
            string.IsNullOrWhiteSpace(label) ? host.ToLowerInvariant() : label.Trim(),
            operatorName.Trim());

        return true;
    }

    /// <summary>A hostname or IPv4 literal. Deliberately strict: anything else is a typo, and a typo
    /// here points the wallet at somebody the user did not choose.</summary>
    private static bool IsPlausibleHost(string host)
    {
        if (host.Length is 0 or > 253) return false;
        if (host.StartsWith('.') || host.EndsWith('.')) return false;
        if (host.StartsWith('-') || host.EndsWith('-')) return false;

        var labels = host.Split('.');
        foreach (var label in labels)
        {
            if (label.Length is 0 or > 63) return false;
            if (label.StartsWith('-') || label.EndsWith('-')) return false;
            foreach (var c in label)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '-') return false;
            }
        }

        // A bare single label is only meaningful for localhost or an intranet name; anything else
        // that reached here without a dot is almost certainly a mistake.
        return labels.Length > 1
               || host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }
}
