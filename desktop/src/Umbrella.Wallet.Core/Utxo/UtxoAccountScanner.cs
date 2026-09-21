using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Core.Utxo;

/// <summary>
/// The floors a scan must cover even when the addresses currently look empty: indices the wallet has
/// already issued or previously seen used. They guarantee a restored wallet keeps scanning past a run
/// of issued-but-empty addresses instead of stopping at the first gap.
/// </summary>
public sealed record UtxoScanFloors(
    uint? LastIssuedExternalIndex,
    uint? LastSeenUsedExternalIndex,
    uint? LastIssuedInternalIndex,
    uint? LastSeenUsedInternalIndex,
    /// <summary>The same floors for the BIP86 Taproot branch. There is no "issued" pair: the wallet
    /// never hands out a Taproot receive address, it only finds and spends ones a previous wallet used
    /// (roadmap P2.1). A Taproot CHANGE index does get reserved, and it lands in the internal floor.</summary>
    uint? TaprootLastSeenUsedExternalIndex = null,
    uint? TaprootLastIssuedInternalIndex = null,
    uint? TaprootLastSeenUsedInternalIndex = null)
{
    public static UtxoScanFloors None { get; } = new(null, null, null, null);
}

/// <summary>
/// Discovers the funded addresses of one BTC/LTC/DOGE wallet by walking the external and internal
/// BIP44/84 chains with a gap limit, and aggregates their unspent outputs. Pure with respect to the
/// network: it reads through an <see cref="IUtxoExplorer"/>, so the whole discovery/restore flow is
/// testable offline (roadmap §3.2). A transient explorer error is never read as "empty" — it marks
/// the result <see cref="UtxoScanResult.Partial"/> and stops extending, so the caller shows
/// "not fully synced" rather than a too-low balance.
/// </summary>
public sealed class UtxoAccountScanner
{
    public const int DefaultGapLimit = 20;

    /// <summary>
    /// The look-ahead of a REFRESH, once a full gap-limit walk has already covered the wallet this
    /// session. The floors carry every address the wallet has issued or seen used, so a refresh only
    /// has to re-read those plus a few past them. A full walk of twenty on every branch every minute
    /// is what got the wallet rate-limited by the free explorers — and a rate-limited explorer shows
    /// the user an unreadable balance. Full walks still run on unlock and periodically (the caller
    /// decides), so money sent further out by another wallet on the same seed is found then.
    /// </summary>
    public const int RefreshGapLimit = 3;

    /// <summary>How many address probes may be in flight at once. Enough to collapse a gap-limit walk
    /// from ~21 sequential round-trips into a handful of waves, low enough not to trip the rate limits
    /// of public explorers (a 429 marks the scan partial, which is worse than being a little slower).</summary>
    public const int MaxParallelProbes = 6;

    private readonly HdAddressDeriver _deriver;
    private readonly int _gapLimit;

    public UtxoAccountScanner(HdAddressDeriver? deriver = null, int gapLimit = DefaultGapLimit)
    {
        _deriver = deriver ?? new HdAddressDeriver();
        _gapLimit = gapLimit > 0 ? gapLimit : DefaultGapLimit;
    }

    public async Task<UtxoScanResult> ScanAsync(
        string mnemonic,
        ChainId chain,
        IUtxoExplorer explorer,
        UtxoScanFloors floors,
        IProgress<UtxoScanProgress>? progress = null,
        CancellationToken ct = default,
        int? gapLimit = null)
    {
        var gap = gapLimit is > 0 ? gapLimit.Value : _gapLimit;
        var utxos = new List<OwnedUtxo>();
        var externalAddresses = new List<string>();
        long confirmed = 0, pending = 0;
        var scanned = 0;
        var partial = false;

        void Add(long c, long p) { confirmed += c; pending += p; }

        // external chain (change = 0): always covered at least through index 0, the default receive.
        var extForced = ForcedThrough(floors.LastIssuedExternalIndex, floors.LastSeenUsedExternalIndex, includeZero: true);
        var (extHigh, extPartial) = await ScanChainAsync(
            mnemonic, chain, change: 0, UtxoScriptKind.Default, explorer, extForced, externalAddresses, utxos,
            Add, progressCount: () => scanned, onScan: () => scanned++, progress, gap, ct);
        partial |= extPartial;

        // internal chain (change = 1): no implicit #0, but a restore must still find used change addresses.
        var intForced = ForcedThrough(floors.LastIssuedInternalIndex, floors.LastSeenUsedInternalIndex, includeZero: false);
        var (intHigh, intPartial) = await ScanChainAsync(
            mnemonic, chain, change: 1, UtxoScriptKind.Default, explorer, intForced, collectAddresses: null, utxos,
            Add, progressCount: () => scanned, onScan: () => scanned++, progress, gap, ct);
        partial |= intPartial;

        uint? trExtHigh = null, trIntHigh = null;

        // BIP86 Taproot lives on a DIFFERENT purpose, so none of the work above can see it. A seed
        // restored from a Taproot wallet would otherwise read as empty while the coins sat in plain
        // sight — the wallet would be telling the user a number it had not actually checked.
        //
        // The cost is real and is not hidden: this doubles the addresses BTC reveals to the explorer
        // per scan. It is spent because a balance that silently omits a branch is worse than a scan
        // that is twice as wide, and it is spent only on Bitcoin, the only chain with a Taproot branch.
        if (ScansTaproot(chain))
        {
            var trExtForced = ForcedThrough(null, floors.TaprootLastSeenUsedExternalIndex, includeZero: true);
            var (h1, p1) = await ScanChainAsync(
                mnemonic, chain, change: 0, UtxoScriptKind.Taproot, explorer, trExtForced,
                collectAddresses: null, utxos,
                Add, progressCount: () => scanned, onScan: () => scanned++, progress, gap, ct);
            trExtHigh = h1;
            partial |= p1;

            var trIntForced = ForcedThrough(
                floors.TaprootLastIssuedInternalIndex, floors.TaprootLastSeenUsedInternalIndex, includeZero: false);
            var (h2, p2) = await ScanChainAsync(
                mnemonic, chain, change: 1, UtxoScriptKind.Taproot, explorer, trIntForced,
                collectAddresses: null, utxos,
                Add, progressCount: () => scanned, onScan: () => scanned++, progress, gap, ct);
            trIntHigh = h2;
            partial |= p2;
        }

        return new UtxoScanResult(
            chain, utxos, confirmed, pending, extHigh, intHigh, externalAddresses, partial,
            trExtHigh, trIntHigh);
    }

    /// <summary>
    /// Which chains have a Taproot branch worth walking. Bitcoin only: Litecoin's Taproot is a
    /// different deployment the wallet does not derive, and DOGE/BCH have none at all. Naming the
    /// chains here — rather than trying every kind everywhere — keeps the scan from revealing
    /// addresses that could not hold anything.
    /// </summary>
    public static bool ScansTaproot(ChainId chain) => chain == ChainId.Btc;

    /// <summary>The highest index the scan is obliged to reach; -1 means "no obligation, gap-scan from 0".</summary>
    private static long ForcedThrough(uint? lastIssued, uint? lastSeenUsed, bool includeZero)
    {
        long forced = includeZero ? 0 : -1;
        if (lastIssued.HasValue) forced = Math.Max(forced, lastIssued.Value);
        if (lastSeenUsed.HasValue) forced = Math.Max(forced, lastSeenUsed.Value);
        return forced;
    }

    private async Task<(uint? HighestUsed, bool Partial)> ScanChainAsync(
        string mnemonic,
        ChainId chain,
        uint change,
        UtxoScriptKind kind,
        IUtxoExplorer explorer,
        long forcedThrough,
        List<string>? collectAddresses,
        List<OwnedUtxo> utxos,
        Action<long, long> add,
        Func<int> progressCount,
        Action onScan,
        IProgress<UtxoScanProgress>? progress,
        int gapLimit,
        CancellationToken ct)
    {
        uint? highestUsed = null;
        var consecutiveUnused = 0;

        // A network error is "unknown", never "empty". These wrappers turn one into null so the walk
        // below can stop extending and flag the result partial — the balance is then a floor, not the
        // truth — while a cancel still propagates.
        //
        // Only OUR cancel propagates. HttpClient reports a request that timed out as a
        // TaskCanceledException too, and treating that as "the user cancelled" aborted the whole scan
        // on one slow explorer — every Bitcoin balance on a rate-limited Blockstream showed as
        // unreadable, while a working fallback server was never asked.
        async Task<AddressActivity?> ProbeAsync(string address)
        {
            try { return await explorer.GetActivityAsync(address, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { return null; }
        }

        async Task<IReadOnlyList<ExplorerUtxo>?> FetchUtxosAsync(string address)
        {
            try { return await explorer.GetUtxosAsync(address, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { return null; }
        }

        for (uint index = 0; ; )
        {
            ct.ThrowIfCancellationRequested();

            // How many more addresses could this walk still need? Enough to complete the gap run, and
            // enough to cover any index the wallet is obliged to reach. Probing that many CONCURRENTLY
            // is what makes a scan fast — the old walk did one HTTP round-trip per address, so a fresh
            // wallet paid 21+ sequential round-trips per chain before showing a balance.
            //
            // The window is deliberately sized to what a strictly sequential walk would have queried
            // anyway, so this never reveals extra addresses to the explorer — a privacy property, not
            // just an optimisation. Concurrency is capped so a burst cannot trip explorer rate limits
            // (a 429 would mark the scan partial and end up slower).
            var stillNeeded = Math.Max(1L, gapLimit - consecutiveUnused);
            if (forcedThrough >= index) stillNeeded = Math.Max(stillNeeded, forcedThrough - index + 1);
            var window = (int)Math.Min(stillNeeded, MaxParallelProbes);

            var batch = new List<DerivedUtxoAccount>(window);
            for (var k = 0; k < window; k++)
                batch.Add(_deriver.DeriveBitcoinLikeAt(mnemonic, chain, change, index + (uint)k, kind: kind));

            var activities = await Task.WhenAll(batch.Select(a => ProbeAsync(a.Address)));

            // Walk the batch in index order so the gap rule, progress reporting and the collected
            // address list stay byte-for-byte what the sequential walk produced.
            var usedInBatch = new List<(int Offset, DerivedUtxoAccount Account)>();
            var stop = false;
            var stopPartial = false;
            var consumed = 0;

            for (var k = 0; k < batch.Count; k++)
            {
                var activity = activities[k];
                if (activity is null) { stop = true; stopPartial = true; break; }

                consumed = k + 1;
                collectAddresses?.Add(batch[k].Address);
                onScan();
                progress?.Report(new UtxoScanProgress(chain, change, index + (uint)k, progressCount(), activity.Used, kind));

                if (activity.Used)
                {
                    highestUsed = index + (uint)k;
                    consecutiveUnused = 0;
                    usedInBatch.Add((k, batch[k]));
                }
                else
                {
                    consecutiveUnused++;
                }

                if (index + (uint)k >= forcedThrough && consecutiveUnused >= gapLimit) { stop = true; break; }

                // Hard cap so a misbehaving explorer that always answers "used" cannot loop forever.
                if (index + (uint)k >= forcedThrough + gapLimit + 10_000) { stop = true; stopPartial = true; break; }
            }

            // Pull the unspent outputs of the used addresses — also concurrently, then applied in index
            // order so the resulting UTXO list is deterministic.
            if (usedInBatch.Count > 0)
            {
                var fetched = await Task.WhenAll(usedInBatch.Select(u => FetchUtxosAsync(u.Account.Address)));
                for (var u = 0; u < usedInBatch.Count; u++)
                {
                    var found = fetched[u];
                    if (found is null) return (highestUsed, true);

                    var account = usedInBatch[u].Account;
                    foreach (var x in found)
                    {
                        utxos.Add(new OwnedUtxo(account.Path, account.Address, x.TxId, x.Vout, x.ValueSat, x.Confirmed));
                        if (x.Confirmed) add(x.ValueSat, 0); else add(0, x.ValueSat);
                    }
                }
            }

            if (stop) return (highestUsed, stopPartial);

            index += (uint)Math.Max(consumed, 1);
        }
    }
}
