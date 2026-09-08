# 4 · Desktop app (deep dive)

The flagship product. `.NET 8` + Avalonia, Windows + Linux.

## Project structure

Three projects, dependencies pointing downward only:

```
Umbrella.Wallet.Core            pure crypto, no I/O — the part that must be provably correct
  ├── Derivation/               HdAddressDeriver, MoneroKeys, Bip39MnemonicService
  └── Chains/                   ChainId enum, per-chain address formatting
Umbrella.Wallet.Infrastructure  everything that touches the outside world
  ├── Network/                  *TransactionSender, EmbeddedTorService, MoneroRpcService, ExchangeConnectors
  ├── AppPaths.cs               where data is stored (see "Data storage")
  ├── EncryptedFileSeedVault    the vault file
  ├── VaultBackup               encrypted export/restore
  └── ExchangeCredentialStore   encrypted read-only exchange keys
Umbrella.Wallet.App             Avalonia UI
  ├── Views/MainWindow.axaml    the single window, all sections
  ├── ViewModels/MainViewModel  the state machine + all commands
  ├── Theming.cs  Localization.cs  UiSettings.cs
  └── Assets/                   logos, icons, stickers, bundled tor/ and monero/
```

`Core` has no reference to `Infrastructure` or `App`; it can be unit-tested in isolation, and it is —
77 tests pin its output to published BIP/SLIP/Monero test vectors.

## The wallet lifecycle (state machine)

`MainViewModel` is a state machine. The onboarding flags drive which screen shows:

```
IsWelcomeStage → IsCreateStage / IsImportStage → IsBackupStage → IsWorkspace
                                              ↘ IsUnlockStage (returning user) ↗
```

- **Welcome** — no sidebar, an intro on why it beats a standard wallet, Create / Import buttons.
- **Create** — generates a 24-word BIP39 phrase, sets the vault password.
- **Backup** — shows the phrase (screenshot-protected), then confirms a few random words.
- **Import** — accepts a 12- or 24-word phrase from any BIP39 wallet.
- **Unlock** — for a returning user whose vault exists on disk.
- **Workspace** — the real app: Portfolio, Receive, Send, Connect, Market, Activity, Settings.

## The vault (how the seed is protected)

`EncryptedFileSeedVault` + `AppPaths.VaultFile`.

1. Seed: 256-bit entropy from the OS CSPRNG → 24 BIP39 words (`Bip39MnemonicService`).
2. Key derivation: **Argon2id**, m = 64 MiB, t = 4, p = 2, from the user's vault password + a random
   salt.
3. Encryption: **AES-256-GCM** (authenticated), random nonce, with versioned associated data so a
   future format change can't be silently downgraded.
4. Stored as `vault.json` in the data directory. The plaintext seed exists in memory only while
   unlocked and is used only to derive keys locally.

**Reveal-phrase is password-gated**: viewing the phrase in Settings requires re-entering the vault
password; the cleared phrase is wiped when leaving the Settings section.

## Coins and address derivation

`HdAddressDeriver` derives one account per chain from the seed. Paths follow BIP44 conventions:

| Coin | Path | Address format |
|------|------|----------------|
| BTC | `m/84'/0'/0'/0/0` | BIP84 native SegWit (`bc1…`) |
| ETH | `m/44'/60'/0'/0/0` | secp256k1 → keccak → `0x…` |
| LTC | `m/84'/2'/0'/0/0` | BIP84 (`ltc1…`) |
| DOGE | `m/44'/3'/0'/0/0` | base58 (`D…`) |
| TRON | `m/44'/195'/0'/0/0` | secp256k1 → keccak → base58check (`T…`) |
| SOL | `m/44'/501'/0'/0'` | SLIP-0010 ed25519 → base58 |
| TON | `m/44'/607'/0'` | SLIP-0010 ed25519 → wallet v4R2 (`UQ…`) |
| ADA | `m/1852'/1815'/0'/0/0` | Icarus CIP-1852 (BIP32-Ed25519) → Shelley base (`addr1…`) |
| XMR | custom (see below) | Monero base58, 95 chars |

Every derivation is verified in tests against the canonical test mnemonic's known addresses; the TON
v4R2 and Cardano CIP-1852 pipelines are pinned byte-for-byte against the reference `@ton` /
cardano-serialization-lib vectors, so the same phrase recovers the funds in those wallets. What each
chain can actually *do* — receive, balance, send, history, swap, tokens, maturity — is declared once
in `ChainCatalog` / `ChainInfo` (roadmap §5.1), so "derives an address" is never confused with
"everything works" (that conflation is what let the app briefly offer sends it couldn't make).

### Monero keys (`MoneroKeys.cs`)

Monero doesn't fit the BIP44 mould. Umbrella derives it deterministically from the wallet seed:

- `spendKey = ScReduce32(Keccak256("umbrella-monero-v1" ‖ seed))` — reduced mod the ed25519 group
  order ℓ.
- `viewKey  = ScReduce32(Keccak256(spendKey))`.
- Public keys via ed25519 basepoint scalar-mult (reached through BouncyCastle reflection; the code
  throws at type-init if that method ever disappears, so an upgrade fails loudly instead of silently
  producing wrong keys).
- Address = `base58(prefix ‖ publicSpend ‖ publicView ‖ keccak[..4])`.

This was verified three ways: published Keccak/basepoint vectors, decoding the Monero project's own
donation address, and — decisively — feeding the derived keys to the real `monero-wallet-rpc`, which
reproduced the exact same address.

## Sending (how a transaction is built)

Each chain has its own sender in `Infrastructure/Network/`. The pattern is always: **fetch inputs →
build → sign locally → verify → broadcast signed bytes only.**

| Sender | Mechanism |
|--------|-----------|
| `BitcoinTransactionSender` | Esplora UTXOs + NBitcoin; fee from `/fee-estimates` (~6-block target); `builder.Verify()` must pass |
| `EthTransactionSender` | `eth_gasPrice` × 1.05, 21000 gas, EIP-155 chainId 1, `eth_sendRawTransaction` |
| `SolanaTransactionSender` | Hand-serialized legacy message, ed25519 sign, `sendTransaction` |
| `TronTransactionSender` | TronGrid builds the unsigned tx; we sign its txID with secp256k1; TRX and **USDT (TRC-20 contract call)** |
| `MoneroRpcService` | `transfer` RPC to the local daemon (RingCT handled by Monero itself) |

A test proves each signing key belongs to the exact address the wallet displays — so a send can
never draw on an account the user never funded.

> **Fees:** see [07-financial.md](07-financial.md). Short version: you pay the network's fee, not
> ours. TRON USDT is the expensive outlier.

## Bundled Tor (`EmbeddedTorService`)

- `tor.exe` (or `tor` on Linux) ships in `Assets/tor/`, launched as a child process.
- SOCKS5 on a **private port 9250** so it never collides with a Tor Browser the user runs.
- Writes its own `torrc`, parses `Bootstrapped N%` from stdout, waits up to **240 s** (a cold start
  with no cached consensus takes ~75 s; the old 90 s budget failed intermittently).
- Working directory is the writable data dir, not the install folder (which is read-only under
  Program Files). When on, `PublicHttp.ActiveProxy` routes wallet HTTP and the Monero daemon through
  it.
- Killed on app close.

## Bundled Monero (`MoneroRpcService`)

- `monero-wallet-rpc` ships in `Assets/monero/`, drives XMR as a full coin.
- JSON-RPC on port 18099; restores the wallet from Umbrella's derived keys via `generate_from_keys`.
- Balance only exists after scanning with the view key (`get_balance` + sync height), so the UI can
  say "still syncing" instead of a wrong 0.
- Readiness wait is 90 s (a 39 MB unsigned binary can spend that long in an antivirus scan on first
  run); on failure it reports the daemon's own last log lines.
- Local RPC uses an HttpClient with `UseProxy = false` — the daemon is on loopback and must not be
  sent through the system proxy or Tor.

## Screenshot protection

`MainWindow.axaml.cs` calls `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` on Windows whenever a
secret is on screen (seed backup, revealed phrase, or Monero keys). The window renders **black** to
screen-capture and remote-viewing software. Guarded by `OperatingSystem.IsWindows()` so Linux builds
compile and simply skip it.

## Themes, languages, layout

- **`Theming.cs`** — 10 palettes as `DynamicResource` brushes; `Apply(id)` repaints live. The QR
  plate and danger-reds are deliberately never themed (a tinted QR won't scan; red must stay red).
- **`Localization.cs`** — an indexer-bound `Loc` singleton; raising a null-name PropertyChanged
  re-reads every bound string, so language switches with no restart. Missing keys fall back to
  English.
- **`UiSettings.cs`** — theme, language and sidebar position (left/right/top/bottom) persisted to
  `ui-settings.json`, applied before first paint so nothing flashes.

## Data storage (`AppPaths.cs`)

Everything the app writes lives in **one `data/` folder beside the executable**, so it follows the
install drive instead of filling the system drive (Monero's scan cache alone runs to hundreds of MB):

```
<app dir>/data/
├── vault.json              encrypted seed
├── watch-addresses.json    linked watch-only addresses
├── exchanges.bin           encrypted read-only exchange keys
├── ui-settings.json        theme / language / layout
├── tor/                    Tor data dir (consensus cache, torrc)
└── monero/                 Monero wallet files + scan cache
```

Resolution order: `UMBRELLA_DATA_DIR` env var → `<app dir>/data` (if writable) → `%APPDATA%`
fallback (for read-only Program Files installs). `MigrateLegacyData()` moves anything an old version
wrote under `%APPDATA%`/`%LOCALAPPDATA%` into the new location on first launch, never overwriting.

## Backups (`VaultBackup`)

- **Export** writes the vault **still encrypted** plus the non-secret side files — the backup file is
  exactly as safe as the vault and useless without the password.
- **Restore** verifies the bundle before touching disk and moves the existing vault aside as a
  `.replaced-<timestamp>` file rather than deleting it, so a mistaken restore is reversible.
- Because it carries the *encrypted* vault, a backup can only be restored with the password that made
  it — there is no recovery path, by design.

## Read-only exchange links

`ExchangeConnectors` + `ExchangeCredentialStore`. Nine venues (Binance, Bybit, OKX, Kraken, KuCoin,
Gate.io, MEXC, Bitget, Telegram CryptoBot). Each connector calls only the venue's *balance* endpoint
with its own signing scheme. Keys are verified before storage, encrypted at rest with a key derived
from the seed, and never used for trading or withdrawal — **there is no such code**.

