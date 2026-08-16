namespace Umbrella.Wallet.Core.Chains;

/// <summary>How finished a chain's wallet support is — shown honestly rather than as a blanket "ready".</summary>
public enum ChainMaturity
{
    /// <summary>Fully exercised: receive, balance, send and (where relevant) history all work.</summary>
    Stable,
    /// <summary>Works but not yet fully hardened / smoke-tested across every path.</summary>
    Beta,
    /// <summary>Partial — some operations are missing; read the per-capability flags.</summary>
    Experimental,
    /// <summary>No real support yet.</summary>
    Unavailable,
}

/// <summary>
/// The single source of truth for what the wallet can actually do on a chain. Each capability is an
/// INDEPENDENT flag — "an address can be derived" is deliberately not the same as "everything works"
/// (roadmap §5.1). The UI, Send picker, README and tests read this instead of each keeping their own
/// list, which is what let the app promise sends it couldn't make (ADA/EVM) and hide ones it could.
/// </summary>
public sealed record ChainInfo(
    ChainId Id,
    string Symbol,
    string Name,
    ChainSupportLevel Support,
    string? DerivationScheme,
    string? ReceivePathTemplate,
    // --- per-capability flags (§5.1) ---
    bool CanSend = true,
    bool CanReceive = true,
    bool CanSyncBalance = true,
    bool HasHistory = false,
    bool CanSwap = false,
    bool HasTokens = false,
    bool HardwareSupport = false,
    ChainMaturity Maturity = ChainMaturity.Stable,
    string? PrivacyNote = null);
