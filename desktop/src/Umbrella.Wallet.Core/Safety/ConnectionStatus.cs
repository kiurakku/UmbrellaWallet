namespace Umbrella.Wallet.Core.Safety;

/// <summary>Where this wallet's requests are actually going, right now.</summary>
public enum ConnectionRoute
{
    /// <summary>Nothing may leave: the Tor-only kill-switch is armed and there is no route.</summary>
    Blocked,

    /// <summary>Through the bundled Tor.</summary>
    Tor,

    /// <summary>Through the proxy the user configured.</summary>
    CustomProxy,

    /// <summary>Straight out, over the open internet.</summary>
    Direct,
}

/// <summary>
/// The route, plus the one thing worth shouting about: the user asked for Tor and is not getting it.
/// </summary>
public sealed record ConnectionState(ConnectionRoute Route, bool TorExpectedButNotUsed);

/// <summary>
/// Roadmap P1.6 — the connection state, in the primary interface rather than three screens deep.
///
/// The wallet already knew this; it just said it in the Security Center, which is exactly where
/// somebody is NOT looking while they check a balance or paste an address. And the state that
/// matters most is the one that looks like the good one: Tor switched on in Settings, Tor not
/// actually running, every request going out in the clear. That is a different fact from "Tor is
/// off", and it deserves to be visible without asking for it.
///
/// Derived from the same live signals the send gate uses (<see cref="SendTransportState"/>), so the
/// chip and the refusal can never tell different stories.
/// </summary>
public static class ConnectionStatus
{
    public static ConnectionState Evaluate(SendTransportState s)
    {
        var proxy = s.ActiveProxy ?? string.Empty;

        if (s.KillSwitchOn && proxy.Length == 0)
            return new ConnectionState(ConnectionRoute.Blocked, s.TorRequested);

        if (proxy.Length > 0 && Same(proxy, s.TorProxy))
            return new ConnectionState(ConnectionRoute.Tor, false);

        if (proxy.Length > 0)
            return new ConnectionState(ConnectionRoute.CustomProxy, s.TorRequested);

        // Going direct. Whether that is fine or alarming depends entirely on what was asked for.
        return new ConnectionState(ConnectionRoute.Direct, s.TorRequested);
    }

    private static bool Same(string a, string? b) =>
        !string.IsNullOrEmpty(b) &&
        string.Equals(a.TrimEnd('/'), b!.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
