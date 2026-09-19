namespace Umbrella.Wallet.Core.Safety;

/// <summary>
/// What the user asked the network layer to do, and what it is actually about to do.
///
/// <paramref name="ActiveProxy"/> is the proxy the shared HTTP client will really use — not a
/// setting, the live value. The two can disagree: Tor can be switched on in Settings and not be
/// connected, a custom proxy can be typed and never applied, a Tor circuit can drop after the
/// wallet was configured. Each of those leaves a user who believes they are anonymous broadcasting
/// a signed transaction from their own IP.
/// </summary>
public sealed record SendTransportState(
    bool TorRequested,
    bool TorConnected,
    bool KillSwitchOn,
    string? TorProxy,
    bool CustomProxyRequested,
    string? RequestedProxy,
    string? ActiveProxy);

/// <summary>Why a send was allowed or refused. Language-neutral: the UI maps these to its own text.</summary>
public enum SendTransportReason
{
    /// <summary>The route matches what was asked for.</summary>
    Match,

    /// <summary>Nothing was asked for: this send goes over the open internet, deliberately.</summary>
    Clearnet,

    /// <summary>Tor is on in Settings but is not connected — the request would fall back to clearnet.</summary>
    TorNotConnected,

    /// <summary>Tor is connected but the traffic is routed somewhere else entirely.</summary>
    TorNotInUse,

    /// <summary>The Tor-only kill-switch is armed and no proxy is active: nothing may leave.</summary>
    KillSwitchWithoutProxy,

    /// <summary>A custom proxy was chosen but is not the route in use.</summary>
    CustomProxyNotInUse,
}

/// <summary>The verdict plus the reason, so the caller can both block and explain.</summary>
public sealed record SendTransportCheck(bool Allowed, SendTransportReason Reason);

/// <summary>
/// Roadmap P0.7 — the send-path transport gate.
///
/// A signed transaction is the single most identifying thing this wallet ever hands to a stranger:
/// it ties an IP to specific coins, permanently, on somebody else's server. Everywhere else a
/// transport mismatch costs privacy; here it costs it irreversibly.
///
/// So before a send is prepared or broadcast, the route the user chose is compared against the route
/// that exists, and a mismatch FAILS rather than proceeding with a warning. The kill-switch already
/// refuses clearnet at the socket, but only when it is armed — this covers the far more common case
/// of somebody who turned Tor on, never armed the kill-switch, and has no idea Tor stopped.
///
/// Pure and offline: it compares state, it does not reach for any of it.
/// </summary>
public static class SendTransportGate
{
    public static SendTransportCheck Evaluate(SendTransportState state)
    {
        // Armed kill-switch with nothing to route through: the transport would refuse anyway, but the
        // refusal belongs on the Send screen, in words, rather than as a failed broadcast (§4).
        if (state.KillSwitchOn && string.IsNullOrEmpty(state.ActiveProxy))
            return new SendTransportCheck(false, SendTransportReason.KillSwitchWithoutProxy);

        if (state.TorRequested)
        {
            if (!state.TorConnected)
                return new SendTransportCheck(false, SendTransportReason.TorNotConnected);

            // Connected, but is the wallet's traffic actually going through it?
            if (!SameRoute(state.ActiveProxy, state.TorProxy))
                return new SendTransportCheck(false, SendTransportReason.TorNotInUse);

            return new SendTransportCheck(true, SendTransportReason.Match);
        }

        if (state.CustomProxyRequested)
        {
            // A typed-but-unapplied proxy is the same failure as a dropped Tor: the user believes
            // their addresses are going somewhere they are not.
            if (string.IsNullOrEmpty(state.RequestedProxy) ||
                !SameRoute(state.ActiveProxy, state.RequestedProxy))
            {
                return new SendTransportCheck(false, SendTransportReason.CustomProxyNotInUse);
            }

            return new SendTransportCheck(true, SendTransportReason.Match);
        }

        // Nothing was asked for. Going direct is then the user's own choice, and the Send review
        // already says what it costs — blocking it would be the wallet overruling them.
        return string.IsNullOrEmpty(state.ActiveProxy)
            ? new SendTransportCheck(true, SendTransportReason.Clearnet)
            : new SendTransportCheck(true, SendTransportReason.Match);
    }

    private static bool SameRoute(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
        string.Equals(a!.TrimEnd('/'), b!.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
