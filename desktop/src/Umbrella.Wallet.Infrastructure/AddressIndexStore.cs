using System.Text.Json;
using System.Text.Json.Serialization;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Utxo;

namespace Umbrella.Wallet.Infrastructure;

/// <summary>
/// The receive/change address state for one wallet on one UTXO chain. Indices are not secret
/// (addresses are public); this just lets the same addresses be re-derived and scanned after a
/// restart. <c>change = 0</c> is the external (receive) chain, <c>change = 1</c> the internal
/// (change) chain — see docs/CLAUDE_IMPLEMENTATION_ROADMAP_UK.md §3.1.
/// </summary>
public sealed class UtxoAddressState
{
    /// <summary>Highest external index handed out. Index 0 is always implicitly issued (it is the
    /// wallet's default receive address), so a fresh wallet reserves #1 next.</summary>
    public uint? LastIssuedExternalIndex { get; set; }

    /// <summary>Highest internal (change) index reserved. Null until the first change output, which
    /// takes internal index 0 — change addresses have no implicit #0 the way receive does.</summary>
    public uint? LastIssuedInternalIndex { get; set; }

    /// <summary>Highest external index observed to have on-chain history. Raises the gap-limit scan
    /// floor so a restored wallet keeps discovering past a run of unused-but-issued addresses.</summary>
    public uint? LastSeenUsedExternalIndex { get; set; }

    /// <summary>Highest internal index observed to have on-chain history.</summary>
    public uint? LastSeenUsedInternalIndex { get; set; }
}

/// <summary>
/// Persists per-wallet, per-chain UTXO address state to data/addr-indexes.json as one versioned,
/// atomically-written JSON document. Reserving an index writes to disk BEFORE returning, and throws
/// if that write fails — a receive address or a change output must never be handed out on an index
/// the wallet might forget after a crash (roadmap §3.1.6, "fail-closed, not best-effort").
/// </summary>
public sealed class AddressIndexStore
{
    private const int CurrentSchemaVersion = 2;

    private readonly string _path;
    private readonly object _gate = new();
    private FileModel _model = new();

    public AddressIndexStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppPaths.DataRoot, "addr-indexes.json");
        Load();
    }

    private sealed class FileModel
    {
        [JsonPropertyName("version")] public int Version { get; set; } = CurrentSchemaVersion;

        [JsonPropertyName("entries")]
        public Dictionary<string, UtxoAddressState> Entries { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private static string Key(string walletId, string chain) => $"{walletId}:{chain.ToUpperInvariant()}";

    /// <summary>
    /// The chain key a branch's state is filed under. BIP86 Taproot is a SEPARATE account on a
    /// separate purpose, so its indices must not share a counter with the default branch — reserving
    /// change index 4 on one says nothing about index 4 on the other (roadmap P2.1). Suffixing the
    /// chain keeps that apart without a schema change, and an older build simply never looks at the
    /// suffixed entries.
    /// </summary>
    public static string BranchKey(string chain, UtxoScriptKind kind) =>
        kind == UtxoScriptKind.Taproot ? chain.ToUpperInvariant() + "-TR" : chain;

    /// <summary>
    /// The scan floors for every branch of one chain, in one call — so a caller cannot remember the
    /// default branch and forget the Taproot one, which would quietly re-narrow the scan and hide
    /// restored Taproot funds again.
    /// </summary>
    public UtxoScanFloors FloorsFor(string walletId, string chain)
    {
        var s = GetState(walletId, chain);
        var t = GetState(walletId, BranchKey(chain, UtxoScriptKind.Taproot));
        return new UtxoScanFloors(
            s.LastIssuedExternalIndex, s.LastSeenUsedExternalIndex,
            s.LastIssuedInternalIndex, s.LastSeenUsedInternalIndex,
            t.LastSeenUsedExternalIndex, t.LastIssuedInternalIndex, t.LastSeenUsedInternalIndex);
    }

    /// <summary>Records what a completed scan found, on every branch it walked, so the next scan
    /// starts from at least as far out as this one reached.</summary>
    public void RecordScan(string walletId, string chain, UtxoScanResult scan)
    {
        if (scan.HighestUsedExternalIndex is { } he) RecordSeenUsed(walletId, chain, 0, he);
        if (scan.HighestUsedInternalIndex is { } hi) RecordSeenUsed(walletId, chain, 1, hi);

        var taproot = BranchKey(chain, UtxoScriptKind.Taproot);
        if (scan.HighestUsedTaprootExternalIndex is { } te) RecordSeenUsed(walletId, taproot, 0, te);
        if (scan.HighestUsedTaprootInternalIndex is { } ti) RecordSeenUsed(walletId, taproot, 1, ti);
    }

    /// <summary>A read-only snapshot of the state for a wallet + chain (never null; empty by default).</summary>
    public UtxoAddressState GetState(string walletId, string chain)
    {
        lock (_gate)
        {
            var s = _model.Entries.TryGetValue(Key(walletId, chain), out var v) ? v : new UtxoAddressState();
            // Hand back a copy so callers can't mutate stored state without going through Reserve/Record.
            return new UtxoAddressState
            {
                LastIssuedExternalIndex = s.LastIssuedExternalIndex,
                LastIssuedInternalIndex = s.LastIssuedInternalIndex,
                LastSeenUsedExternalIndex = s.LastSeenUsedExternalIndex,
                LastSeenUsedInternalIndex = s.LastSeenUsedInternalIndex,
            };
        }
    }

    /// <summary>
    /// Reserves and persists the next external (receive) index, then returns it. Index 0 is the
    /// implicit default receive address, so the first reservation yields 1. Persisted before return;
    /// throws on write failure so the caller can abort rather than show an index it may lose.
    /// </summary>
    public uint ReserveNextExternalIndex(string walletId, string chain)
    {
        lock (_gate)
        {
            var s = Entry(walletId, chain);
            var next = (s.LastIssuedExternalIndex ?? 0u) + 1u;
            s.LastIssuedExternalIndex = next;
            Save();
            return next;
        }
    }

    /// <summary>
    /// Reserves and persists the next internal (change) index, then returns it. The first change
    /// output takes index 0. Persisted before return; throws on write failure.
    /// </summary>
    public uint ReserveNextChangeIndex(string walletId, string chain)
    {
        lock (_gate)
        {
            var s = Entry(walletId, chain);
            var next = s.LastIssuedInternalIndex.HasValue ? s.LastIssuedInternalIndex.Value + 1u : 0u;
            s.LastIssuedInternalIndex = next;
            Save();
            return next;
        }
    }

    /// <summary>
    /// Records that discovery found on-chain history at a given leaf, raising the last-seen-used
    /// floor (and the issued floor, since a used address is by definition issued). Best-effort on
    /// disk: losing this only means the next scan re-derives it, never a lost or reused address.
    /// </summary>
    public void RecordSeenUsed(string walletId, string chain, uint change, uint index)
    {
        lock (_gate)
        {
            var s = Entry(walletId, chain);
            if (change == 0)
            {
                if (index > (s.LastSeenUsedExternalIndex ?? 0u) || s.LastSeenUsedExternalIndex is null)
                    s.LastSeenUsedExternalIndex = Max(s.LastSeenUsedExternalIndex, index);
                s.LastIssuedExternalIndex = Max(s.LastIssuedExternalIndex, index);
            }
            else
            {
                s.LastSeenUsedInternalIndex = Max(s.LastSeenUsedInternalIndex, index);
                s.LastIssuedInternalIndex = Max(s.LastIssuedInternalIndex, index);
            }

            try { Save(); }
            catch { /* a re-scan re-derives this; unlike Reserve, nothing is handed out here */ }
        }
    }

    private static uint Max(uint? current, uint candidate) =>
        current.HasValue ? Math.Max(current.Value, candidate) : candidate;

    private UtxoAddressState Entry(string walletId, string chain)
    {
        var k = Key(walletId, chain);
        if (!_model.Entries.TryGetValue(k, out var s))
        {
            s = new UtxoAddressState();
            _model.Entries[k] = s;
        }

        return s;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json)) return;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Current format: { "version": N, "entries": { "wallet:CHAIN": {...} } }
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("entries", out _))
            {
                _model = JsonSerializer.Deserialize<FileModel>(json, JsonOpts) ?? new FileModel();
            }
            // Legacy v1 format: a flat { "wallet:CHAIN": intIndex } map. Migrate the old value as the
            // last-issued EXTERNAL index so an upgrading user does not lose a rotated receive address.
            else if (root.ValueKind == JsonValueKind.Object)
            {
                var legacy = JsonSerializer.Deserialize<Dictionary<string, int>>(json)
                             ?? new Dictionary<string, int>();
                _model = new FileModel();
                foreach (var (k, v) in legacy)
                {
                    _model.Entries[k] = new UtxoAddressState
                    {
                        LastIssuedExternalIndex = v > 0 ? (uint)v : null,
                    };
                }
            }
        }
        catch
        {
            // A corrupt file must not wedge the wallet; start clean. Reserve will overwrite it
            // atomically on the next hand-out.
            _model = new FileModel();
        }
    }

    /// <summary>
    /// Atomically writes the whole document: serialise to a temp file, flush, then move over the
    /// target so a crash mid-write can never leave a truncated index file. Throws on failure.
    /// </summary>
    private void Save()
    {
        _model.Version = CurrentSchemaVersion;
        var dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);

        var tmp = _path + ".tmp";
        var json = JsonSerializer.Serialize(_model, JsonOpts);
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);
    }
}
