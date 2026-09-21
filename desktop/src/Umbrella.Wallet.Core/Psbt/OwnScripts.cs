using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Utxo;

namespace Umbrella.Wallet.Core.Psbt;

/// <summary>
/// Every script this wallet could have handed out or sent change to on one chain, mapped back to the
/// path it came from — enough to say of any PSBT output "that one comes back to you" (roadmap H.1).
///
/// It covers each branch the scanner walks (native SegWit and, on Bitcoin, Taproot), both the receive
/// and the change chain, through the highest index the wallet has issued or seen used plus a full gap
/// — the same horizon the scanner itself looks across. Built from the account-level public keys, so
/// no private key is derived to answer "is this ours?".
/// </summary>
public sealed class OwnScripts
{
    private readonly Dictionary<Script, UtxoDerivationPath> _paths;

    private OwnScripts(Dictionary<Script, UtxoDerivationPath> paths) => _paths = paths;

    public int Count => _paths.Count;

    public bool Contains(Script script) => _paths.ContainsKey(script);

    public bool TryGetPath(Script script, out UtxoDerivationPath path) => _paths.TryGetValue(script, out path);

    /// <summary>Every script and the leaf it came from — receive and change, every branch.</summary>
    public IEnumerable<(UtxoDerivationPath Path, Script Script)> Entries =>
        _paths.Select(p => (p.Value, p.Key));

    public static OwnScripts For(
        HdAddressDeriver deriver, string mnemonic, ChainId chain, UtxoScanFloors floors,
        int gap = UtxoAccountScanner.DefaultGapLimit)
    {
        var (_, _, network, _) = HdAddressDeriver.BitcoinLikeParams(chain);
        var paths = new Dictionary<Script, UtxoDerivationPath>();

        var kinds = UtxoAccountScanner.ScansTaproot(chain)
            ? new[] { UtxoScriptKind.Default, UtxoScriptKind.Taproot }
            : new[] { UtxoScriptKind.Default };

        foreach (var kind in kinds)
        {
            var account = deriver.DeriveAccountExtPubKey(mnemonic, chain, kind);
            var (_, scriptType) = HdAddressDeriver.BranchParams(chain, kind);

            var (externalReach, internalReach) = kind == UtxoScriptKind.Taproot
                ? (Max(null, floors.TaprootLastSeenUsedExternalIndex),
                   Max(floors.TaprootLastIssuedInternalIndex, floors.TaprootLastSeenUsedInternalIndex))
                : (Max(floors.LastIssuedExternalIndex, floors.LastSeenUsedExternalIndex),
                   Max(floors.LastIssuedInternalIndex, floors.LastSeenUsedInternalIndex));

            foreach (var (change, reach) in new[] { (0u, externalReach), (1u, internalReach) })
            {
                var branch = account.Derive(change);
                var last = (reach ?? 0) + (uint)gap;
                for (uint i = 0; i <= last; i++)
                {
                    var script = branch.Derive(i).PubKey.GetAddress(scriptType, network).ScriptPubKey;
                    paths[script] = new UtxoDerivationPath(chain, change, i, kind);
                }
            }
        }

        return new OwnScripts(paths);
    }

    private static uint? Max(uint? a, uint? b) =>
        a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
}
