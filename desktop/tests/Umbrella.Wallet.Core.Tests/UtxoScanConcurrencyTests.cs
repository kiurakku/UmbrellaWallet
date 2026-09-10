using System.Collections.Concurrent;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Utxo;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The gap-limit walk probes addresses in concurrent windows instead of one HTTP round-trip at a time.
/// Two properties have to hold for that to be a safe trade:
///
/// 1. PRIVACY — it must never query MORE addresses than a strictly sequential walk would have. Every
///    address handed to an explorer is an address linked to this wallet, so "faster" must not mean
///    "reveals more".
/// 2. SPEED — the probes must actually overlap, otherwise nothing was gained.
///
/// Correctness of the discovered balance is covered by <see cref="UtxoDiscoveryTests"/>; these tests
/// exist to stop a future refactor from quietly widening the window or serialising it again.
/// </summary>
public sealed class UtxoScanConcurrencyTests
{
    private const string Phrase =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    [Fact]
    public async Task An_empty_wallet_probes_exactly_the_gap_limit_on_each_chain()
    {
        // A sequential walk stops after `gapLimit` consecutive unused addresses: indices 0..19 on the
        // external chain, and the same on the internal one. The windowed walk must query that exact set
        // — no speculative look-ahead.
        var fake = new RecordingExplorer();
        var scanner = new UtxoAccountScanner(gapLimit: 20);

        await scanner.ScanAsync(Phrase, ChainId.Btc, fake, UtxoScanFloors.None);

        Assert.Equal(40, fake.Probed.Count);                    // 20 external + 20 internal
        Assert.Equal(fake.Probed.Count, fake.Probed.Distinct().Count()); // and never the same one twice
    }

    [Fact]
    public async Task A_used_address_extends_the_walk_by_exactly_one_gap_run()
    {
        // Funding index 0 resets the gap counter there, so the walk must continue to index 20 and stop —
        // 21 external probes. Anything more would mean the window over-reached.
        var fake = new RecordingExplorer();
        var scanner = new UtxoAccountScanner(gapLimit: 20);

        var addr0 = new Umbrella.Wallet.Core.Derivation.HdAddressDeriver()
            .DeriveBitcoinLikeAt(Phrase, ChainId.Btc, change: 0, index: 0).Address;
        fake.Fund(addr0, 50_000);

        var result = await scanner.ScanAsync(Phrase, ChainId.Btc, fake, UtxoScanFloors.None);

        Assert.Equal(50_000, result.ConfirmedSat);
        Assert.Equal(0u, result.HighestUsedExternalIndex);
        Assert.Equal(41, fake.Probed.Count); // 21 external + 20 internal
    }

    [Fact]
    public async Task Probes_actually_run_concurrently()
    {
        // The whole point of the change. With a sequential walk the peak in-flight count is 1.
        var fake = new RecordingExplorer { HoldMs = 25 };
        var scanner = new UtxoAccountScanner(gapLimit: 20);

        await scanner.ScanAsync(Phrase, ChainId.Btc, fake, UtxoScanFloors.None);

        Assert.True(fake.PeakInFlight > 1, $"probes did not overlap (peak in-flight was {fake.PeakInFlight})");
        Assert.True(fake.PeakInFlight <= UtxoAccountScanner.MaxParallelProbes,
            $"probe burst exceeded the rate-limit cap (peak {fake.PeakInFlight} > {UtxoAccountScanner.MaxParallelProbes})");
    }

    [Fact]
    public async Task A_network_error_still_marks_the_scan_partial_rather_than_reporting_a_low_balance()
    {
        // Concurrency must not weaken the "an error is unknown, never empty" rule: a failed probe inside
        // a window has to surface as Partial, so the UI presents a floor instead of a wrong total.
        var fake = new RecordingExplorer();
        var deriver = new Umbrella.Wallet.Core.Derivation.HdAddressDeriver();
        fake.ThrowOn.Add(deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, change: 0, index: 3).Address);

        var scanner = new UtxoAccountScanner(gapLimit: 20);
        var result = await scanner.ScanAsync(Phrase, ChainId.Btc, fake, UtxoScanFloors.None);

        Assert.True(result.Partial);
    }

    /// <summary>Records every probed address and tracks how many calls are in flight at once.</summary>
    private sealed class RecordingExplorer : IUtxoExplorer
    {
        private readonly Dictionary<string, List<ExplorerUtxo>> _utxos = new();
        private readonly object _gate = new();
        private int _inFlight;
        private int _txCounter;

        public ConcurrentQueue<string> Probed { get; } = new();
        public HashSet<string> ThrowOn { get; } = new();
        public int PeakInFlight { get; private set; }

        /// <summary>Milliseconds each probe is held open, so overlapping calls are observable.</summary>
        public int HoldMs { get; init; }

        public void Fund(string address, long sat, bool confirmed = true)
        {
            if (!_utxos.TryGetValue(address, out var list))
            {
                list = new List<ExplorerUtxo>();
                _utxos[address] = list;
            }

            list.Add(new ExplorerUtxo((++_txCounter).ToString("x64"), 0, sat, confirmed));
        }

        public async Task<AddressActivity> GetActivityAsync(string address, CancellationToken ct)
        {
            lock (_gate)
            {
                _inFlight++;
                if (_inFlight > PeakInFlight) PeakInFlight = _inFlight;
            }

            try
            {
                Probed.Enqueue(address);
                if (HoldMs > 0) await Task.Delay(HoldMs, ct);
                if (ThrowOn.Contains(address)) throw new HttpRequestException("simulated explorer failure");
                var used = _utxos.ContainsKey(address);
                return new AddressActivity(used, used ? 1 : 0);
            }
            finally
            {
                lock (_gate) _inFlight--;
            }
        }

        public Task<IReadOnlyList<ExplorerUtxo>> GetUtxosAsync(string address, CancellationToken ct)
        {
            if (ThrowOn.Contains(address)) throw new HttpRequestException("simulated explorer failure");
            IReadOnlyList<ExplorerUtxo> r = _utxos.TryGetValue(address, out var l) ? l : Array.Empty<ExplorerUtxo>();
            return Task.FromResult(r);
        }
    }
}
