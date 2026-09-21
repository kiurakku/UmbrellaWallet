using NBitcoin;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Psbt;

namespace Umbrella.Wallet.Core.Utxo;

/// <summary>Which addresses to ask an explorer about, and which ones count as the wallet's own.</summary>
public sealed record HistoryAddressPlan(IReadOnlyList<string> Query, IReadOnlySet<string> Own);

/// <summary>
/// History for a UTXO chain (roadmap P0.1, §3.2.4): every address the wallet has used — receive AND
/// change, on every branch — and a transaction judged against all of them at once.
///
/// Two separate sets on purpose. <see cref="HistoryAddressPlan.Query"/> is the addresses that can have
/// transactions: issued or seen used, capped so a huge index never fans out into hundreds of calls.
/// <see cref="HistoryAddressPlan.Own"/> is wider — everything to the scan horizon — so change landing
/// on an index the wallet has not recorded yet is still recognised as coming back to it.
/// </summary>
public static class HistoryAddresses
{
    /// <summary>At most this many addresses per branch and chain are queried.</summary>
    public const uint MaxPerBranch = 25;

    public static HistoryAddressPlan Plan(OwnScripts own, UtxoScanFloors floors, Network network)
    {
        var ownSet = new HashSet<string>(StringComparer.Ordinal);
        var query = new List<(UtxoDerivationPath Path, string Address)>();

        foreach (var (path, script) in own.Entries)
        {
            var address = script.GetDestinationAddress(network)?.ToString();
            if (address is null) continue;
            ownSet.Add(address);

            if (Reach(path, floors) is { } reach && path.Index <= Math.Min(reach, MaxPerBranch))
                query.Add((path, address));
        }

        var ordered = query
            .OrderBy(q => q.Path.Kind).ThenBy(q => q.Path.Change).ThenBy(q => q.Path.Index)
            .Select(q => q.Address)
            .ToList();

        return new HistoryAddressPlan(ordered, ownSet);
    }

    /// <summary>How far one branch has been used or handed out; null for a branch never touched. The
    /// default receive chain always includes #0 — it is the address shown from the first unlock.</summary>
    private static uint? Reach(UtxoDerivationPath path, UtxoScanFloors f) => (path.Kind, path.Change) switch
    {
        (UtxoScriptKind.Default, 0) => Max(f.LastIssuedExternalIndex, f.LastSeenUsedExternalIndex) ?? 0,
        (UtxoScriptKind.Default, _) => Max(f.LastIssuedInternalIndex, f.LastSeenUsedInternalIndex),
        (UtxoScriptKind.Taproot, 0) => f.TaprootLastSeenUsedExternalIndex,
        (UtxoScriptKind.Taproot, _) => Max(f.TaprootLastIssuedInternalIndex, f.TaprootLastSeenUsedInternalIndex),
        _ => null,
    };

    private static uint? Max(uint? a, uint? b) =>
        a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
}
