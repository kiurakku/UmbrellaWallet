namespace Umbrella.Wallet.Core.Chains;

/// <summary>
/// Metadata for a chain exposed by the wallet core.
/// </summary>
/// <param name="CanSend">
/// Whether the app can actually build, sign and broadcast a transaction on this chain — the first of
/// the roadmap §5.1 per-capability flags. It is deliberately independent of <see cref="Support"/>:
/// a chain can derive a real address and sync a balance yet have no send path (e.g. Dogecoin), and
/// the UI must never present such a chain as fully spendable. Defaults to true.
/// </param>
public sealed record ChainInfo(
    ChainId Id,
    string Symbol,
    string Name,
    ChainSupportLevel Support,
    string? DerivationScheme,
    string? ReceivePathTemplate,
    bool CanSend = true);
