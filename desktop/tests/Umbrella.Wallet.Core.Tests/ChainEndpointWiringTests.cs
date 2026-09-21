using Umbrella.Wallet.Core.Safety;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The setting has to actually move the traffic.
///
/// A picker that writes a preference nobody reads is worse than no picker: the user believes they
/// have moved their addresses off a company's servers, and every request still goes there. The
/// registry is deliberately consulted inside the adapters' own factories, because those factories are
/// called from a dozen places — so these tests check the factories, not the registry.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class ChainEndpointWiringTests : IDisposable
{
    public ChainEndpointWiringTests() => ChainEndpoints.ClearAll();
    /// <summary>Restores the offline state the whole run is set up with. Leaving the registry merely
    /// CLEARED would hand real network access back to every app-state test scheduled after this one —
    /// silently, and only sometimes, depending on ordering.</summary>
    public void Dispose() => TestDataIsolation.GoOffline();

    [Fact]
    public void With_no_choice_the_esplora_explorer_uses_the_shipped_default()
    {
        Assert.Equal("https://blockstream.info/api", EsploraUtxoExplorer.BaseUrlFor("BTC"));
        Assert.Equal("https://litecoinspace.org/api", EsploraUtxoExplorer.BaseUrlFor("LTC"));
    }

    [Fact]
    public void A_chosen_server_is_what_the_esplora_explorer_actually_calls()
    {
        ChainEndpoints.SetOverride("BTC", "https://mempool.space/api");

        Assert.Equal("https://mempool.space/api", EsploraUtxoExplorer.BaseUrlFor("BTC"));

        // And only that chain moved.
        Assert.Equal("https://litecoinspace.org/api", EsploraUtxoExplorer.BaseUrlFor("LTC"));
    }

    [Fact]
    public void The_default_is_still_reachable_after_a_choice_is_cleared()
    {
        ChainEndpoints.SetOverride("BTC", "https://mempool.space/api");
        ChainEndpoints.SetOverride("BTC", null);

        Assert.Equal(
            EsploraUtxoExplorer.DefaultBaseUrlFor("BTC"),
            EsploraUtxoExplorer.BaseUrlFor("BTC"));
    }

    [Fact]
    public void The_shipped_defaults_are_the_first_option_the_picker_offers()
    {
        // The picker treats its first entry as "the default" and clears the override when it is
        // chosen. If that entry were not really the default, choosing it would silently move the user
        // somewhere else — the exact opposite of what the button says.
        Assert.Equal(
            EsploraUtxoExplorer.DefaultBaseUrlFor("BTC"),
            ChainEndpoints.Known["BTC"][0].BaseUrl);
        Assert.Equal(
            EsploraUtxoExplorer.DefaultBaseUrlFor("LTC"),
            ChainEndpoints.Known["LTC"][0].BaseUrl);
        Assert.Equal(HaskoinUtxoExplorer.DefaultRoot, ChainEndpoints.Known["BCH"][0].BaseUrl);
        Assert.Equal(BlockCypherUtxoExplorer.DefaultRoot, ChainEndpoints.Known["DOGE"][0].BaseUrl);
    }

    [Fact]
    public void Every_configurable_chain_is_one_the_wallet_really_reads()
    {
        // Offering to redirect a chain the wallet does not fetch would be a control that does nothing.
        var known = new[] { "BTC", "LTC", "BCH", "DOGE", "ETH", "SOL", "TON", "TRX", "ADA", "XRP" };

        foreach (var symbol in ChainEndpoints.Configurable)
        {
            Assert.Contains(symbol, known);
        }
    }

    [Fact]
    public void An_override_does_not_leak_between_chains_that_share_an_adapter()
    {
        // BCH and DOGE both go through slug-based adapters. Pointing one at a private server must not
        // drag the other along with it.
        ChainEndpoints.SetOverride("BCH", "https://my-haskoin.example.org");

        Assert.True(ChainEndpoints.IsCustomised("BCH"));
        Assert.False(ChainEndpoints.IsCustomised("DOGE"));
        Assert.Equal(
            BlockCypherUtxoExplorer.DefaultRoot,
            ChainEndpoints.Resolve("DOGE", BlockCypherUtxoExplorer.DefaultRoot));
    }
}
