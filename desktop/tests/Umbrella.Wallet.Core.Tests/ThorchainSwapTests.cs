using System.Text;
using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Utxo;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Pins the THORChain swap quote parser to REAL captured responses from the public THORNode API, and
/// checks the fund-safety invariants of a swap: the memo must fit an 80-byte OP_RETURN and must survive
/// the round-trip into a Bitcoin OP_RETURN script byte-for-byte (a mangled or dropped memo would turn a
/// swap deposit into an unrecoverable plain transfer).
/// </summary>
public sealed class ThorchainSwapTests
{
    // Verbatim BTC -> ETH quote from https://thornode/.../thorchain/quote/swap (trimmed of prose fields).
    private const string BtcToEthJson = """
    {
      "inbound_address": "bc1q9nd5j7q3gynye66lmdlut9yyg9fcal68kaykl2",
      "inbound_confirmation_blocks": 1,
      "inbound_confirmation_seconds": 600,
      "outbound_delay_blocks": 14,
      "outbound_delay_seconds": 84,
      "fees": {
        "asset": "ETH.ETH", "affiliate": "0", "outbound": "13358",
        "liquidity": "999312", "total": "1012670", "slippage_bps": 29, "total_bps": 30
      },
      "expiry": 1785516746,
      "dust_threshold": "1000",
      "recommended_min_amount_in": "7575",
      "recommended_gas_rate": "4",
      "gas_rate_units": "satsperbyte",
      "memo": "=:e:0x111111111117dC0aa78b770fA6A738034120C302",
      "expected_amount_out": "335463741"
    }
    """;

    [Fact]
    public void Parses_a_real_btc_to_eth_quote()
    {
        var (q, err) = ThorchainSwapClient.ParseQuote(BtcToEthJson, "BTC", "ETH", 0.1m);

        Assert.Null(err);
        Assert.NotNull(q);
        Assert.Equal("bc1q9nd5j7q3gynye66lmdlut9yyg9fcal68kaykl2", q!.InboundAddress);
        Assert.Equal("=:e:0x111111111117dC0aa78b770fA6A738034120C302", q.Memo);
        Assert.Equal(3.35463741m, q.ExpectedOut);       // 335463741 / 1e8
        Assert.Equal(0.0101267m, q.TotalFee);           // 1012670 / 1e8
        Assert.Equal(29, q.SlippageBps);
        Assert.Equal(30, q.TotalBps);
        Assert.Equal(1785516746, q.ExpiryUnix);
        Assert.Equal(0.00007575m, q.RecommendedMinIn);  // 7575 / 1e8
        Assert.Equal(0.00001m, q.DustThreshold);        // 1000 / 1e8
        Assert.Equal(684, q.EtaSeconds);                // 600 + 84
        Assert.Null(q.Router);
    }

    [Fact]
    public void Below_minimum_is_flagged_but_still_a_quote()
    {
        // 0.1 BTC is far above the ~0.00007575 min; a dust amount is below it.
        var (above, _) = ThorchainSwapClient.ParseQuote(BtcToEthJson, "BTC", "ETH", 0.1m);
        Assert.False(above!.BelowMinimum);

        var (below, _) = ThorchainSwapClient.ParseQuote(BtcToEthJson, "BTC", "ETH", 0.00001m);
        Assert.True(below!.BelowMinimum);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"error\":\"swap quote error: fail to simulate swap: pool is halted\"}")]
    [InlineData("{\"expected_amount_out\":\"1\"}")] // no inbound/memo
    public void Bad_bodies_never_produce_a_quote(string body)
    {
        var (q, err) = ThorchainSwapClient.ParseQuote(body, "BTC", "ETH", 0.1m);
        Assert.Null(q);
        Assert.False(string.IsNullOrEmpty(err));
    }

    [Fact]
    public void Swap_memo_fits_an_op_return_and_round_trips_into_the_script()
    {
        // The exact memo THORChain returns for an LTC -> BTC swap.
        const string memo = "=:b:bc1q9nd5j7q3gynye66lmdlut9yyg9fcal68kaykl2";
        var bytes = Encoding.ASCII.GetBytes(memo);
        Assert.True(bytes.Length <= 80, "THORChain swap memo must fit the 80-byte OP_RETURN limit.");

        // Building the OP_RETURN the sender uses must embed the memo so it comes back out unchanged.
        var script = TxNullDataTemplate.Instance.GenerateScriptPubKey(bytes);
        var recovered = TxNullDataTemplate.Instance.ExtractScriptPubKeyParameters(script);
        Assert.NotNull(recovered);
        Assert.Single(recovered!);
        Assert.Equal(memo, Encoding.ASCII.GetString(recovered![0]));
    }

    [Fact]
    public void Spender_refuses_a_memo_that_would_overflow_the_op_return()
    {
        // The multisource spend planner is where the OP_RETURN guard now lives (pure, no network).
        const string phrase =
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
        var deriver = new HdAddressDeriver();
        var acct = deriver.DeriveBitcoinLikeAt(phrase, ChainId.Btc, 0, 0);
        var utxos = new[] { new OwnedUtxo(acct.Path, acct.Address, new string('0', 64), 0, 1_000_000, true) };
        var request = new UtxoSpendRequest(
            ChainId.Btc, "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4", 100_000, 1, Memo: new string('x', 81));

        var (plan, err) = new HdUtxoSpender(deriver).PlanSpend(ChainId.Btc, utxos, request);

        Assert.Null(plan);
        Assert.Contains("80", err);
    }

    [Fact]
    public void Coverage_lists_are_sane()
    {
        Assert.Contains("BTC", ThorchainSwapClient.SendableFrom);
        Assert.Contains("LTC", ThorchainSwapClient.SendableFrom);
        Assert.Contains("ETH", ThorchainSwapClient.ReceivableTo);
        Assert.True(ThorchainSwapClient.Supports("DOGE"));
        Assert.False(ThorchainSwapClient.Supports("XMR")); // THORChain has no Monero pool
    }
}
