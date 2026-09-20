using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Roadmap P1.6 — the connection state, in the primary interface.
///
/// The case this exists for is the one that looks like the good one: Tor switched on in Settings,
/// Tor not actually running, and every request going out in the clear. Somebody who turned Tor on
/// once believes it is on until told otherwise, and the Security Center is precisely where they are
/// not looking while they check a balance.
/// </summary>
public sealed class ConnectionStatusTests
{
    private const string TorProxy = "socks5://127.0.0.1:9250";

    private static SendTransportState State(
        bool torRequested = false, bool torConnected = false, bool killSwitch = false,
        bool customProxy = false, string? requested = null, string? active = null) =>
        new(torRequested, torConnected, killSwitch, TorProxy, customProxy, requested, active);

    [Fact]
    public void Traffic_through_tor_reads_as_tor()
    {
        var state = ConnectionStatus.Evaluate(
            State(torRequested: true, torConnected: true, active: TorProxy));

        Assert.Equal(ConnectionRoute.Tor, state.Route);
        Assert.False(state.TorExpectedButNotUsed);
    }

    /// <summary>The whole reason the chip exists.</summary>
    [Fact]
    public void Tor_requested_but_not_carrying_the_traffic_is_flagged()
    {
        var state = ConnectionStatus.Evaluate(State(torRequested: true, torConnected: false, active: null));

        Assert.Equal(ConnectionRoute.Direct, state.Route);
        Assert.True(state.TorExpectedButNotUsed);
    }

    [Fact]
    public void A_deliberate_direct_connection_is_not_flagged()
    {
        var state = ConnectionStatus.Evaluate(State());

        Assert.Equal(ConnectionRoute.Direct, state.Route);
        Assert.False(state.TorExpectedButNotUsed);
    }

    [Fact]
    public void A_user_proxy_reads_as_a_proxy()
    {
        var state = ConnectionStatus.Evaluate(State(
            customProxy: true, requested: "socks5://127.0.0.1:9050", active: "socks5://127.0.0.1:9050"));

        Assert.Equal(ConnectionRoute.CustomProxy, state.Route);
    }

    /// <summary>
    /// A proxy is in use, but the user asked for Tor: the traffic IS going somewhere, and not where
    /// they think. Worth the warning colour even though nothing is leaking to clearnet.
    /// </summary>
    [Fact]
    public void A_proxy_while_tor_was_asked_for_is_still_flagged()
    {
        var state = ConnectionStatus.Evaluate(State(
            torRequested: true, customProxy: true,
            requested: "socks5://127.0.0.1:9050", active: "socks5://127.0.0.1:9050"));

        Assert.Equal(ConnectionRoute.CustomProxy, state.Route);
        Assert.True(state.TorExpectedButNotUsed);
    }

    [Fact]
    public void An_armed_kill_switch_with_no_route_reads_as_blocked()
    {
        var state = ConnectionStatus.Evaluate(State(torRequested: true, killSwitch: true, active: null));

        Assert.Equal(ConnectionRoute.Blocked, state.Route);
    }

    /// <summary>
    /// The chip and the send gate read the same state, so they can never tell different stories —
    /// a chip saying "TOR" over a send refused for not being on Tor would be worse than either.
    /// </summary>
    [Theory]
    [InlineData(true, false, false)]   // Tor asked for, not connected
    [InlineData(true, true, true)]     // Tor connected and armed
    [InlineData(false, false, true)]   // kill-switch armed, no Tor
    public void The_chip_agrees_with_the_send_gate(bool torRequested, bool torConnected, bool killSwitch)
    {
        var s = State(torRequested, torConnected, killSwitch,
            active: torConnected ? TorProxy : null);

        var chip = ConnectionStatus.Evaluate(s);
        var gate = SendTransportGate.Evaluate(s);

        // Blocked or "not what you asked for" on one side must not read as fine on the other.
        var chipHappy = chip.Route is ConnectionRoute.Tor or ConnectionRoute.CustomProxy
                        || (chip.Route == ConnectionRoute.Direct && !chip.TorExpectedButNotUsed);

        Assert.Equal(gate.Allowed, chipHappy);
    }
}
