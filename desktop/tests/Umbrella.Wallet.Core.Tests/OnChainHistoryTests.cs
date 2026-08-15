using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

public sealed class OnChainHistoryTests
{
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
}
