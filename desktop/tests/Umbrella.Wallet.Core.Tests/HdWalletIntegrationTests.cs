using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Utxo;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The full HD-wallet cycle the roadmap requires (§3.5): receive on two different addresses, see the
/// aggregated balance, spend across BOTH of them in one transaction, send change to a fresh internal
/// address, and then rediscover that change on a restored wallet with no local state. Entirely
/// offline — a fake explorer plus NBitcoin's own signature verification, no network, no real funds.
/// </summary>
public sealed class HdWalletIntegrationTests
{
    private const string Phrase =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    // A valid mainnet P2WPKH address that does NOT belong to this wallet (BIP173 example vector).
    private const string ExternalDestination = "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4";

    [Fact]
    public async Task Receive_two_addresses_spend_across_them_change_to_internal_then_restore()
    {
        var deriver = new HdAddressDeriver();
        const ChainId chain = ChainId.Btc;
        string Addr(uint c, uint i) => deriver.DeriveBitcoinLikeAt(Phrase, chain, c, i).Address;

        // 1. Receive coins on external #0 and external #1.
        var received = new FakeExplorer();
        received.Fund(Addr(0, 0), 60_000);
        received.Fund(Addr(0, 1), 60_000);

        var scanner = new UtxoAccountScanner(deriver, gapLimit: 5);
        var scan = await scanner.ScanAsync(Phrase, chain, received, UtxoScanFloors.None);

        // 2. See the aggregated sum across both addresses.
        Assert.False(scan.Partial);
        Assert.Equal(120_000, scan.TotalSat);
        Assert.Equal(2, scan.Utxos.Count);

        // 3. Spend an amount that cannot be covered by a single input — it must draw on #0 AND #1.
        var spender = new HdUtxoSpender(deriver);
        var request = new UtxoSpendRequest(chain, ExternalDestination, AmountSat: 100_000, FeeRateSatPerVByte: 1);
        var (plan, planError) = spender.PlanSpend(chain, scan.Utxos, request);

        Assert.Null(planError);
        Assert.NotNull(plan);
        Assert.Equal(2, plan!.Inputs.Count);
        Assert.Contains(plan.Inputs, u => u.Path.Change == 0 && u.Path.Index == 0);
        Assert.Contains(plan.Inputs, u => u.Path.Change == 0 && u.Path.Index == 1);
        Assert.True(plan.NeedsChange);

        // 4. Durably reserve the next internal index BEFORE signing, then derive the change address.
        var storePath = TempPath();
        try
        {
            var store = new AddressIndexStore(storePath);
            var changeIndex = store.ReserveNextChangeIndex("wallet-1", "BTC");
            Assert.Equal(0u, changeIndex); // first change output takes internal #0
            var changeAddress = deriver.DeriveBitcoinLikeAt(Phrase, chain, change: 1, index: changeIndex).Address;

            var (tx, buildError) = spender.BuildSigned(Phrase, plan, request, changeAddress);
            Assert.Null(buildError);
            Assert.NotNull(tx);

            // Both inputs are present and were signed with their own keys (BuildSigned ran Verify).
            Assert.Equal(2, tx!.Inputs.Count);

            // The recipient gets exactly the requested amount.
            var toScript = BitcoinAddress.Create(ExternalDestination, Network.Main).ScriptPubKey;
            var toOut = Assert.Single(tx.Outputs, o => o.ScriptPubKey == toScript);
            Assert.Equal(100_000, toOut.Value.Satoshi);

            // Change returns to the fresh INTERNAL address (change = 1), never a reused public one.
            var changeScript = deriver.DeriveBitcoinLikeAt(Phrase, chain, 1, 0).ScriptPubKey;
            var changeOut = Assert.Single(tx.Outputs, o => o.ScriptPubKey == changeScript);
            Assert.Equal(plan.ChangeSat, changeOut.Value.Satoshi);

            // Fee is exactly what the plan promised: inputs − amount − change.
            Assert.Equal(plan.FeeSat, plan.InputSat - request.AmountSat - plan.ChangeSat);

            // 5. After broadcast the change UTXO sits on internal #0. A RESTORED wallet with no local
            //    addr-indexes.json rediscovers it purely by gap-scanning from index 0.
            var afterSpend = new FakeExplorer();
            afterSpend.Fund(Addr(1, 0), plan.ChangeSat);

            var restored = await scanner.ScanAsync(Phrase, chain, afterSpend, UtxoScanFloors.None);
            Assert.False(restored.Partial);
            Assert.Equal(plan.ChangeSat, restored.TotalSat);
            Assert.Equal(0u, restored.HighestUsedInternalIndex);
            Assert.Single(restored.Utxos, u => u.Path.IsChange);
        }
        finally
        {
            try { if (File.Exists(storePath)) File.Delete(storePath); } catch { /* best effort */ }
        }
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"umbrella-hdint-{Guid.NewGuid():N}.json");

    private sealed class FakeExplorer : IUtxoExplorer
    {
        private readonly Dictionary<string, List<ExplorerUtxo>> _utxos = new();
        private int _txCounter;

        public void Fund(string address, long sat, bool confirmed = true)
        {
            if (!_utxos.TryGetValue(address, out var list))
            {
                list = new List<ExplorerUtxo>();
                _utxos[address] = list;
            }

            list.Add(new ExplorerUtxo((++_txCounter).ToString("x64"), 0, sat, confirmed));
        }

        public Task<AddressActivity> GetActivityAsync(string address, CancellationToken ct)
        {
            var used = _utxos.ContainsKey(address);
            return Task.FromResult(new AddressActivity(used, used ? 1 : 0));
        }

        public Task<IReadOnlyList<ExplorerUtxo>> GetUtxosAsync(string address, CancellationToken ct)
        {
            IReadOnlyList<ExplorerUtxo> r = _utxos.TryGetValue(address, out var l) ? l : Array.Empty<ExplorerUtxo>();
            return Task.FromResult(r);
        }
    }
}
