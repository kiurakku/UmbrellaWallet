using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Safety;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Public servers go down: while this was being built, Polkadot Asset Hub's default RPC stopped
/// answering minutes after it had answered, and the wallet — correctly — showed "could not read".
/// Correct, but avoidable: the other listed servers were up. These pin the rule for falling back, and
/// the rule that it never overrides a server the user chose.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class EndpointFailoverTests
{
    [Fact]
    public void An_untouched_setting_tries_the_default_then_every_listed_server()
    {
        ChainEndpoints.SetOverride("DOT", null);
        var candidates = ChainEndpoints.Candidates("DOT", "https://polkadot-asset-hub-rpc.polkadot.io");

        Assert.Equal("https://polkadot-asset-hub-rpc.polkadot.io", candidates[0]);
        Assert.Contains("https://statemint.api.onfinality.io/public", candidates);
        Assert.Equal(candidates.Count, candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void A_chosen_server_is_the_only_one_asked()
    {
        // Falling back from the user's own node to a company's server would send their addresses
        // somewhere they decided not to send them.
        ChainEndpoints.SetOverride("XRP", "https://my-rippled.example.org");
        try
        {
            Assert.Equal(["https://my-rippled.example.org"], ChainEndpoints.Candidates("XRP", "https://xrplcluster.com"));
        }
        finally
        {
            ChainEndpoints.SetOverride("XRP", null);
        }
    }

    [Fact]
    public void Every_chain_has_a_price_source()
    {
        // Without one the fiat column reads $0.00 — true only while the balance is also zero. Three
        // chains shipped like that for one commit; this keeps the next one from doing the same.
        var missing = ChainCatalog.All
            .Select(c => c.Symbol)
            .Where(s => !PublicMarketRatesClient.HasPriceSource(s))
            .ToList();

        Assert.Empty(missing);
    }
}
