using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using Umbrella.Wallet.Infrastructure.Network;
using Xunit.Abstractions;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Live, read-only: what a transfer really costs in gas on each EVM network this wallet sends on.
///
/// The point is zkSync Era, which was receive-only because the wallet signed every transfer with a
/// flat 21,000 gas. Asking the chain shows why that could never work there — and that it is right
/// everywhere else. Nothing is signed or sent.
/// </summary>
[Trait("Category", "Live")]
public sealed class EvmGasLiveTests(ITestOutputHelper output)
{
    private static void Online()
    {
        Umbrella.Wallet.Core.Safety.ChainEndpoints.ClearAll();
        PublicHttp.SetProxy(null);
        PublicHttp.SetRequireProxy(false);
    }

    private static async Task<BigInteger?> EstimateAsync(string rpc, string from, string to)
    {
        using var res = await PublicHttp.Shared.PostAsJsonAsync(rpc, new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "eth_estimateGas",
            @params = new object[] { new { from, to, value = "0x1" } },
        });
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("result", out var result) || result.GetString() is not { } hex) return null;
        return BigInteger.Parse("0" + hex[2..], System.Globalization.NumberStyles.HexNumber);
    }

    [Fact]
    public async Task A_plain_transfer_costs_21000_on_ethereum_and_more_on_zksync()
    {
        Online();
        // Two ordinary, funded accounts; an estimate reads state and changes nothing.
        const string from = "0x9696f59E4d72E237BE84fFD425DCaD154Bf96976";   // a long-lived exchange address
        const string to = "0x742d35Cc6634C0532925a3b844Bc454e4438f44e";

        var ethereum = await EstimateAsync(EthTransactionSender.Chains["ETH"].Rpcs[0], from, to);
        var zksync = await EstimateAsync(EthTransactionSender.Chains["ZKSYNC"].Rpcs[0], from, to);
        output.WriteLine($"ethereum {ethereum}, zksync {zksync}");

        Assert.Equal(new BigInteger(21_000), ethereum);
        Assert.NotNull(zksync);
        Assert.True(zksync > 21_000, $"zkSync's own estimate is {zksync}; signing 21,000 there could never execute");
    }
}
