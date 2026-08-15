using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Utxo;

namespace Umbrella.Wallet.Core.Tests;

public sealed class UtxoDiscoveryTests
{
    private const string Phrase =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private static readonly HdAddressDeriver Deriver = new();

    private static string Addr(uint change, uint index) =>
        Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, change, index).Address;

    /// <summary>Funds on external #0, external #2 and internal (change) #0; everything else empty.</summary>
    [Fact]
    public async Task Discovers_funded_external_and_change_addresses_and_aggregates_balance()
    {
        var fake = new FakeExplorer();
        fake.Fund(Addr(0, 0), 100_000);
        fake.Fund(Addr(0, 2), 50_000);
        fake.Fund(Addr(1, 0), 20_000);

        var scanner = new UtxoAccountScanner(Deriver, gapLimit: 3);
        var result = await scanner.ScanAsync(Phrase, ChainId.Btc, fake, UtxoScanFloors.None);

        Assert.False(result.Partial);
        Assert.Equal(170_000, result.ConfirmedSat);
        Assert.Equal(0, result.PendingSat);
        Assert.Equal(170_000, result.TotalSat);
        Assert.Equal(2u, result.HighestUsedExternalIndex);
        Assert.Equal(0u, result.HighestUsedInternalIndex);

        // The change UTXO is tagged as internal so the spender can find its key.
        var change = Assert.Single(result.Utxos, u => u.Path.IsChange);
        Assert.Equal(20_000, change.ValueSat);
        Assert.Equal(3, result.Utxos.Count);
    }

    /// <summary>
    /// A fresh install with no addr-indexes.json (a seed restore) must still find funded addresses
    /// purely by gap-scanning from index 0 — the balance cannot depend on local state.
    /// </summary>
    [Fact]
    public async Task Restore_without_local_state_still_finds_funds_by_gap_scan()
    {
        var fake = new FakeExplorer();
        fake.Fund(Addr(0, 0), 100_000);
        fake.Fund(Addr(0, 1), 25_000);
        fake.Fund(Addr(1, 0), 5_000);

        var scanner = new UtxoAccountScanner(Deriver, gapLimit: 5);
        var result = await scanner.ScanAsync(Phrase, ChainId.Btc, fake, UtxoScanFloors.None);

        Assert.False(result.Partial);
        Assert.Equal(130_000, result.TotalSat);
        Assert.Equal(1u, result.HighestUsedExternalIndex);
    }

    /// <summary>
    /// A used address beyond the gap limit is (correctly) missed with no floor — but a stored
    /// "last issued" floor forces the scan through the empty run and finds it. This is the mechanism
    /// that keeps an actively-rotated wallet's funds visible.
    /// </summary>
    [Fact]
    public async Task Issued_floor_forces_scanning_past_an_empty_run()
    {
        var fake = new FakeExplorer();
        fake.Fund(Addr(0, 0), 100_000);
        fake.Fund(Addr(0, 5), 40_000); // the last issued index; #1..#4 are an empty run wider than the gap

        var scanner = new UtxoAccountScanner(Deriver, gapLimit: 3);

        // No floor: the scan stops in the #1..#3 gap and never reaches #5.
        var blind = await scanner.ScanAsync(Phrase, ChainId.Btc, fake, UtxoScanFloors.None);
        Assert.Equal(100_000, blind.TotalSat);

        // With the wallet's own record that it had issued through #5, the scan covers #5.
        var floors = new UtxoScanFloors(LastIssuedExternalIndex: 5, null, null, null);
        var informed = await scanner.ScanAsync(Phrase, ChainId.Btc, fake, floors);
        Assert.Equal(140_000, informed.TotalSat);
        Assert.Equal(5u, informed.HighestUsedExternalIndex);
    }

    /// <summary>A network error is reported as "not fully synced", never as a lower balance.</summary>
    [Fact]
    public async Task Explorer_failure_marks_the_result_partial_and_keeps_a_floor()
    {
        var fake = new FakeExplorer();
        fake.Fund(Addr(0, 0), 100_000);
        fake.ThrowOn.Add(Addr(0, 1)); // the very next probe fails

        var scanner = new UtxoAccountScanner(Deriver, gapLimit: 5);
        var result = await scanner.ScanAsync(Phrase, ChainId.Btc, fake, UtxoScanFloors.None);

        Assert.True(result.Partial);
        Assert.Equal(100_000, result.TotalSat); // what we know so far, presented as a floor
    }

    [Fact]
    public async Task Unconfirmed_utxos_count_as_pending_not_confirmed()
    {
        var fake = new FakeExplorer();
        fake.Fund(Addr(0, 0), 100_000, confirmed: true);
        fake.Fund(Addr(0, 0), 30_000, confirmed: false);

        var scanner = new UtxoAccountScanner(Deriver, gapLimit: 3);
        var result = await scanner.ScanAsync(Phrase, ChainId.Btc, fake, UtxoScanFloors.None);

        Assert.Equal(100_000, result.ConfirmedSat);
        Assert.Equal(30_000, result.PendingSat);
        Assert.Equal(130_000, result.TotalSat);
    }

    private sealed class FakeExplorer : IUtxoExplorer
    {
        private readonly Dictionary<string, List<ExplorerUtxo>> _utxos = new();
        private int _txCounter;

        public HashSet<string> ThrowOn { get; } = new();

        public void Fund(string address, long sat, bool confirmed = true)
        {
            if (!_utxos.TryGetValue(address, out var list))
            {
                list = new List<ExplorerUtxo>();
                _utxos[address] = list;
            }

            var txid = (++_txCounter).ToString("x64");
            list.Add(new ExplorerUtxo(txid, 0, sat, confirmed));
        }

        public Task<AddressActivity> GetActivityAsync(string address, CancellationToken ct)
        {
            if (ThrowOn.Contains(address)) throw new HttpRequestException("simulated explorer failure");
            var used = _utxos.ContainsKey(address);
            return Task.FromResult(new AddressActivity(used, used ? 1 : 0));
        }

        public Task<IReadOnlyList<ExplorerUtxo>> GetUtxosAsync(string address, CancellationToken ct)
        {
            if (ThrowOn.Contains(address)) throw new HttpRequestException("simulated explorer failure");
            IReadOnlyList<ExplorerUtxo> r = _utxos.TryGetValue(address, out var l)
                ? l
                : Array.Empty<ExplorerUtxo>();
            return Task.FromResult(r);
        }
    }
}
