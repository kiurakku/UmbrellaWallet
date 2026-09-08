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

    // Verbatim BTC -> BCH quote from the live THORNode quote API (0.05 BTC, real BCH CashAddr body as the
    // destination). Pins that the wallet can quote a swap whose OUTPUT is BCH — the new receive-only swap
    // target — including THORChain's BCH memo form "=:c:<cashaddr-body>".
    private const string BtcToBchJson = """
    {
      "inbound_address": "bc1qeay5x2ap5cycqgje4s9y973eh3faaz9yw3zwph",
      "inbound_confirmation_seconds": 600,
      "outbound_delay_seconds": 48,
      "fees": {
        "asset": "BCH.BCH", "affiliate": "0", "outbound": "95670",
        "liquidity": "3310134", "total": "3405804", "slippage_bps": 21, "total_bps": 22
      },
      "expiry": 1788689914,
      "dust_threshold": "1000",
      "recommended_min_amount_in": "6069",
      "memo": "=:c:qqyx49mu0kkn9ftfj6hje6g2wfer34yfnq5tahq3q6:0/0/3",
      "expected_amount_out": "1528734784"
    }
    """;

    [Fact]
    public void Parses_a_real_btc_to_bch_quote()
    {
        var (q, err) = ThorchainSwapClient.ParseQuote(BtcToBchJson, "BTC", "BCH", 0.05m);

        Assert.Null(err);
        Assert.NotNull(q);
        Assert.Equal("bc1qeay5x2ap5cycqgje4s9y973eh3faaz9yw3zwph", q!.InboundAddress);
        Assert.Equal("=:c:qqyx49mu0kkn9ftfj6hje6g2wfer34yfnq5tahq3q6:0/0/3", q.Memo);
        Assert.Equal(15.28734784m, q.ExpectedOut);      // 1528734784 / 1e8
        Assert.Equal(0.03405804m, q.TotalFee);          // 3405804 / 1e8
        Assert.Equal(22, q.TotalBps);
        Assert.Equal(648, q.EtaSeconds);                // 600 + 48
    }

    // Verbatim ETH -> AVAX quote from the live THORNode API (1 ETH, 0x destination). Pins that a native
    // EVM L1 (Avalanche) works as a swap TARGET, delivered to the shared 0x address, with a router set.
    private const string EthToAvaxJson = """
    {
      "inbound_address": "0x70ea3187a45e83d540c4f54158c3dcacfd03f677",
      "router": "0xD37BbE5744D730a1d98d8DC97c42F0Ca46aD7146",
      "inbound_confirmation_seconds": 24,
      "outbound_delay_seconds": 30,
      "fees": { "asset": "AVAX.AVAX", "total": "68528165", "slippage_bps": 20, "total_bps": 21 },
      "expiry": 1788692260,
      "recommended_min_amount_in": "1000000",
      "memo": "=:a:0x111111111117dC0aa78b770fA6A738034120C302:0/0/4",
      "expected_amount_out": "32500323962"
    }
    """;

    // Verbatim BTC -> USDC quote from the live THORNode API (0.05 BTC, 0x destination). Pins that an
    // ERC-20 stablecoin works as a swap TARGET: the to_asset carries the token contract, the memo uses
    // THORChain's short "=:ETH.USDC:" form, and the 1e8-normalised output converts to a human USDC amount.
    private const string BtcToUsdcJson = """
    {
      "inbound_address": "bc1qeay5x2ap5cycqgje4s9y973eh3faaz9yw3zwph",
      "inbound_confirmation_seconds": 600,
      "outbound_delay_seconds": 48,
      "fees": { "asset": "ETH.USDC", "total": "1213645000", "slippage_bps": 29, "total_bps": 30 },
      "expiry": 1788693067,
      "memo": "=:ETH.USDC:0x111111111117dC0aa78b770fA6A738034120C302",
      "expected_amount_out": "397797143300"
    }
    """;

    [Fact]
    public void Parses_a_real_btc_to_usdc_quote()
    {
        var (q, err) = ThorchainSwapClient.ParseQuote(BtcToUsdcJson, "BTC", "USDC", 0.05m);

        Assert.Null(err);
        Assert.NotNull(q);
        Assert.Equal("=:ETH.USDC:0x111111111117dC0aa78b770fA6A738034120C302", q!.Memo);
        Assert.Equal(3977.971433m, q.ExpectedOut);      // 397797143300 / 1e8
        Assert.Equal(12.13645m, q.TotalFee);            // 1213645000 / 1e8
        Assert.Equal(30, q.TotalBps);
        Assert.Equal(648, q.EtaSeconds);                // 600 + 48
    }

    [Fact]
    public void Parses_a_real_eth_to_avax_quote()
    {
        var (q, err) = ThorchainSwapClient.ParseQuote(EthToAvaxJson, "ETH", "AVAX", 1m);

        Assert.Null(err);
        Assert.NotNull(q);
        Assert.Equal("0x70ea3187a45e83d540c4f54158c3dcacfd03f677", q!.InboundAddress);
        Assert.Equal("0xD37BbE5744D730a1d98d8DC97c42F0Ca46aD7146", q.Router);
        Assert.Equal(325.00323962m, q.ExpectedOut);     // 32500323962 / 1e8
        Assert.Equal(0.68528165m, q.TotalFee);          // 68528165 / 1e8
        Assert.Equal(21, q.TotalBps);
        Assert.Equal(54, q.EtaSeconds);                 // 24 + 30
    }

    [Theory]
    // THORChain wants BCH as the CashAddr BODY, not the "bitcoincash:" URI form (verified live: the
    // prefixed form returns an empty quote). Every other asset — and an already-bare BCH addr — is untouched.
    [InlineData("BCH", "bitcoincash:qqyx49mu0kkn9ftfj6hje6g2wfer34yfnq5tahq3q6", "qqyx49mu0kkn9ftfj6hje6g2wfer34yfnq5tahq3q6")]
    [InlineData("BCH", "qqyx49mu0kkn9ftfj6hje6g2wfer34yfnq5tahq3q6", "qqyx49mu0kkn9ftfj6hje6g2wfer34yfnq5tahq3q6")]
    [InlineData("BTC", "bc1qeay5x2ap5cycqgje4s9y973eh3faaz9yw3zwph", "bc1qeay5x2ap5cycqgje4s9y973eh3faaz9yw3zwph")]
    [InlineData("ETH", "0x111111111117dC0aa78b770fA6A738034120C302", "0x111111111117dC0aa78b770fA6A738034120C302")]
    public void NormalizeDestination_strips_only_the_bch_uri_scheme(string sym, string input, string expected) =>
        Assert.Equal(expected, ThorchainSwapClient.NormalizeDestination(sym, input));

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
    public void Bch_swap_deposit_builds_signs_with_the_op_return_memo()
    {
        // A BCH-FROM swap: send BCH to THORChain's inbound vault (CashAddr) with the swap memo as an
        // OP_RETURN, signed with SIGHASH_FORKID. Proves the swap-from-BCH deposit is buildable offline —
        // the vault address (bare from the API) is normalised with the "bitcoincash:" scheme the parser needs.
        const string phrase =
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
        var deriver = new HdAddressDeriver();
        var spender = new HdUtxoSpender(deriver);
        var acct = deriver.DeriveBitcoinLikeAt(phrase, ChainId.Bch, 0, 0);
        var utxos = new[] { new OwnedUtxo(acct.Path, acct.Address, new string('0', 64), 0, 50_000_000, true) };

        const string vault = "bitcoincash:qqckttuv9y0a3mngndvu8syla93u3l3wmq7glxmhzc"; // live THORChain BCH vault
        const string memo = "=:b:bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4";
        Assert.True(System.Text.Encoding.ASCII.GetByteCount(memo) <= 80);

        var request = new UtxoSpendRequest(ChainId.Bch, vault, 10_000_000, 2, Memo: memo);
        var (plan, planErr) = spender.PlanSpend(ChainId.Bch, utxos, request);
        Assert.Null(planErr);
        Assert.NotNull(plan);

        var change = deriver.DeriveBitcoinLikeAt(phrase, ChainId.Bch, 1, 0).Address;
        var (tx, buildErr) = spender.BuildSigned(phrase, plan!, request, change);
        Assert.Null(buildErr);
        Assert.NotNull(tx);
        Assert.Equal(3, tx!.Outputs.Count); // vault deposit + OP_RETURN memo + change
    }

    [Fact]
    public void Coverage_lists_are_sane()
    {
        Assert.Contains("BTC", ThorchainSwapClient.SendableFrom);
        Assert.Contains("LTC", ThorchainSwapClient.SendableFrom);
        Assert.Contains("DOGE", ThorchainSwapClient.SendableFrom); // enabled once DOGE send landed
        Assert.Contains("ETH", ThorchainSwapClient.SendableFrom);  // ETH-from via the router-contract call
        Assert.Contains("ETH", ThorchainSwapClient.ReceivableTo);
        // Receive-only swap TARGETS — valid outputs, but never offered as sources (send path not wired).
        foreach (var target in new[] { "AVAX", "BNB", "USDC", "USDT" })
        {
            Assert.Contains(target, ThorchainSwapClient.ReceivableTo);
            Assert.DoesNotContain(target, ThorchainSwapClient.SendableFrom);
            Assert.True(ThorchainSwapClient.Supports(target));
        }
        // BCH swaps BOTH ways now that its SIGHASH_FORKID send path landed — a source AND a target.
        Assert.Contains("BCH", ThorchainSwapClient.SendableFrom);
        Assert.Contains("BCH", ThorchainSwapClient.ReceivableTo);
        Assert.True(ThorchainSwapClient.Supports("DOGE"));
        Assert.False(ThorchainSwapClient.Supports("XMR")); // THORChain has no Monero pool
    }

    [Fact]
    public void EncodeDepositWithExpiry_hasRouterSelector_andEncodesArgs()
    {
        // The fund-critical piece of an ETH-from swap: the exact router calldata. A wrong signature or
        // layout makes the call revert (ETH stays), but we pin it anyway so it's provably correct offline.
        const string vault = "0x1091c4De6a3cF09CdA00AbDAeD42c7c3B69C83EC";
        const string asset = "0x0000000000000000000000000000000000000000"; // native ETH
        var amount = System.Numerics.BigInteger.Parse("1000000000000000000"); // 1 ETH
        const string memo = "=:BTC.BTC:bc1qexampleaddress:0/1/0";
        var expiry = new System.Numerics.BigInteger(1723700000);

        var data = EthTransactionSender.EncodeDepositWithExpiry(vault, asset, amount, memo, expiry);

        // THORChain router depositWithExpiry(address,address,uint256,string,uint256) 4-byte selector.
        Assert.StartsWith("0x44bc937b", data);

        var body = data[10..]; // after "0x" + 4-byte selector
        string Word(int i) => body.Substring(i * 64, 64).ToLowerInvariant();
        System.Numerics.BigInteger Dec(string w) =>
            System.Numerics.BigInteger.Parse("0" + w, System.Globalization.NumberStyles.HexNumber);

        Assert.EndsWith("1091c4de6a3cf09cda00abdaed42c7c3b69c83ec", Word(0)); // head[0] vault, right-aligned
        Assert.Equal(System.Numerics.BigInteger.Zero, Dec(Word(1)));          // head[1] asset = 0x0
        Assert.Equal(amount, Dec(Word(2)));                                   // head[2] amount
        Assert.Equal(new System.Numerics.BigInteger(160), Dec(Word(3)));      // head[3] offset to memo
        Assert.Equal(expiry, Dec(Word(4)));                                   // head[4] expiry
        var memoLen = Encoding.UTF8.GetByteCount(memo);
        Assert.Equal(new System.Numerics.BigInteger(memoLen), Dec(Word(5)));  // tail: memo length
        Assert.Contains(Convert.ToHexString(Encoding.UTF8.GetBytes(memo)).ToLowerInvariant(), body.ToLowerInvariant());
    }
}
