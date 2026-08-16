namespace Umbrella.Wallet.Core.Chains;

/// <summary>
/// Static catalog of chains for the desktop MVP.
/// Supported chains have real HD derivation; planned chains are explicit stubs.
/// </summary>
public static class ChainCatalog
{
    private static readonly IReadOnlyDictionary<ChainId, ChainInfo> ById;

    public static IReadOnlyList<ChainInfo> All { get; }

    static ChainCatalog()
    {
        All =
        [
            new ChainInfo(
                ChainId.Btc, "BTC", "Bitcoin", ChainSupportLevel.Supported, "BIP84", "m/84'/0'/0'/0/{index}",
                CanSend: true, CanReceive: true, CanSyncBalance: true, HasHistory: true, CanSwap: true,
                HasTokens: false, Maturity: ChainMaturity.Stable,
                PrivacyNote: "Public ledger — use Tor and a fresh address per receive to reduce linking."),
            new ChainInfo(
                ChainId.Eth, "ETH", "Ethereum", ChainSupportLevel.Supported, "BIP44", "m/44'/60'/0'/0/{index}",
                CanSend: true, CanReceive: true, CanSyncBalance: true, HasHistory: true, CanSwap: true,
                HasTokens: true, Maturity: ChainMaturity.Stable,
                PrivacyNote: "Public ledger; a reused address links all of your activity."),
            new ChainInfo(
                ChainId.Ltc, "LTC", "Litecoin", ChainSupportLevel.Supported, "BIP84", "m/84'/2'/0'/0/{index}",
                CanSend: true, CanReceive: true, CanSyncBalance: true, HasHistory: true, CanSwap: true,
                HasTokens: false, Maturity: ChainMaturity.Stable,
                PrivacyNote: "Public ledger — use Tor and a fresh address per receive to reduce linking."),
            new ChainInfo(
                ChainId.Doge, "DOGE", "Dogecoin", ChainSupportLevel.Supported, "BIP44", "m/44'/3'/0'/0/{index}",
                // Address + balance work, but there is no Dogecoin send path yet — it must not look spendable.
                CanSend: false, CanReceive: true, CanSyncBalance: true, HasHistory: false, CanSwap: true,
                HasTokens: false, Maturity: ChainMaturity.Experimental,
                PrivacyNote: "Receive and hold only — no send path yet."),
            new ChainInfo(
                ChainId.Tron, "TRX", "TRON", ChainSupportLevel.Supported, "BIP44", "m/44'/195'/0'/0/{index}",
                CanSend: true, CanReceive: true, CanSyncBalance: true, HasHistory: true, CanSwap: false,
                HasTokens: true, Maturity: ChainMaturity.Stable,
                PrivacyNote: "Public ledger; a new account burns TRX for energy on its first USDT transfer."),
            new ChainInfo(
                ChainId.Sol, "SOL", "Solana", ChainSupportLevel.Supported, "SLIP-0010 ed25519", "m/44'/501'/0'/{index}'",
                CanSend: true, CanReceive: true, CanSyncBalance: true, HasHistory: false, CanSwap: false,
                HasTokens: true, Maturity: ChainMaturity.Beta,
                PrivacyNote: "Public ledger; on-chain history is not synced yet."),
            new ChainInfo(
                ChainId.Ton, "TON", "TON", ChainSupportLevel.Supported, "SLIP-0010 ed25519 · wallet v4R2", "m/44'/607'/0'",
                CanSend: true, CanReceive: true, CanSyncBalance: true, HasHistory: false, CanSwap: false,
                HasTokens: false, Maturity: ChainMaturity.Beta,
                PrivacyNote: "Public ledger; on-chain history is not synced yet."),
            new ChainInfo(
                ChainId.Ada, "ADA", "Cardano", ChainSupportLevel.Supported, "Icarus CIP-1852 · BIP32-Ed25519", "m/1852'/1815'/0'/0/0",
                CanSend: true, CanReceive: true, CanSyncBalance: true, HasHistory: false, CanSwap: false,
                HasTokens: false, Maturity: ChainMaturity.Beta,
                PrivacyNote: "Public ledger; on-chain history is not synced yet."),
            new ChainInfo(
                ChainId.Xmr, "XMR", "Monero", ChainSupportLevel.ReceiveOnly, "Monero ed25519 · restore-from-keys", "umbrella-monero-v1",
                CanSend: true, CanReceive: true, CanSyncBalance: true, HasHistory: true, CanSwap: false,
                HasTokens: false, Maturity: ChainMaturity.Beta,
                PrivacyNote: "Private by default. Send and balance need the bundled Monero service running."),
        ];

        ById = All.ToDictionary(c => c.Id);
    }

    public static IEnumerable<ChainInfo> Supported =>
        All.Where(c => c.Support == ChainSupportLevel.Supported);

    /// <summary>Real derivable address, but no public balance sync (Monero).</summary>
    public static IEnumerable<ChainInfo> ReceiveOnly =>
        All.Where(c => c.Support == ChainSupportLevel.ReceiveOnly);

    public static IEnumerable<ChainInfo> Planned =>
        All.Where(c => c.Support == ChainSupportLevel.Planned);

    /// <summary>Chains that produce a genuine address the user can safely receive to.</summary>
    public static bool HasRealAddress(ChainId id) =>
        ById.TryGetValue(id, out var info) &&
        info.Support is ChainSupportLevel.Supported or ChainSupportLevel.ReceiveOnly;

    public static ChainInfo Get(ChainId id) => ById[id];

    public static bool IsSupported(ChainId id) =>
        ById.TryGetValue(id, out var info) && info.Support == ChainSupportLevel.Supported;
}
