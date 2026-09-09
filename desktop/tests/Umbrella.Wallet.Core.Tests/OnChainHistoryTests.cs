using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

public sealed class OnChainHistoryTests
{
    [Fact]
    public void ParseTokenMarketData_readsCoinGeckoMarketsRow()
    {
        var json = """
        [{"id":"ethereum","symbol":"eth","market_cap":226000000000,
          "fully_diluted_valuation":230000000000,"total_volume":15000000000}]
        """;
        var md = PublicMarketRatesClient.ParseTokenMarketData(json);
        Assert.NotNull(md);
        Assert.Equal(226000000000m, md!.MarketCap);
        Assert.Equal(230000000000m, md.Fdv);
        Assert.Equal(15000000000m, md.Volume24h);

        Assert.Null(PublicMarketRatesClient.ParseTokenMarketData("[]"));
    }

    [Fact]
    public void ScaleDown_dividesByDecimals_andTrimsZeros()
    {
        Assert.Equal("1.5", OnChainHistoryClient.ScaleDown("1500000", 6));   // 1.5 USDT
        Assert.Equal("12", OnChainHistoryClient.ScaleDown("12000000", 6));   // 12 USDT, no trailing .0
        Assert.Equal("0.0001", OnChainHistoryClient.ScaleDown("10000", 8));  // 0.0001 BTC
        Assert.Equal("0", OnChainHistoryClient.ScaleDown("0", 6));
    }

    [Fact]
    public void ParseTronTrc20_marksDirectionAndAmount()
    {
        const string me = "TNvxWShQmqxskvFvh2TGYjskVwVWEisPCA";
        var json = """
        {"data":[
          {"transaction_id":"aaa","from":"TSenderAddrXXXXXXXXXXXXXXXXXXXXXX","to":"TNvxWShQmqxskvFvh2TGYjskVwVWEisPCA",
           "value":"2500000","block_timestamp":1723680000000,
           "token_info":{"symbol":"USDT","decimals":6}},
          {"transaction_id":"bbb","from":"TNvxWShQmqxskvFvh2TGYjskVwVWEisPCA","to":"TRecipientAddrYYYYYYYYYYYYYYYYYY",
           "value":"1000000","block_timestamp":1723690000000,
           "token_info":{"symbol":"USDT","decimals":6}}
        ]}
        """;
        var rows = OnChainHistoryClient.ParseTronTrc20(json, me);
        Assert.Equal(2, rows.Count);

        var received = rows[0];
        Assert.Equal("Received", received.Kind);
        Assert.Equal("USDT", received.Asset);
        Assert.Equal("2.5", received.Amount);
        Assert.Contains("aaa", received.Explorer);

        var sent = rows[1];
        Assert.Equal("Sent", sent.Kind);
        Assert.Equal("1", sent.Amount);
        Assert.Equal("TRecipientAddrYYYYYYYYYYYYYYYYYY", sent.Counterparty);
    }

    [Fact]
    public void TronBase58ToHex_roundTrips()
    {
        const string addr = "TNvxWShQmqxskvFvh2TGYjskVwVWEisPCA";
        var hex = OnChainHistoryClient.TronBase58ToHex(addr);
        Assert.StartsWith("41", hex);          // TRON mainnet prefix
        Assert.Equal(42, hex.Length);          // 21 bytes → 42 hex chars
    }

    [Fact]
    public void ParseTronNative_classifiesTrxTransfers()
    {
        const string addr = "TNvxWShQmqxskvFvh2TGYjskVwVWEisPCA";
        var meHex = OnChainHistoryClient.TronBase58ToHex(addr);
        const string otherHex = "410000000000000000000000000000000000000001"; // any distinct 41-hex
        var json = """
        {"data":[
          {"txID":"n1","block_timestamp":1723600000000,"raw_data":{"contract":[
            {"type":"TransferContract","parameter":{"value":{"owner_address":"OTHER","to_address":"ME","amount":5000000}}}]}},
          {"txID":"n2","block_timestamp":1723700000000,"raw_data":{"contract":[
            {"type":"TransferContract","parameter":{"value":{"owner_address":"ME","to_address":"OTHER","amount":1200000}}}]}}
        ]}
        """.Replace("OTHER", otherHex).Replace("ME", meHex);
        var rows = OnChainHistoryClient.ParseTronNative(json, meHex);
        Assert.Equal(2, rows.Count);
        Assert.Equal("Received", rows[0].Kind);
        Assert.Equal("TRX", rows[0].Asset);
        Assert.Equal("5", rows[0].Amount);
        Assert.Equal("Sent", rows[1].Kind);
        Assert.Equal("1.2", rows[1].Amount);
    }

    [Fact]
    public void ParseEvmTxlist_classifiesEthTransfers()
    {
        const string me = "0xabc0000000000000000000000000000000000001";
        var json = """
        {"status":"1","message":"OK","result":[
          {"hash":"0xin","from":"0xsender0000000000000000000000000000000009","to":"0xabc0000000000000000000000000000000000001",
           "value":"1500000000000000000","timeStamp":"1723600000","isError":"0"},
          {"hash":"0xout","from":"0xabc0000000000000000000000000000000000001","to":"0xmerchant000000000000000000000000000000",
           "value":"250000000000000000","timeStamp":"1723700000","isError":"0"},
          {"hash":"0xfail","from":"0xabc0000000000000000000000000000000000001","to":"0xz","value":"0","timeStamp":"1723710000","isError":"1"}
        ]}
        """;
        var rows = OnChainHistoryClient.ParseEvmTxlist(json, me, "ETH", "https://etherscan.io/tx/", 18);
        Assert.Equal(2, rows.Count); // the failed / zero-value one is skipped
        Assert.Equal("Received", rows[0].Kind);
        Assert.Equal("1.5", rows[0].Amount);
        Assert.Equal("Sent", rows[1].Kind);
        Assert.Equal("0.25", rows[1].Amount);
    }

    [Fact]
    public void ParseBitcoin_classifiesReceiveAndSpend()
    {
        const string me = "bc1qexampleaddr";
        var json = """
        [
          {"txid":"rx","status":{"block_time":1723600000},
           "vin":[{"prevout":{"scriptpubkey_address":"bc1qsomeoneelse","value":500000}}],
           "vout":[{"scriptpubkey_address":"bc1qexampleaddr","value":300000},
                   {"scriptpubkey_address":"bc1qsomeoneelse","value":200000}]},
          {"txid":"sx","status":{"block_time":1723700000},
           "vin":[{"prevout":{"scriptpubkey_address":"bc1qexampleaddr","value":1000000}}],
           "vout":[{"scriptpubkey_address":"bc1qmerchant","value":700000},
                   {"scriptpubkey_address":"bc1qexampleaddr","value":250000}]}
        ]
        """;
        var rows = OnChainHistoryClient.ParseBitcoin(json, me);
        Assert.Equal(2, rows.Count);

        Assert.Equal("Received", rows[0].Kind);
        Assert.Equal("BTC", rows[0].Asset);
        Assert.Equal("0.003", rows[0].Amount);           // 300000 sats to us

        Assert.Equal("Sent", rows[1].Kind);
        Assert.Equal("bc1qmerchant", rows[1].Counterparty);
        Assert.Equal("0.007", rows[1].Amount);           // 700000 sats to others
    }

    [Fact]
    public void ParseHaskoinFull_classifiesBchReceiveAndSpend()
    {
        const string me = "bitcoincash:qqmeexampleaddr";
        // Haskoin transactions/full: inputs[]/outputs[] each carry address + value (satoshis, 1e8), plus
        // block + time (unix seconds). Same net-effect logic as Bitcoin. A coinbase input has no address,
        // which must not crash the parse or count toward "mine".
        var json = """
        [
          {"txid":"rx","time":1723600000,"block":{"height":800001},
           "inputs":[{"coinbase":false,"address":"bitcoincash:qqsomeoneelse","value":500000},
                     {"coinbase":true,"value":0}],
           "outputs":[{"address":"bitcoincash:qqmeexampleaddr","value":300000},
                      {"address":"bitcoincash:qqsomeoneelse","value":200000}]},
          {"txid":"sx","time":1723700000,"block":{"height":800002},
           "inputs":[{"address":"bitcoincash:qqmeexampleaddr","value":1000000}],
           "outputs":[{"address":"bitcoincash:qqmerchant","value":700000},
                      {"address":"bitcoincash:qqmeexampleaddr","value":250000}]}
        ]
        """;
        var rows = OnChainHistoryClient.ParseHaskoinFull(
            json, me, "BCH", "https://blockchair.com/bitcoin-cash/transaction/");
        Assert.Equal(2, rows.Count);

        Assert.Equal("Received", rows[0].Kind);
        Assert.Equal("BCH", rows[0].Asset);
        Assert.Equal("0.003", rows[0].Amount);            // 300000 sats to us
        Assert.Equal(1723600000000, rows[0].UnixMs);      // seconds → ms
        Assert.StartsWith("https://blockchair.com/bitcoin-cash/transaction/rx", rows[0].Explorer);

        Assert.Equal("Sent", rows[1].Kind);
        Assert.Equal("bitcoincash:qqmerchant", rows[1].Counterparty);
        Assert.Equal("0.007", rows[1].Amount);            // 700000 sats to others (change + fee excluded)
    }

    [Fact]
    public void ParseHaskoinFull_matchesRegardlessOfCashAddrScheme()
    {
        // The wallet derives "me" WITH the "bitcoincash:" scheme and Haskoin returns addresses WITH it
        // too, but the comparison must survive either side dropping the scheme — so a bare "me" still
        // matches a scheme-carrying output.
        const string bareMe = "qqmeexampleaddr";
        var json = """
        [{"txid":"rx","time":1723600000,"block":{"height":800001},
          "inputs":[{"address":"bitcoincash:qqsomeoneelse","value":500000}],
          "outputs":[{"address":"bitcoincash:qqmeexampleaddr","value":300000}]}]
        """;
        var rows = OnChainHistoryClient.ParseHaskoinFull(
            json, bareMe, "BCH", "https://blockchair.com/bitcoin-cash/transaction/");
        Assert.Single(rows);
        Assert.Equal("Received", rows[0].Kind);
        Assert.Equal("0.003", rows[0].Amount);
    }

    [Fact]
    public void ParseTon_classifiesReceiveAndSend()
    {
        const string me = "EQMe0000000000000000000000000000000000000000";
        // TON: an incoming transfer carries value on in_msg (with a real source); an outgoing one has an
        // empty in_msg source and the transfers live in out_msgs. Amounts are nanoTON (1e9).
        var json = """
        {"ok":true,"result":[
          {"utime":1723600000,"transaction_id":{"hash":"rx1"},
           "in_msg":{"source":"EQSenderAddr","destination":"EQMe","value":"2500000000"},"out_msgs":[]},
          {"utime":1723700000,"transaction_id":{"hash":"sx1"},
           "in_msg":{"source":"","destination":"EQMe","value":"0"},
           "out_msgs":[{"source":"EQMe","destination":"EQRecipient","value":"1000000000"}]}
        ]}
        """;
        var rows = OnChainHistoryClient.ParseTon(json, me);
        Assert.Equal(2, rows.Count);

        Assert.Equal("Received", rows[0].Kind);
        Assert.Equal("TON", rows[0].Asset);
        Assert.Equal("2.5", rows[0].Amount);             // 2.5e9 nanoTON
        Assert.Equal("EQSenderAddr", rows[0].Counterparty);

        Assert.Equal("Sent", rows[1].Kind);
        Assert.Equal("1", rows[1].Amount);               // 1e9 nanoTON
        Assert.Equal("EQRecipient", rows[1].Counterparty);
    }

    [Fact]
    public void ParseCardanoTxInfo_classifiesReceiveAndSpend()
    {
        const string me = "addr1me";
        // Same net-effect logic as Bitcoin: own inputs vs own outputs (lovelace, 1e6).
        var json = """
        [
          {"tx_hash":"rx1","tx_timestamp":1723600000,
           "inputs":[{"payment_addr":{"bech32":"addr1other"},"value":"5000000"}],
           "outputs":[{"payment_addr":{"bech32":"addr1me"},"value":"3000000"},
                      {"payment_addr":{"bech32":"addr1other"},"value":"1800000"}]},
          {"tx_hash":"sx1","tx_timestamp":1723700000,
           "inputs":[{"payment_addr":{"bech32":"addr1me"},"value":"10000000"}],
           "outputs":[{"payment_addr":{"bech32":"addr1merchant"},"value":"7000000"},
                      {"payment_addr":{"bech32":"addr1me"},"value":"2800000"}]}
        ]
        """;
        var rows = OnChainHistoryClient.ParseCardanoTxInfo(json, me);
        Assert.Equal(2, rows.Count);

        Assert.Equal("Received", rows[0].Kind);
        Assert.Equal("ADA", rows[0].Asset);
        Assert.Equal("3", rows[0].Amount);               // 3e6 lovelace to us

        Assert.Equal("Sent", rows[1].Kind);
        Assert.Equal("7", rows[1].Amount);               // 7e6 lovelace to others
        Assert.Equal("addr1merchant", rows[1].Counterparty);
    }

    [Fact]
    public void ParseSolanaTransactions_classifiesByBalanceDelta()
    {
        const string me = "MEsol";
        // Batched getTransaction responses: classify by the net change to our own lamport balance
        // (1e9). For a send we're the fee payer (index 0), so the fee is backed out of the amount.
        var json = """
        [
          {"jsonrpc":"2.0","id":0,"result":{"blockTime":1723600000,
            "meta":{"err":null,"fee":5000,"preBalances":[9000000000,1000000000],"postBalances":[9000000000,1500000000]},
            "transaction":{"message":{"accountKeys":["OTHER","MEsol"]},"signatures":["sigRX"]}}},
          {"jsonrpc":"2.0","id":1,"result":{"blockTime":1723700000,
            "meta":{"err":null,"fee":5000,"preBalances":[3000000000,7000000000],"postBalances":[1999995000,8000000000]},
            "transaction":{"message":{"accountKeys":["MEsol","OTHER"]},"signatures":["sigTX"]}}}
        ]
        """;
        var rows = OnChainHistoryClient.ParseSolanaTransactions(json, me);
        Assert.Equal(2, rows.Count);

        Assert.Equal("Received", rows[0].Kind);
        Assert.Equal("SOL", rows[0].Asset);
        Assert.Equal("0.5", rows[0].Amount);             // +0.5 SOL to us

        Assert.Equal("Sent", rows[1].Kind);
        Assert.Equal("1", rows[1].Amount);               // 1 SOL out, fee backed out
    }
}
