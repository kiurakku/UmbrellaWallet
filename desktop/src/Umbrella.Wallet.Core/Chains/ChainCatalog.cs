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
            // enabled: verify with a small amount before trusting. HasHistory: on — the Activity feed pulls
            // real BCH history over Haskoin (transactions/full) across every issued address, same net-effect
            // parse as BTC/LTC, pinned by ParseHaskoinFull tests. CanSwap: BCH is a swap TARGET via
            // THORChain (in ReceivableTo), but not a source, so this stays false.
            new ChainInfo(
                ChainId.Bch, "BCH", "Bitcoin Cash", ChainSupportLevel.Supported, "BIP44", "m/44'/145'/0'/0/{index}",
                CanSend: true, CanReceive: true, CanSyncBalance: true, HasHistory: true, CanSwap: false,
                HasTokens: false, Maturity: ChainMaturity.Beta,
                PrivacyNote: "Public ledger — use Tor and a fresh address per receive to reduce linking."),
            // Zcash — TRANSPARENT (t-addr) only. A t-addr is a normal P2PKH (BIP44 coinType 133) with
            // Zcash's two-byte version prefix; balance comes from Blockchair. This is the PUBLIC side of
            // Zcash: shielded (z-addr) sending and receiving is a different scheme the wallet does not
            // derive, so the privacy note says so plainly rather than implying Zcash's shielded privacy.
            // Sends a v4 (Sapling) transparent transaction signed with the ZIP-243 digest, pinned to
            // Zcash's own sighash vectors (ZcashSigHashTests), with the ZIP-317 fee. HasHistory stays
            // off until that path is wired and tested.
            new ChainInfo(
                ChainId.Zec, "ZEC", "Zcash", ChainSupportLevel.Supported, "BIP44 · transparent", "m/44'/133'/0'/0/{index}",
                CanSend: true, CanReceive: true, CanSyncBalance: true, HasHistory: false, CanSwap: false,
                HasTokens: false, Maturity: ChainMaturity.Beta,
                PrivacyNote: "Transparent address — this is Zcash's PUBLIC ledger, not a shielded z-address. Use Tor and a fresh address per receive."),
            // XRP Ledger — receive and balance (roadmap N.4). BIP44 coin type 144, secp256k1, the same
            // m/44'/144'/0'/0/0 that Xaman, Ledger and Trust derive, so the phrase restores there. One
            // address, like ETH: every XRP account locks a reserve, so a fresh address per receive
            // would cost real money rather than just privacy. Sends a plain XRP Payment with an optional
            // destination tag, pinned byte-for-byte to xrpl.js (XrpSendTests).
            new ChainInfo(
                ChainId.Xrp, "XRP", "XRP Ledger", ChainSupportLevel.Supported, "BIP44 · secp256k1", "m/44'/144'/0'/0/0",
                CanSend: true, CanReceive: true, CanSyncBalance: true, HasHistory: false, CanSwap: false,
                HasTokens: false, Maturity: ChainMaturity.Beta,
                PrivacyNote: "Public ledger. An XRP address only becomes an account once it has received the network's reserve, which then stays locked while the account exists — a smaller first payment is refused by the network."),
            // Stellar — receive, balance and send (roadmap N.5). SEP-0005: SLIP-0010 ed25519 at
            // m/44'/148'/0', the scheme LOBSTR, Solar and Ledger use, pinned to the SEP's own test vectors.
            // Send: one native Payment (or CreateAccount for an address that is not an account yet), with
            // a text or ID memo and a five-minute window, byte-for-byte what the Stellar Go SDK builds.
            // One account per seed for the same reason as XRP: an account locks a minimum balance.
            new ChainInfo(
                ChainId.Xlm, "XLM", "Stellar", ChainSupportLevel.Supported, "SEP-0005 · ed25519", "m/44'/148'/0'",
                CanSend: true, CanReceive: true, CanSyncBalance: true, HasHistory: false, CanSwap: false,
                HasTokens: false, Maturity: ChainMaturity.Beta,
                PrivacyNote: "Public ledger. A Stellar address only becomes an account once someone funds it with the network's minimum balance, which stays locked while the account exists."),
            // Cosmos Hub — receive, balance and send (roadmap N.6). BIP44 coin type 118, secp256k1, the
            // m/44'/118'/0'/0/0 that Keplr, Leap and Ledger derive, pinned to cosmjs's own wallet test;
            // sends one bank MsgSend, pinned byte-for-byte to cosmjs's signing vectors (CosmosSendTests).
            new ChainInfo(
                ChainId.Atom, "ATOM", "Cosmos Hub", ChainSupportLevel.Supported, "BIP44 · secp256k1", "m/44'/118'/0'/0/0",
                CanSend: true, CanReceive: true, CanSyncBalance: true, HasHistory: false, CanSwap: false,
                HasTokens: false, Maturity: ChainMaturity.Beta,
                PrivacyNote: "Public ledger. The balance shown is available ATOM only — ATOM you have staked with a validator is not included."),
            // NEAR — receive and balance (roadmap N.7). SLIP-0010 ed25519 at m/44'/397'/0', the path
            // near-seed-phrase (and so MyNearWallet, Meteor, Ledger) use; the address is the implicit
            // account — the hex of the public key — so nothing has to be registered to receive.
            new ChainInfo(
                ChainId.Near, "NEAR", "NEAR Protocol", ChainSupportLevel.Supported, "SLIP-0010 · ed25519", "m/44'/397'/0'",
                CanSend: true, CanReceive: true, CanSyncBalance: true, HasHistory: false, CanSwap: false,
                HasTokens: false, Maturity: ChainMaturity.Beta,
                PrivacyNote: "Public ledger. This is your implicit account — the 64-character id is your public key. Named accounts (you.near) and NEAR staked with a pool are not shown."),
            // Polkadot — receive, balance and send (roadmap N.8). substrate-bip39 + sr25519, the scheme of
            // Polkadot.js, Talisman, SubWallet and Nova — NOT BIP-44 — so the phrase shows the same
            // account there. The root key, no derivation path. Balance from Asset Hub and the relay chain;
            // sends Balances.transfer_keep_alive from Asset Hub, built from the running runtime's metadata
            // and validated by the node before broadcast (PolkadotSendTests, PolkadotSendLiveTests).
            new ChainInfo(
                ChainId.Dot, "DOT", "Polkadot", ChainSupportLevel.Supported, "substrate-bip39 · sr25519", "root key (no derivation path)",
                CanSend: true, CanReceive: true, CanSyncBalance: true, HasHistory: false, CanSwap: false,
                HasTokens: false, Maturity: ChainMaturity.Beta,
                PrivacyNote: "Public ledger. The balance adds up DOT on Asset Hub and on the relay chain — Polkadot moved balances to Asset Hub in 2025 — and includes DOT locked for staking or governance, which may not all be spendable."),
            // Nano — receive and balance. SLIP-0010 at m/44'/165'/0' with Nano's ed25519-BLAKE2b public key,
            // the scheme Ledger, Trust Wallet and Nault's BIP39 mode use, pinned stage by stage to the Nano
            // documentation's own test vector (NanoReceiveTests). Sending needs signed blocks with proof of
            // work and stays off until that path is proven; XNO sent here waits as "receivable" until a
            // wallet that signs pockets it, and the balance counts it so the money is never hidden.
            new ChainInfo(
                ChainId.Nano, "XNO", "Nano", ChainSupportLevel.Supported, "SLIP-0010 · ed25519-blake2b", "m/44'/165'/0'",
                CanSend: false, CanReceive: true, CanSyncBalance: true, HasHistory: false, CanSwap: false,
                HasTokens: false, Maturity: ChainMaturity.Beta,
                PrivacyNote: "Public ledger. Incoming XNO stays \"receivable\" until a signed receive block pockets it; this wallet shows it in the balance but cannot sign Nano blocks yet — restore the phrase in Nault or a Ledger to move it."),
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
