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
    uint? LastSeenUsedInternalIndex)
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
        CancellationToken ct = default)
    {
        var utxos = new List<OwnedUtxo>();
        var externalAddresses = new List<string>();
        long confirmed = 0, pending = 0;
        var scanned = 0;
        var partial = false;

        // external chain (change = 0): always covered at least through index 0, the default receive.
        var extForced = ForcedThrough(floors.LastIssuedExternalIndex, floors.LastSeenUsedExternalIndex, includeZero: true);
        var (extHigh, extPartial) = await ScanChainAsync(
            mnemonic, chain, change: 0, explorer, extForced, externalAddresses, utxos,
            add: (c, p) => { confirmed += c; pending += p; },
            progressCount: () => scanned, onScan: () => scanned++, progress, ct);
        partial |= extPartial;

        // internal chain (change = 1): no implicit #0, but a restore must still find used change addresses.
        var intForced = ForcedThrough(floors.LastIssuedInternalIndex, floors.LastSeenUsedInternalIndex, includeZero: false);
        var (intHigh, intPartial) = await ScanChainAsync(
            mnemonic, chain, change: 1, explorer, intForced, collectAddresses: null, utxos,
            add: (c, p) => { confirmed += c; pending += p; },
            progressCount: () => scanned, onScan: () => scanned++, progress, ct);
        partial |= intPartial;

        return new UtxoScanResult(chain, utxos, confirmed, pending, extHigh, intHigh, externalAddresses, partial);
    }

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
        IUtxoExplorer explorer,
        long forcedThrough,
        List<string>? collectAddresses,
        List<OwnedUtxo> utxos,
        Action<long, long> add,
        Func<int> progressCount,
        Action onScan,
        IProgress<UtxoScanProgress>? progress,
        CancellationToken ct)
    {
        uint? highestUsed = null;
        var consecutiveUnused = 0;

        for (uint index = 0; ; index++)
        {
            ct.ThrowIfCancellationRequested();

            var account = _deriver.DeriveBitcoinLikeAt(mnemonic, chain, change, index);
            collectAddresses?.Add(account.Address);

            AddressActivity activity;
            try
            {
                activity = await explorer.GetActivityAsync(account.Address, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // A network error is "unknown", never "empty": stop extending and flag the result
                // partial so the balance is presented as a floor, not the truth.
                return (highestUsed, true);
            }

            onScan();
            progress?.Report(new UtxoScanProgress(chain, change, index, progressCount(), activity.Used));

            if (activity.Used)
            {
                highestUsed = index;
                consecutiveUnused = 0;

                IReadOnlyList<ExplorerUtxo> found;
                try
                {
                    found = await explorer.GetUtxosAsync(account.Address, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    return (highestUsed, true);
                }

                foreach (var u in found)
                {
                    utxos.Add(new OwnedUtxo(account.Path, account.Address, u.TxId, u.Vout, u.ValueSat, u.Confirmed));
                    if (u.Confirmed) add(u.ValueSat, 0); else add(0, u.ValueSat);
                }
            }
            else
            {
                consecutiveUnused++;
            }

            if (index >= forcedThrough && consecutiveUnused >= _gapLimit)
                return (highestUsed, false);

            // Hard cap so a misbehaving explorer that always answers "used" cannot loop forever.
            if (index >= forcedThrough + _gapLimit + 10_000)
                return (highestUsed, true);
        }
    }
}
