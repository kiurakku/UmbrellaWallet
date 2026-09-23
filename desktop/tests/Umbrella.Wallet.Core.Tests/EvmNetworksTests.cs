using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The EVM networks the wallet offers and the ones it can actually finish a send on must be the same
/// set. They were not: Linea was in the Send picker and in the sender's registry, but the confirm step
/// matched a hand-written list that had never been updated, so a Linea send could be prepared and then
/// refused with "prepare first". The list is gone — the registry decides — and this keeps the two in step.
/// </summary>
public sealed class EvmNetworksTests
{
    private static IReadOnlySet<string> PickerSymbols =>
        Umbrella.Wallet.App.ViewModels.MainViewModel.SendableSymbols;

    [Fact]
    public void Every_network_the_sender_knows_is_offered_in_the_picker()
    {
        var missing = EthTransactionSender.Chains.Keys
            .Where(k => !PickerSymbols.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Every_evm_symbol_in_the_picker_is_a_network_the_sender_knows()
    {
        // The picker also lists non-EVM coins; what matters is that nothing EVM-shaped is offered
        // without the sender having a chain id, RPCs and an explorer for it.
        var evmish = new[] { "ETH", "BNB", "MATIC", "AVAX", "FTM", "CRO", "ARB", "BASE", "OP", "LINEA", "ZKSYNC" };
        var offeredButUnknown = PickerSymbols
            .Where(s => evmish.Contains(s, StringComparer.OrdinalIgnoreCase) && !EthTransactionSender.Chains.ContainsKey(s))
            .ToList();
        Assert.Empty(offeredButUnknown);
    }

    [Fact]
    public void Each_network_has_its_own_chain_id_and_explorer()
    {
        var chains = EthTransactionSender.Chains.Values.ToList();
        Assert.Equal(chains.Select(c => c.ChainId).Distinct().Count(), chains.Count);
        Assert.All(chains, c =>
        {
            Assert.True(c.ChainId > 0);
            Assert.NotEmpty(c.Rpcs);
            Assert.EndsWith("/", c.ExplorerTx);
        });

        // The L2s carry ETH as their coin but must not be keyed as "ETH", or mainnet would be shadowed.
        Assert.Equal(1, EthTransactionSender.Chains["ETH"].ChainId);
        Assert.Equal(324, EthTransactionSender.Chains["ZKSYNC"].ChainId);
    }
}
