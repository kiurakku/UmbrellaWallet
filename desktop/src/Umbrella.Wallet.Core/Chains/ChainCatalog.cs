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
                // Send is a real UTXO spend over BlockCypher (UTXOs, fee, broadcast); the same proven
                // spender signs it as BTC/LTC. Newly enabled — verify with a small amount before trusting.
                CanSend: true, CanReceive: true, CanSyncBalance: true, HasHistory: false, CanSwap: true,
                HasTokens: false, Maturity: ChainMaturity.Beta,
                PrivacyNote: "Public ledger — use Tor and a fresh address per receive to reduce linking."),
            new ChainInfo(
                ChainId.Tron, "TRX", "TRON", ChainSupportLevel.Supported, "BIP44", "m/44'/195'/0'/0/{index}",
                CanSend: true, CanReceive: true, CanSyncBalance: true, HasHistory: true, CanSwap: false,
                HasTokens: true, Maturity: ChainMaturity.Stable,
                PrivacyNote: "Public ledger; a new account burns TRX for energy on its first USDT transfer."),
            new ChainInfo(
                ChainId.Sol, "SOL", "Solana", ChainSupportLevel.Supported, "SLIP-0010 ed25519", "m/44'/501'/0'/{index}'",
                CanSend: true, CanReceive: true, CanSyncBalance: true, HasHistory: true, CanSwap: false,
                HasTokens: true, Maturity: ChainMaturity.Beta,
                PrivacyNote: "Public ledger; history is best-effort via the public RPC (rate-limited)."),
            new ChainInfo(
                ChainId.Ton, "TON", "TON", ChainSupportLevel.Supported, "SLIP-0010 ed25519 · wallet v4R2", "m/44'/607'/0'",
                CanSend: true, CanReceive: true, CanSyncBalance: true, HasHistory: true, CanSwap: false,
                HasTokens: false, Maturity: ChainMaturity.Beta,
                PrivacyNote: "Public ledger; use a fresh flow per counterparty to reduce linking."),
            new ChainInfo(
                ChainId.Ada, "ADA", "Cardano", ChainSupportLevel.Supported, "Icarus CIP-1852 · BIP32-Ed25519", "m/1852'/1815'/0'/0/0",
                CanSend: true, CanReceive: true, CanSyncBalance: true, HasHistory: true, CanSwap: false,
                HasTokens: false, Maturity: ChainMaturity.Beta,
                PrivacyNote: "Public ledger; a reused address links your activity."),
            new ChainInfo(
                ChainId.Xmr, "XMR", "Monero", ChainSupportLevel.ReceiveOnly, "Monero ed25519 · restore-from-keys", "umbrella-monero-v1",
                CanSend: true, CanReceive: true, CanSyncBalance: true, HasHistory: true, CanSwap: false,
                HasTokens: false, Maturity: ChainMaturity.Beta,
                PrivacyNote: "Private by default. Send and balance need the bundled Monero service running."),
            // Bitcoin Cash — a UTXO chain like BTC/LTC/DOGE (BIP44, P2PKH, CashAddr). Send is a real UTXO
            // spend (Haskoin UTXOs/fee/broadcast) signed by the same proven spender as BTC/LTC/DOGE with
            // NBitcoin's SIGHASH_FORKID — the FORKID signature + change path are pinned by tests. Newly
            // enabled: verify with a small amount before trusting. HasHistory stays off until a real BCH
            // history fetcher lands (the Activity feed has no BCH branch yet). CanSwap: BCH is a swap
            // TARGET via THORChain (in ReceivableTo), but not a source, so this stays false.
            new ChainInfo(
                ChainId.Bch, "BCH", "Bitcoin Cash", ChainSupportLevel.Supported, "BIP44", "m/44'/145'/0'/0/{index}",
                CanSend: true, CanReceive: true, CanSyncBalance: true, HasHistory: false, CanSwap: false,
                HasTokens: false, Maturity: ChainMaturity.Beta,
                PrivacyNote: "Public ledger — use Tor and a fresh address per receive to reduce linking."),
            // Zcash — TRANSPARENT (t-addr) receive only. A t-addr is a normal P2PKH (BIP44 coinType 133)
            // with Zcash's two-byte version prefix; balance comes from Blockchair. This is the PUBLIC
            // side of Zcash: shielded (z-addr) receiving is a different scheme the wallet does not derive,
            // so the privacy note says so plainly rather than implying Zcash's shielded privacy. CanSend
            // and HasHistory stay off until their paths are wired and tested.
            new ChainInfo(
                ChainId.Zec, "ZEC", "Zcash", ChainSupportLevel.Supported, "BIP44 · transparent", "m/44'/133'/0'/0/{index}",
                CanSend: false, CanReceive: true, CanSyncBalance: true, HasHistory: false, CanSwap: false,
                HasTokens: false, Maturity: ChainMaturity.Beta,
                PrivacyNote: "Transparent address — this is Zcash's PUBLIC ledger, not a shielded z-address. Use Tor and a fresh address per receive."),
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
