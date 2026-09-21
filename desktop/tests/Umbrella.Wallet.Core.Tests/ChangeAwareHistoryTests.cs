using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Psbt;
using Umbrella.Wallet.Core.Utxo;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// History on a UTXO chain, judged against the wallet's whole address set (roadmap P0.1, §3.2.4).
///
/// The defect this pins: judged one address at a time, a payment of 0.001 BTC from receive #0 with
/// 0.00899 change back to an internal address showed as 0.00999 SENT — the change counted as money
/// that left — and a later spend funded only by that change address never appeared at all.
/// </summary>
public sealed class ChangeAwareHistoryTests
{
    private const string Phrase =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private static readonly HdAddressDeriver Deriver = new();
    private static readonly string Receive0 = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, 0, 0).Address;
    private static readonly string Change0 = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, 1, 0).Address;
    private const string Payee = "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4";

    /// <summary>An Esplora /address/{a}/txs payload with one transaction.</summary>
    private static string Esplora(string txid, (string Addr, long Sat)[] ins, (string Addr, long Sat)[] outs)
    {
        var vin = string.Join(",", ins.Select(i => $$$"""{"prevout":{"scriptpubkey_address":"{{{i.Addr}}}","value":{{{i.Sat}}}}}"""));
        var vout = string.Join(",", outs.Select(o => $$$"""{"scriptpubkey_address":"{{{o.Addr}}}","value":{{{o.Sat}}}}"""));
        return $$$"""[{"txid":"{{{txid}}}","vin":[{{{vin}}}],"vout":[{{{vout}}}],"status":{"block_time":1700000000}}]""";
    }

    private static readonly string PaymentWithChange = Esplora("aa",
        [(Receive0, 1_000_000)],
        [(Payee, 100_000), (Change0, 899_000)]);   // 1 000 sat fee

    [Fact]
    public void Judged_one_address_at_a_time_the_change_was_counted_as_sent()
    {
        // The old behaviour, kept as the counter-example this fix is measured against.
        var row = Assert.Single(OnChainHistoryClient.ParseEsplora(PaymentWithChange, Receive0, "BTC", "x/"));
        Assert.Equal("0.00999", row.Amount);
    }

    [Fact]
    public void Judged_against_the_whole_wallet_sent_is_exactly_what_left_it()
    {
        var own = new HashSet<string>(StringComparer.Ordinal) { Receive0, Change0 };
        var row = Assert.Single(OnChainHistoryClient.ParseEsplora(PaymentWithChange, own, "BTC", "x/"));

        Assert.Equal("Sent", row.Kind);
        Assert.Equal("0.001", row.Amount);
        Assert.Equal(Payee, row.Counterparty);
    }

    [Fact]
    public void A_spend_funded_only_by_change_is_recognised_as_a_send()
    {
        var spendFromChange = Esplora("bb", [(Change0, 899_000)], [(Payee, 500_000), (Change0, 398_000)]);
        var own = new HashSet<string>(StringComparer.Ordinal) { Receive0, Change0 };

        var row = Assert.Single(OnChainHistoryClient.ParseEsplora(spendFromChange, own, "BTC", "x/"));
        Assert.Equal("Sent", row.Kind);
        Assert.Equal("0.005", row.Amount);
    }

    [Fact]
    public void Bitcoin_Cash_history_nets_out_change_the_same_way()
    {
        var bchReceive = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Bch, 0, 0).Address;
        var bchChange = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Bch, 1, 0).Address;
        var other = Deriver.DeriveBitcoinLikeAt(
            "legal winner thank year wave sausage worth useful legal winner thank yellow", ChainId.Bch, 0, 0).Address;

        // Haskoin writes addresses without the "bitcoincash:" scheme; the wallet's carry it.
        static string Bare(string a) => a.Replace("bitcoincash:", "");
        var json = $$"""
            [{"txid":"cc","time":1700000000,
              "inputs":[{"address":"{{Bare(bchReceive)}}","value":1000000}],
              "outputs":[{"address":"{{Bare(other)}}","value":100000},{"address":"{{Bare(bchChange)}}","value":899000}]}]
            """;

        var own = new HashSet<string>(StringComparer.Ordinal) { bchReceive, bchChange };
        var row = Assert.Single(OnChainHistoryClient.ParseHaskoinFull(json, own, "BCH", "x/"));
        Assert.Equal("0.001", row.Amount);
    }

    // --- which addresses are asked about --------------------------------------------------------

    [Fact]
    public void A_fresh_wallet_asks_about_receive_0_only_but_recognises_change_as_its_own()
    {
        var own = OwnScripts.For(Deriver, Phrase, ChainId.Btc, UtxoScanFloors.None);
        var plan = HistoryAddresses.Plan(own, UtxoScanFloors.None, Network.Main);

        Assert.Equal([Receive0], plan.Query);
        Assert.Contains(Change0, plan.Own);
        Assert.Contains(Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, 0, 0, kind: UtxoScriptKind.Taproot).Address, plan.Own);
    }

    [Fact]
    public void Used_change_and_Taproot_branches_are_asked_about_too()
    {
        var floors = new UtxoScanFloors(
            LastIssuedExternalIndex: 1, LastSeenUsedExternalIndex: null,
            LastIssuedInternalIndex: 2, LastSeenUsedInternalIndex: null,
            TaprootLastSeenUsedExternalIndex: 0);
        var own = OwnScripts.For(Deriver, Phrase, ChainId.Btc, floors);
        var plan = HistoryAddresses.Plan(own, floors, Network.Main);

        Assert.Equal(2 + 3 + 1, plan.Query.Count);   // receive 0..1, change 0..2, Taproot receive 0
        Assert.Contains(Change0, plan.Query);
        Assert.Contains(Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, 1, 2).Address, plan.Query);
        Assert.Contains(Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, 0, 0, kind: UtxoScriptKind.Taproot).Address, plan.Query);
    }

    [Fact]
    public void The_query_is_capped_per_branch()
    {
        var floors = new UtxoScanFloors(500, null, null, null);
        var own = OwnScripts.For(Deriver, Phrase, ChainId.Btc, floors);
        var plan = HistoryAddresses.Plan(own, floors, Network.Main);

        Assert.Equal((int)HistoryAddresses.MaxPerBranch + 1, plan.Query.Count);   // receive 0..25
    }
}
