using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Derivation;

/// <summary>
/// Thrown when address derivation is requested for a planned/unsupported chain.
/// </summary>
public sealed class UnsupportedChainException : NotSupportedException
{
    public ChainId ChainId { get; }

    public UnsupportedChainException(ChainId chainId)
        : base($"Receive-address derivation for {chainId} is planned but not supported in this MVP.")
    {
        ChainId = chainId;
    }
}

/// <summary>
/// Thrown when a chain is asked to derive with a non-empty BIP39 passphrase but its scheme cannot
/// honour one. Cardano's Icarus derivation is built from the raw entropy, so a passphrase has no
/// effect there — rather than silently return the base wallet's address (which would break the
/// hidden-wallet guarantee), the deriver refuses, and the app hides that chain under a passphrase.
/// </summary>
public sealed class PassphraseUnsupportedException : NotSupportedException
{
    public ChainId ChainId { get; }

    public PassphraseUnsupportedException(ChainId chainId)
        : base($"{chainId} cannot derive a distinct address for a BIP39 passphrase (its scheme ignores the passphrase).")
    {
        ChainId = chainId;
    }
}
