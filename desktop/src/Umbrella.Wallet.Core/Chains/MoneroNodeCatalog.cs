namespace Umbrella.Wallet.Core.Chains;

/// <summary>
/// The remote Monero nodes this build offers, and the rules for choosing one.
///
/// Every one of these is somebody else's machine. That is not a flaw in Monero — a wallet has to ask
/// SOMETHING what is on the chain, and running your own node is the only way to make that something
/// yourself. The wallet's job is to make the choice visible rather than to make it quietly.
///
/// None of these is endorsed. They are public nodes run by known projects in the Monero ecosystem,
/// listed so the user has somewhere to start and can move away from whichever one this build happened
/// to ship with. The custom field is the point of the feature: your own node, or one you were given.
/// </summary>
public static class MoneroNodeCatalog
{
    /// <summary>
    /// Public nodes, in the order they are offered. Each was reachable on the port given when it was
    /// added; none of that is a promise about tomorrow, which is why the wallet falls through the list
    /// and says which one it actually reached.
    /// </summary>
    public static IReadOnlyList<MoneroNode> Public { get; } =
    [
        new("xmr-node.cakewallet.com", 18081, "Cake Wallet", "Cake Wallet"),
        new("xmr.stormycloud.org", 18089, "StormyCloud", "StormyCloud"),
        new("node.sethforprivacy.com", 18089, "Seth For Privacy", "Seth For Privacy"),
        new("nodes.hashvault.pro", 18081, "HashVault", "HashVault"),
        new("node.monerodevs.org", 18089, "MoneroDevs", "MoneroDevs"),
    ];

    /// <summary>The node used when the user has never chosen one.</summary>
    public static MoneroNode Default => Public[0];

    /// <summary>
    /// The node to actually start with, given what the user picked and what the network allows.
    ///
    /// A saved choice wins whenever it can be used. When it cannot — an .onion node with Tor off, say —
    /// this returns null rather than quietly substituting a different operator: silently sending the
    /// wallet to somebody else's machine is precisely the behaviour this feature exists to end. The
    /// caller shows the reason and lets the user decide.
    /// </summary>
    public static MoneroNode? Resolve(string? savedChoice, bool torConnected, bool killSwitchArmed)
    {
        if (MoneroNode.TryParse(savedChoice, out var saved))
        {
            var known = Public.FirstOrDefault(n => n.Address.Equals(saved.Address, StringComparison.OrdinalIgnoreCase));
            var node = known ?? saved;
            return node.CanUse(torConnected, killSwitchArmed) ? node : null;
        }

        return Default.CanUse(torConnected, killSwitchArmed) ? Default : null;
    }

    /// <summary>
    /// The order to try when the chosen node does not answer. Starts with the chosen one, then the
    /// other public nodes that are usable right now — an unreachable node should not leave Monero
    /// stuck with no balance, but a node the user could not have used is never substituted in.
    /// </summary>
    public static IReadOnlyList<MoneroNode> FallbackOrder(
        MoneroNode chosen, bool torConnected, bool killSwitchArmed)
    {
        var order = new List<MoneroNode> { chosen };
        order.AddRange(Public
            .Where(n => !n.Address.Equals(chosen.Address, StringComparison.OrdinalIgnoreCase))
            .Where(n => n.CanUse(torConnected, killSwitchArmed)));

        return order;
    }

    /// <summary>True when the address names one of the nodes shipped with this build.</summary>
    public static bool IsKnown(string? address) =>
        MoneroNode.TryParse(address, out var node)
        && Public.Any(n => n.Address.Equals(node.Address, StringComparison.OrdinalIgnoreCase));
}
