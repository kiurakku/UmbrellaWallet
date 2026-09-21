using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Core.Utxo;

/// <summary>
/// One unspent output the wallet controls, tagged with the exact HD leaf it was received on so the
/// spender can derive the matching signing key. Discovered across every issued external and internal
/// address, not just receive #0.
/// </summary>
public sealed record OwnedUtxo(
    UtxoDerivationPath Path,
    string Address,
    string TxId,
    int Vout,
    long ValueSat,
    bool Confirmed);

/// <summary>Per-address progress during a scan, so the UI can show "Discovering BTC — 12/40".</summary>
public sealed record UtxoScanProgress(
    ChainId Chain, uint Change, uint Index, int Scanned, bool Used,
    UtxoScriptKind Kind = UtxoScriptKind.Default);

/// <summary>
/// The aggregate result of scanning one chain. Balances are split confirmed/pending and never
/// substitute an error for zero: if any address query failed, <see cref="Partial"/> is true and the
/// totals are a floor the UI must present as "not fully synced", not as the real balance
/// (roadmap §1.10, §3.2.5).
/// </summary>
public sealed record UtxoScanResult(
    ChainId Chain,
    IReadOnlyList<OwnedUtxo> Utxos,
    long ConfirmedSat,
    long PendingSat,
    uint? HighestUsedExternalIndex,
    uint? HighestUsedInternalIndex,
    IReadOnlyList<string> ExternalAddresses,
    bool Partial,
    /// <summary>The highest used index found on the BIP86 Taproot branch, if that branch was scanned
    /// (roadmap P2.1). Tracked apart from the default branch because they are different accounts on
    /// different purposes, and one being empty says nothing about the other.</summary>
    uint? HighestUsedTaprootExternalIndex = null,
    uint? HighestUsedTaprootInternalIndex = null)
{
    public long TotalSat => ConfirmedSat + PendingSat;

    /// <summary>True when any of the discovered outputs sit on Taproot addresses — the wallet still
    /// issues native SegWit, so this only happens for a seed restored from a Taproot wallet.</summary>
    public bool HasTaprootFunds => Utxos.Any(u => u.Path.IsTaproot);

    public static UtxoScanResult Empty(ChainId chain) =>
        new(chain, Array.Empty<OwnedUtxo>(), 0, 0, null, null, Array.Empty<string>(), false);
}
