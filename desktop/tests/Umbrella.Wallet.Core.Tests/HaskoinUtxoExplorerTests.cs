using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Pins the Haskoin /unspent parser to a REAL captured response shape (Bitcoin Cash). A confirmed output
/// carries a "block" object with a height; a mempool one has "block": null and must be surfaced as
/// unconfirmed so the spender never selects it. Getting the confirmed flag wrong could let an unconfirmed
/// (reorg-able) coin fund a spend — so it is pinned here, offline.
/// </summary>
public sealed class HaskoinUtxoExplorerTests
{
    // First entry is confirmed (has a block); second is in the mempool (block: null).
    private const string UnspentJson = """
    [
      {
        "address": "bitcoincash:qqm04dymqgx3j4kav9xmfvh6ew3jfnagdyn58nq7hl",
        "block": { "height": 967317, "position": 12 },
        "txid": "0b533635f273cc19851364bbf377dab745730412c80c6bad587041207293ef7a",
        "index": 0,
        "pkscript": "76a91436fab49b020d1956dd614db4b2facba324cfa86988ac",
        "value": 1644550
      },
      {
        "address": "bitcoincash:qqm04dymqgx3j4kav9xmfvh6ew3jfnagdyn58nq7hl",
        "block": null,
        "txid": "e9b7cfc78fec3cc28560580e03b4eaf8b4bc32ab0d617d0c9dd005cb8ab51735",
        "index": 1,
        "pkscript": "76a91436fab49b020d1956dd614db4b2facba324cfa86988ac",
        "value": 5000000
      }
    ]
    """;

    [Fact]
    public void Parses_confirmed_and_mempool_utxos_with_the_right_flag()
    {
        var utxos = HaskoinUtxoExplorer.ParseUnspent(UnspentJson);

        Assert.Equal(2, utxos.Count);

        var confirmed = utxos[0];
        Assert.Equal("0b533635f273cc19851364bbf377dab745730412c80c6bad587041207293ef7a", confirmed.TxId);
        Assert.Equal(0, confirmed.Vout);
        Assert.Equal(1_644_550, confirmed.ValueSat);
        Assert.True(confirmed.Confirmed);

        var mempool = utxos[1];
        Assert.Equal(1, mempool.Vout);
        Assert.Equal(5_000_000, mempool.ValueSat);
        Assert.False(mempool.Confirmed); // block: null → not spendable yet
    }

    [Fact]
    public void Empty_or_non_array_yields_no_utxos()
    {
        Assert.Empty(HaskoinUtxoExplorer.ParseUnspent("[]"));
        Assert.Empty(HaskoinUtxoExplorer.ParseUnspent("{}"));
    }
}
