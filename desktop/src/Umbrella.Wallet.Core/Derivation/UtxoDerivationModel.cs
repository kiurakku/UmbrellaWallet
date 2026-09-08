using NBitcoin;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Derivation;

/// <summary>
/// An explicit BIP44/84 leaf on a UTXO chain: which chain, which change level (0 = external receive
/// chain, 1 = internal change chain) and which address index. Making the change level a first-class
/// field — rather than overloading the address index — is what lets the HD wallet keep receive and
/// change addresses apart and spend from both. See docs/CLAUDE_IMPLEMENTATION_ROADMAP_UK.md §3.1.
/// </summary>
public readonly record struct UtxoDerivationPath(ChainId Chain, uint Change, uint Index)
{
    /// <summary>The external (receive) chain the wallet hands out to other people.</summary>
    public bool IsExternal => Change == 0;

    /// <summary>The internal chain that change outputs return to, never shown as a receive address.</summary>
    public bool IsChange => Change == 1;
}

/// <summary>
/// Everything needed to see and spend one HD address: the path it came from, the address string
/// itself, the private key that signs its inputs and the scriptPubKey those inputs pay to. All four
/// are produced together by <see cref="HdAddressDeriver.DeriveUtxoAccount"/> so they cannot drift.
/// The private key is transient signing material — the caller must not persist or log it.
/// </summary>
public sealed record DerivedUtxoAccount(
    UtxoDerivationPath Path,
    string Address,
    Key PrivateKey,
    Script ScriptPubKey);
