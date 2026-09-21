using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Roadmap P0.7 — the route a send takes must be the route the user chose, or the send does not
/// happen.
///
/// The kill-switch already refuses clearnet at the socket, but only when it is armed. The case this
/// gate exists for is the common one: somebody turned Tor on, never armed the kill-switch, and has
/// no idea Tor failed to start or died an hour ago. Every balance lookup after that leaks, which is
/// bad; the broadcast leaks permanently, which is the one that cannot be walked back — it ties an IP
/// to specific coins on somebody else's server, forever.
/// </summary>
public sealed class SendTransportGateTests
{
    private const string TorProxy = "socks5://127.0.0.1:9250";

    private static SendTransportState State(
        bool torRequested = false,
        bool torConnected = false,
        bool killSwitch = false,
        bool customProxyRequested = false,
        string? requestedProxy = null,
        string? activeProxy = null) =>
        new(torRequested, torConnected, killSwitch, TorProxy, customProxyRequested, requestedProxy, activeProxy);

    [Fact]
    public void Tor_on_but_not_connected_blocks_the_send()
    {
        var check = SendTransportGate.Evaluate(State(torRequested: true, torConnected: false));

        Assert.False(check.Allowed);
        Assert.Equal(SendTransportReason.TorNotConnected, check.Reason);
    }

    /// <summary>
    /// The nastiest shape of the bug: Tor really is running, so the status line looks right — but the
    /// shared client was left pointing somewhere else, so the broadcast goes out in the clear anyway.
    /// </summary>
    [Fact]
    public void Tor_connected_but_traffic_routed_elsewhere_blocks_the_send()
    {
        var check = SendTransportGate.Evaluate(
            State(torRequested: true, torConnected: true, activeProxy: null));

        Assert.False(check.Allowed);
        Assert.Equal(SendTransportReason.TorNotInUse, check.Reason);
    }

    [Fact]
    public void Tor_connected_and_in_use_allows_the_send()
    {
        var check = SendTransportGate.Evaluate(
            State(torRequested: true, torConnected: true, activeProxy: TorProxy));

        Assert.True(check.Allowed);
        Assert.Equal(SendTransportReason.Match, check.Reason);
    }

    [Fact]
    public void An_armed_kill_switch_with_no_proxy_blocks_before_the_transport_has_to()
    {
        // The socket layer would refuse this too, but the refusal belongs on the Send screen in words
        // rather than as a failed broadcast the user has to interpret (MANIFESTO §4).
        var check = SendTransportGate.Evaluate(State(killSwitch: true, activeProxy: null));

        Assert.False(check.Allowed);
        Assert.Equal(SendTransportReason.KillSwitchWithoutProxy, check.Reason);
    }

    [Fact]
    public void A_typed_but_unapplied_proxy_blocks_the_send()
    {
        var check = SendTransportGate.Evaluate(State(
            customProxyRequested: true,
            requestedProxy: "socks5://127.0.0.1:9050",
            activeProxy: null));

        Assert.False(check.Allowed);
        Assert.Equal(SendTransportReason.CustomProxyNotInUse, check.Reason);
    }

    [Fact]
    public void A_proxy_that_is_not_the_one_chosen_blocks_the_send()
    {
        var check = SendTransportGate.Evaluate(State(
            customProxyRequested: true,
            requestedProxy: "socks5://127.0.0.1:9050",
            activeProxy: "socks5://10.0.0.1:1080"));

        Assert.False(check.Allowed);
        Assert.Equal(SendTransportReason.CustomProxyNotInUse, check.Reason);
    }

    [Fact]
    public void The_chosen_proxy_in_use_allows_the_send()
    {
        var check = SendTransportGate.Evaluate(State(
            customProxyRequested: true,
            requestedProxy: "socks5://127.0.0.1:9050",
            activeProxy: "socks5://127.0.0.1:9050"));

        Assert.True(check.Allowed);
    }

    /// <summary>
    /// Asking for nothing is a choice the wallet honours. Blocking a deliberate clearnet send would
    /// be the wallet overruling the user, and the Send review already states what it costs.
    /// </summary>
    [Fact]
    public void A_deliberate_clearnet_send_is_allowed_and_named_as_such()
    {
        var check = SendTransportGate.Evaluate(State());

        Assert.True(check.Allowed);
        Assert.Equal(SendTransportReason.Clearnet, check.Reason);
    }

    /// <summary>A route comparison must not be defeated by a trailing slash or capitalisation.</summary>
    [Theory]
    [InlineData("socks5://127.0.0.1:9250/")]
    [InlineData("SOCKS5://127.0.0.1:9250")]
    public void Route_matching_is_not_fooled_by_formatting(string active)
    {
        var check = SendTransportGate.Evaluate(
            State(torRequested: true, torConnected: true, activeProxy: active));

        Assert.True(check.Allowed);
    }
}
