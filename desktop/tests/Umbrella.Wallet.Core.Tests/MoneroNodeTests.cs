using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Choosing which machine answers "what is on the chain" for a Monero wallet.
///
/// The node never learns the keys, the balance, the addresses or the amounts. It does learn the IP
/// that connected, that the IP belongs to a Monero wallet, roughly which blocks it asked for, when it
/// is online, and which connection a transaction entered the network through. That is enough to be
/// worth choosing deliberately, and the wallet had been choosing for the user in silence.
///
/// The rules that matter here are the two refusals. An .onion node without Tor cannot work at all. A
/// clearnet node while the kill-switch is armed and Tor is down would work — by handing a stranger the
/// user's real IP, which is exactly what the kill-switch exists to prevent.
/// </summary>
public sealed class MoneroNodeTests
{
    private static MoneroNode Parse(string text)
    {
        Assert.True(MoneroNode.TryParse(text, out var node), text);
        return node;
    }

    // --- parsing: a typo here points the wallet at somebody the user did not choose ---------------

    [Theory]
    [InlineData("node.example.com:18089", "node.example.com", 18089)]
    [InlineData("node.example.com", "node.example.com", 18081)]           // default port
    [InlineData("http://node.example.com:18081", "node.example.com", 18081)]
    [InlineData("https://node.example.com:18089/", "node.example.com", 18089)]
    [InlineData("  NODE.Example.COM:18081  ", "node.example.com", 18081)] // trimmed, lower-cased
    [InlineData("127.0.0.1:18081", "127.0.0.1", 18081)]                   // your own node
    [InlineData("localhost:18081", "localhost", 18081)]
    public void A_node_address_parses_the_way_operators_publish_it(string text, string host, int port)
    {
        var node = Parse(text);
        Assert.Equal(host, node.Host);
        Assert.Equal(port, node.Port);
        Assert.Equal($"{host}:{port}", node.Address);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("node.example.com:notaport")]
    [InlineData("node.example.com:0")]
    [InlineData("node.example.com:70000")]
    [InlineData("node.example.com/rpc")]        // a path is not a node address
    [InlineData("user:pass@node.example.com")]  // credentials do not belong here
    [InlineData("two words:18081")]
    [InlineData("-leading.dash.com")]
    [InlineData(".leading.dot.com")]
    [InlineData("trailing.dot.")]
    [InlineData("nodots")]                      // a bare label that is not localhost
    [InlineData("::1")]
    public void Anything_that_is_not_a_node_address_is_refused_rather_than_guessed(string text)
    {
        Assert.False(MoneroNode.TryParse(text, out _), text);
    }

    // --- the two refusals --------------------------------------------------------------------------

    [Fact]
    public void An_onion_node_is_unusable_without_tor()
    {
        var onion = Parse("abcdefghijklmnop.onion:18081");
        Assert.True(onion.IsOnion);

        Assert.Equal(MoneroNodeBlock.OnionNeedsTor, onion.BlockedBecause(torConnected: false, killSwitchArmed: false));
        Assert.False(onion.CanUse(torConnected: false, killSwitchArmed: false));

        Assert.True(onion.CanUse(torConnected: true, killSwitchArmed: false));
        Assert.True(onion.CanUse(torConnected: true, killSwitchArmed: true));
    }

    [Fact]
    public void A_clearnet_node_is_blocked_when_the_kill_switch_is_armed_and_tor_is_down()
    {
        // Not a convenience check. Connecting anyway would hand the node the real IP — the single
        // outcome the Tor-only kill-switch exists to prevent, on the one coin people choose for
        // privacy.
        var node = Parse("node.example.com:18081");
        Assert.False(node.IsOnion);

        Assert.Equal(
            MoneroNodeBlock.ClearnetBlockedByKillSwitch,
            node.BlockedBecause(torConnected: false, killSwitchArmed: true));

        Assert.True(node.CanUse(torConnected: true, killSwitchArmed: true));
        Assert.True(node.CanUse(torConnected: false, killSwitchArmed: false));
    }

    // --- the catalog -------------------------------------------------------------------------------

    [Fact]
    public void Every_shipped_node_is_a_well_formed_address_with_an_operator_named()
    {
        // "Somebody else's machine" is only an informed choice if the user can see whose.
        foreach (var node in MoneroNodeCatalog.Public)
        {
            Assert.True(MoneroNode.TryParse(node.Address, out var reparsed), node.Address);
            Assert.Equal(node.Address, reparsed.Address);
            Assert.False(string.IsNullOrWhiteSpace(node.Operator), node.Address);
            Assert.False(string.IsNullOrWhiteSpace(node.Label), node.Address);
        }
    }

    [Fact]
    public void No_shipped_node_is_listed_twice()
    {
        var addresses = MoneroNodeCatalog.Public.Select(n => n.Address).ToList();
        Assert.Equal(addresses.Distinct(StringComparer.OrdinalIgnoreCase).Count(), addresses.Count);
    }

    [Fact]
    public void A_saved_choice_wins_over_the_default()
    {
        var resolved = MoneroNodeCatalog.Resolve(
            "node.sethforprivacy.com:18089", torConnected: false, killSwitchArmed: false);

        Assert.NotNull(resolved);
        Assert.Equal("node.sethforprivacy.com:18089", resolved!.Address);
    }

    [Fact]
    public void A_saved_choice_that_cannot_be_used_is_not_quietly_replaced()
    {
        // The whole point of the feature. Substituting a different operator behind the user's back is
        // the behaviour it exists to end, so this reports "no node" and lets the UI say why.
        var resolved = MoneroNodeCatalog.Resolve(
            "abcdefghijklmnop.onion:18081", torConnected: false, killSwitchArmed: false);

        Assert.Null(resolved);
    }

    [Fact]
    public void An_unparseable_saved_choice_falls_back_to_the_default()
    {
        // A corrupted settings file should not leave Monero with no node at all.
        var resolved = MoneroNodeCatalog.Resolve("nonsense/////", torConnected: false, killSwitchArmed: false);

        Assert.NotNull(resolved);
        Assert.Equal(MoneroNodeCatalog.Default.Address, resolved!.Address);
    }

    [Fact]
    public void Nothing_is_resolved_when_the_kill_switch_blocks_every_clearnet_node()
    {
        Assert.Null(MoneroNodeCatalog.Resolve(null, torConnected: false, killSwitchArmed: true));
    }

    [Fact]
    public void A_custom_node_keeps_its_own_identity_rather_than_borrowing_a_shipped_one()
    {
        var resolved = MoneroNodeCatalog.Resolve("192.168.1.50:18081", torConnected: false, killSwitchArmed: false);

        Assert.NotNull(resolved);
        Assert.Equal("192.168.1.50:18081", resolved!.Address);
        Assert.False(MoneroNodeCatalog.IsKnown(resolved.Address));
    }

    [Fact]
    public void A_shipped_node_keeps_its_operator_name_when_it_is_the_saved_choice()
    {
        // Saved settings hold only an address; the label and operator have to be recovered from the
        // catalog or the Settings screen would show a bare hostname with nobody's name on it.
        var resolved = MoneroNodeCatalog.Resolve(
            "NODES.HASHVAULT.PRO:18081", torConnected: false, killSwitchArmed: false);

        Assert.Equal("HashVault", resolved!.Operator);
    }

    // --- fallback ----------------------------------------------------------------------------------

    [Fact]
    public void The_chosen_node_is_always_tried_first()
    {
        var chosen = Parse("node.monerodevs.org:18089");
        var order = MoneroNodeCatalog.FallbackOrder(chosen, torConnected: false, killSwitchArmed: false);

        Assert.Equal(chosen.Address, order[0].Address);
        Assert.Equal(order.Select(n => n.Address).Distinct(StringComparer.OrdinalIgnoreCase).Count(), order.Count);
    }

    [Fact]
    public void The_fallbacks_never_include_a_node_the_user_could_not_have_used()
    {
        // With the kill-switch armed and Tor down, no clearnet node is a legitimate substitute — so
        // "the chosen one failed" must not become "we connected you somewhere else anyway".
        var chosen = Parse("abcdefghijklmnop.onion:18081");
        var order = MoneroNodeCatalog.FallbackOrder(chosen, torConnected: false, killSwitchArmed: true);

        Assert.Single(order);
        Assert.Equal(chosen.Address, order[0].Address);
    }

    [Fact]
    public void A_custom_node_still_gets_the_public_ones_as_fallbacks()
    {
        var chosen = Parse("my-own-node.example.com:18081");
        var order = MoneroNodeCatalog.FallbackOrder(chosen, torConnected: true, killSwitchArmed: false);

        Assert.Equal(chosen.Address, order[0].Address);
        Assert.Equal(MoneroNodeCatalog.Public.Count + 1, order.Count);
    }
}
