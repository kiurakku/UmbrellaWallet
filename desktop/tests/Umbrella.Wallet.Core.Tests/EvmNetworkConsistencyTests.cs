using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Every EVM network lives in three lists that have to agree: the balance fetch that reads it, the
/// send registry that can broadcast on it, and the Send picker the user chooses from.
///
/// Reading a balance is a GET against a public RPC; spending needs a signer whose fee model matches
/// the chain. So the two are genuinely different capabilities, and the dangerous drift is a balance
/// the wallet shows as "Ready" on a network it cannot actually broadcast to — the holdings list would
/// promise a send the Send screen never offers, which is how somebody ends up believing funds are
/// stuck.
///
/// zkSync Era is exactly that case and is the reason these tests exist: its balance is a normal
/// eth_getBalance, but a plain transfer there does not cost the flat 21,000 gas this wallet signs
/// with, so sending is deliberately off.
/// </summary>
public sealed class EvmNetworkConsistencyTests
{
    [Fact]
    public void A_network_marked_sendable_really_has_a_send_registry_entry()
    {
        var missing = PublicChainBalanceClient.EvmSideNetworks
            .Where(n => n.CanSend)
            .Where(n => !EthTransactionSender.Chains.Values.Any(c => c.Name.Contains(n.Network, StringComparison.OrdinalIgnoreCase)))
            .Select(n => n.Network)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void A_network_marked_read_only_has_no_way_to_broadcast()
    {
        // The other direction, and the one that actually loses money: if zkSync were quietly added to
        // the send registry, the wallet would sign a transfer with a gas limit the chain rejects.
        var leaked = PublicChainBalanceClient.EvmSideNetworks
            .Where(n => !n.CanSend)
            .Where(n => EthTransactionSender.Chains.Values.Any(c => c.Name.Contains(n.Network, StringComparison.OrdinalIgnoreCase)))
            .Select(n => n.Network)
            .ToList();

        Assert.Empty(leaked);
    }

    [Fact]
    public void Every_network_whose_balance_is_read_can_also_be_spent()
    {
        // zkSync Era was the exception while the wallet signed a flat 21,000 gas everywhere; now the
        // gas limit is the chain's own estimate, so a balance that is read is a balance that can move.
        // Pinned by name, so adding a network later does not silently rewrite the claim.
        var networks = PublicChainBalanceClient.EvmSideNetworks.ToDictionary(n => n.Network, n => n.CanSend);

        Assert.True(networks.ContainsKey("zkSync Era"));
        Assert.True(networks["zkSync Era"]);
        Assert.True(networks.ContainsKey("Linea"));
        Assert.True(networks["Linea"]);
        Assert.DoesNotContain(networks, n => !n.Value);
    }

    [Fact]
    public void Every_evm_send_network_is_offered_in_the_picker()
    {
        // A network the wallet can broadcast on but never lists is a feature nobody finds. The
        // capability set is keyed by the registry key (ARB/BASE/OP/LINEA), not by the coin symbol,
        // because the L2s all report "ETH"; MainViewModelTests pins the picker itself to this set.
        var missing = EthTransactionSender.Chains.Keys
            .Where(k => !MainViewModel.SendableSymbols.Contains(k))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_chain_id_is_distinct()
    {
        // Two networks sharing a chain id would let a transaction signed for one be replayed on the
        // other — the exact thing EIP-155 exists to prevent.
        var ids = EthTransactionSender.Chains.Values.Select(c => c.ChainId).ToList();
        Assert.Equal(ids.Distinct().Count(), ids.Count);
    }

    [Fact]
    public void Every_network_has_at_least_one_fallback_rpc()
    {
        // A single RPC is a single point of failure for a balance, and worse for a broadcast: one
        // rate-limited host and the send simply does not go out.
        var thin = EthTransactionSender.Chains
            .Where(c => c.Value.Rpcs.Count < 2)
            .Select(c => c.Key)
            .ToList();

        Assert.Empty(thin);
    }
}
