using System.Text.Json;
using System.Text.RegularExpressions;
using Umbrella.Wallet.Core.Chains;
using Xunit;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The two facts about Zcash the wallet cannot check offline, asked of the network itself.
///
/// Marked <c>Category=Live</c> so CI skips it: the suite must never fail because somebody else's
/// server is down. Run it by hand when Zcash announces an upgrade — which is exactly when both of
/// these stop being true, and when a wallet that assumed otherwise starts signing transactions no
/// node will accept.
/// </summary>
[Trait("Category", "Live")]
public class ZcashConsensusLiveTests
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    [Fact]
    public async Task Branch_ids_still_match_Zcashs_own_source()
    {
        var upgrades = await Http.GetStringAsync(
            "https://raw.githubusercontent.com/zcash/zcash/master/src/consensus/upgrades.cpp");
        var chainparams = await Http.GetStringAsync(
            "https://raw.githubusercontent.com/zcash/zcash/master/src/chainparams.cpp");

        // .nBranchId = 0x…, .strName = "…" — in upgrade order.
        var branches = Regex.Matches(upgrades, @"\.nBranchId = (0x[0-9a-fA-F]+|0),\s*\.strName = ""([^""]+)""")
            .Select(m => (Name: m.Groups[2].Value, Id: Convert.ToUInt32(m.Groups[1].Value, m.Groups[1].Value.StartsWith("0x") ? 16 : 10)))
            .Where(b => b.Name is not ("Test dummy" or "ZFUTURE"))
            .ToList();

        // chainparams.cpp lists mainnet first, then testnet, then regtest — so the FIRST height each
        // upgrade is given is the mainnet one.
        var mainnet = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(chainparams, @"UPGRADE_(\w+)\]\.nActivationHeight = (\d+);"))
            mainnet.TryAdd(m.Groups[1].Value, uint.Parse(m.Groups[2].Value));

        Assert.NotEmpty(mainnet);
        var checkedAny = false;

        foreach (var (name, id) in branches)
        {
            if (!mainnet.TryGetValue(name.Replace(".", "_"), out var height)) continue;   // Sprout, or not scheduled
            Assert.Equal(id, ZcashTransactions.ConsensusBranchId(height));
            Assert.NotEqual(id, ZcashTransactions.ConsensusBranchId(height - 1));
            checkedAny = true;
        }

        Assert.True(checkedAny, "No upgrade could be matched to an activation height — the source layout changed.");
    }

    [Fact]
    public async Task Version_4_transactions_are_still_being_mined()
    {
        // The wallet builds v4. Mainnet now also produces v5 and v6, and a consensus rule that stopped
        // accepting v4 would make every Zcash send fail — so this asks the chain rather than the spec.
        using var blocks = await Json("https://api.blockchair.com/zcash/blocks?limit=3");
        var versions = new List<int>();

        foreach (var block in blocks.RootElement.GetProperty("data").EnumerateArray())
        {
            await Task.Delay(1500);
            var id = block.GetProperty("id").GetInt64();
            using var dashboard = await Json($"https://api.blockchair.com/zcash/dashboards/block/{id}");
            var hashes = dashboard.RootElement.GetProperty("data").GetProperty(id.ToString())
                .GetProperty("transactions").EnumerateArray().Take(8).Select(h => h.GetString()!).ToArray();
            if (hashes.Length == 0) continue;

            await Task.Delay(1500);
            using var txs = await Json($"https://api.blockchair.com/zcash/dashboards/transactions/{string.Join(",", hashes)}");
            foreach (var entry in txs.RootElement.GetProperty("data").EnumerateObject())
            {
                if (entry.Value.TryGetProperty("transaction", out var tx) && tx.TryGetProperty("version", out var v))
                    versions.Add(v.GetInt32());
            }
        }

        Assert.NotEmpty(versions);
        Assert.Contains(4, versions);
    }

    [Fact]
    public async Task A_node_decodes_the_transaction_and_stops_only_at_the_missing_coin()
    {
        // The signature hash is pinned to Zcash's vectors; the WIRE FORMAT is not, because those vectors
        // carry shielded parts this wallet never builds. So the network is asked: a transaction built
        // exactly as a send would be, signed by a key made up for this test, spending a coin that does
        // not exist. A node that cannot parse it answers "decode failed"; one that parses it and runs
        // the context-free checks (version group, expiry, amounts) gets as far as looking the coin up,
        // and stops there. Nothing can be spent: the coin is invented and the key holds nothing.
        using var key = new NBitcoin.Key();
        var from = ZcashAddress.EncodePublicKeyHash(key.PubKey.Hash.ToBytes());
        var fromScript = ZcashAddress.TryDecode(from).Address!.ScriptPubKey;

        using var stats = await Json("https://api.blockchair.com/zcash/stats");
        var next = (uint)stats.RootElement.GetProperty("data").GetProperty("best_block_height").GetInt64() + 1;
        var expiry = ZcashTransactions.ExpiryHeight(next) ?? throw new InvalidOperationException("Upgrade imminent.");

        var invented = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var quote = new Umbrella.Wallet.Infrastructure.Network.ZecSendQuote(
            From: from, To: "t3cFfPt1Bcvgez9ZbMBFWeZsskxTkPzGCow", Amount: 0.001m,
            AmountZat: 100_000, FeeZat: 10_000, FeeZec: 0.0001m, ChangeZat: 90_000,
            ExpiryHeight: expiry, BranchId: ZcashTransactions.ConsensusBranchId(next),
            Inputs: [new ZcashUtxo(invented, 0, 200_000, next - 10)],
            FromScript: fromScript,
            ToScript: ZcashAddress.TryDecode("t3cFfPt1Bcvgez9ZbMBFWeZsskxTkPzGCow").Address!.ScriptPubKey,
            ChangeSweptToFee: false);
        var (raw, _) = Umbrella.Wallet.Infrastructure.Network.ZcashTransactionSender.Build(quote, key);

        using var content = new FormUrlEncodedContent([new("data", Convert.ToHexString(raw).ToLowerInvariant())]);
        using var res = await Http.PostAsync("https://api.blockchair.com/zcash/push/transaction", content);
        var body = await res.Content.ReadAsStringAsync();

        Assert.False(res.IsSuccessStatusCode, body);
        Assert.DoesNotContain("decode", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missing", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<JsonDocument> Json(string url) =>
        JsonDocument.Parse(await Http.GetStringAsync(url));
}
