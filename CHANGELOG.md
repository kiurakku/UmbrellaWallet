# Changelog

All notable releases of **Umbrella Wallet**.

Format follows [Keep a Changelog](https://keepachangelog.com/). Versioning follows [SemVer](https://semver.org/).

## [4.0.2] — 2026-08-15 (test build)

- **Connect removed from the menu (#5)** — the quick-action tile is now Swap; watch-address markup
  stays in code but is no longer in navigation.
- **More Ukrainian/RU/ZH/ES/DE coverage (#15/#2)** — Receive / Send / Buy / NFT / Staking descriptions
  now follow your language instead of staying English.
- **Toasts in more places (#8)** — opening an external venue/link now shows a top-centre toast.
- 147/147 tests.

## [4.0.1] — 2026-08-15 (test build)

- **Coin logos** — brand-coloured coin badges now show on **Market** and **Receive** rows too (matching
  Holdings), so every coin is recognisable at a glance. No external icon assets bundled.
- **Developer fee → your dedicated TRON/USDT wallet** (`TNvxWSh…`). Note: TRON send-path fee routing
  isn't enabled yet, so nothing is deducted on TRON until that's implemented and tested on-chain —
  the address is in place for when it is.
- 147/147 tests.

## [4.0.0] — 2026-08-15

### On-chain transaction history (new)
- The Transactions section now shows **real on-chain history for your own addresses** — including
  transactions made **before** you ever opened the wallet. Covers **TRX (native + USDT/TRC-20),
  Bitcoin, Ethereum and Litecoin**, via keyless public explorers, routed through Tor / your proxy
  like everything else. Merged with local activity and de-duplicated.

### Richer token pages
- Opening a coin shows a **24h High / Low / Volume** row (from the same Binance feed).
- **Optional market-data connector (CoinGecko)** — **off by default** so the privacy-first wallet
  contacts no third party unless you enable it (Settings → Privacy). When on, token pages also show
  **Market Cap, FDV and Volume**.

### Wallets
- **Colour-tag your wallets** — a colour ring on each wallet's badge + a swatch picker
  (Settings → Wallets), persisted.

### Stickers (yours) — all toggle with the other animations
- Welcome: greeting sticker beside the umbrella, **GhostPepe** (Anonymous), **encryption** (Your keys).
- **67** in NFT, **Recieve** on Receive, **Sending** on the send animation, **up/down** on the market chart.

### Security & privacy
- **Lock on minimize**, **hide balances by default**, custom SOCKS5 proxy, IPv4/IPv6 control,
  clipboard auto-clear (Settings → Security / Privacy). Re-verified vault (Argon2id + AES-256-GCM),
  screenshot blackout, KDF-parameter guard.

### Fixes
- Nav bar reappears on Top/Bottom layouts (regression) · green chart no longer draws a red line ·
  unlock screen vertically centred · quick-action tiles no longer clip · Activity events are separate
  cards · shorter top-centre toasts, now shown across all sections · new Telegram news logo ·
  removed the umbrella-logo glow.

### Still ahead (honest — each its own piece of work)
Full in-page swap widget + swap between **all** pairs (needs an aggregator), per-wallet single-coin
support, Telegram-gift NFTs (TON NFT API), SOL/DOGE history, real per-coin logos, and the
developer-fee routing decision. Being done deliberately, with your testing, not a blind mass rewrite.

### Verified
146/146 tests pass; smoke-launches clean (desktop + mobile). Updates never touch your data.

## [3.5.1] — 2026-08-15

### Market / token detail (toward the Uniswap token page)
- Opening a coin now shows a **24h stats row — High · Low · Volume** — beneath the chart, pulled
  from the same Binance feed the prices use (no new data source, no extra tracking, same Tor/proxy
  route). Converted to your display currency; volume shown compact (e.g. 4.6B).

> Market cap / FDV / TVL / 52-week range and an in-page swap widget need an external market-data
> service (e.g. CoinGecko/DeFiLlama) — that will land as an **optional, off-by-default** connector so
> the privacy-first default never calls a third party you didn't enable.

### Verified
139/139 tests pass; smoke-launch clean.

## [3.5.0] — 2026-08-15

### Mobile layout (toward a Uniswap-style phone UI)
- **Floating pill bottom nav** with five fixed slots (Portfolio · Receive · Send · Market · More) —
  no more horizontal scrolling. Overflow sections open in a **"More" bottom sheet**.
- The 330px side rail is **hidden on the phone layout**, so the content is a clean single column
  instead of a cramped, crooked split.

### UI fixes (from screenshots)
- **the fear logo** now sits beside the umbrella on the welcome screen (replacing the sticker).
- **Quick-action tiles no longer clip** long labels — icon + a bounded, ellipsised text column.
- **Activity events are separate rounded cards** with gaps, not rows crammed into one field.
- **Toasts are shorter** (≈2.2s notices / 3.5s errors), still top-centre.

### Security
- **Hide balances by default** (Settings → Security): every unlock starts with amounts hidden.

### P2P & DEX
- Added **SushiSwap** and **Raydium** (Solana) — both non-custodial. (We deliberately don't list
  sites we can't verify as safe/non-custodial.)

### Coin support (audit)
- Sending is wired for BTC, ETH (+BNB/Polygon/Avalanche/Fantom/Cronos & L2s), LTC, SOL, XMR, TRON,
  USDT-TRC20 and ADA, plus ERC-20/TRC-20 tokens; TON sending for imported TON wallets. DOGE and a
  few others are receive/balance-only for now.

### Verified
139/139 tests pass; smoke-launches clean in desktop and mobile layouts.

## [3.4.2] — 2026-08-15

### Desktop
- **Real phone-style mobile layout.** Mobile mode now shows a proper bottom **icon tab bar**
  (icons + labels, horizontally scrollable so every section stays reachable) instead of a narrowed
  desktop menu, and the quick actions wrap to 2×2. A soft themed **glow** now sits behind the
  the-fear logo on the welcome screen.
- **Clearer backup.** The recovery phrase and the (optional) Monero keys are now one card with a
  plain-language intro explaining that the 24-word phrase is the real backup and the Monero keys are
  an advanced extra most people never need.
- **Fixed the Activity spam.** The feed no longer records a "Sync · Public RPC · OK" row every 60
  seconds; the live status line already shows the last-updated time.

### Security & privacy
- **Lock on minimize** (Settings → Privacy): the vault locks the instant the window is minimized.
- Builds on 3.3's custom SOCKS5 proxy, IPv4/IPv6 control and clipboard auto-clear.

### P2P & DEX
- **More vetted non-custodial venues:** CoW Swap (MEV-protected), Matcha, Curve, Osmosis, plus
  Haveno (Monero P2P), Vexl (no-KYC BTC P2P) and LocalCoinSwap.

### Localization
- The welcome screen (buttons, trust cards, blurb) and several section headers now follow your
  language in all six locales. (Translation coverage continues to expand each release.)

### Verified
139/139 tests pass; app smoke-launches clean in both desktop and mobile layouts.

## [3.4.0] — 2026-08-14

### Desktop
- **Mobile layout on your PC.** Settings → Appearance → *Mobile layout*: the wallet renders as a
  phone-style app — a narrow centred column, a bottom tab bar, and a phone-sized window (430×900) —
  and switches straight back to the wide desktop layout when you turn it off. Your saved menu
  position is preserved across the switch (mobile mode force-docks the nav to the bottom without
  overwriting your preference).
- **Responsive quick actions.** The dashboard's action tiles now wrap to a 2×2 grid in the mobile
  layout instead of clipping four across, and "Prices & charts" is now localized in all six languages.

### Verified
139/139 tests pass; app smoke-launches clean. Anonymity/security model unchanged; updates preserve data.

## [3.3.0] — 2026-08-14

### Security & privacy
- **Security review + vault hardening.** A full pass over the crypto/storage layer. `UnlockAsync`
  now rejects out-of-range Argon2 parameters (memory 8 MiB–1 GiB, t 1–64, p 1–16) up front, so a
  tampered or foreign vault file can no longer stall/OOM the app on unlock. Re-verified: on-device
  Argon2id + AES-256-GCM vault, sign-then-`ZeroMemory` key handling, `SetWindowDisplayAffinity`
  screenshot/screen-share blackout while a seed or Monero key is on screen, and the `https://`-only
  external-link guard.
- **Custom SOCKS5 proxy.** Settings → Privacy routes every request through your own proxy (VPN,
  SSH tunnel, another Tor) instead of the bundled Tor. `host:port` or `socks5://host:port`; Tor and
  the custom proxy are mutually exclusive.
- **IPv4 / IPv6 control.** Force outbound (direct) connections onto one family, or leave it automatic.
- **Clipboard auto-clear.** Copied addresses are wiped from the clipboard after a chosen delay
  (off / 30s / 45s / 1m / 2m), and only if the clipboard still holds what the app put there.

### Localization
- Onboarding (create / import / unlock / restore / back-up), section titles and the holdings/market
  column headers are now localized across all six languages instead of hardcoded English.

### Installer
- Setup shows the licence, carries publisher/version metadata and support/update URLs, and on
  uninstall tells the user where their encrypted data is kept (and that it's deliberately preserved).

### Verified
139/139 tests pass (added a KDF-parameter-rejection test). Anonymity/security model unchanged;
updates still preserve all wallet data.

## [3.2.0] — 2026-08-09

### Desktop
- **Lock-screen background is yours.** Settings → Appearance → Lock screen: pick your own image for the
  unlock screen, revert to the bundled default, or turn it off entirely for a flat, plain lock screen.
- **Market opens instantly.** Prices are cached on this device and shown the moment the Market opens,
  instead of populating dash-by-dash over a few seconds; the live fetch then updates them in the
  background. (Same device-only cache approach as the portfolio totals.)

### Verified
138/138 tests pass. Anonymity/security model unchanged; updates still preserve all wallet data.

## [3.1.0] — 2026-08-09

### Desktop
- **Developer fee → your Solana address.** The 0.5% send fee (disclosed in the review, capped at 2%)
  now routes to `ABX24FdKZb6nyW6eiQ3bE5TdZUPdypG9P23AZeutRXL5` on SOL. Recipient addresses for
  TRON/USDT, Ethereum and TON are stored, but their send paths don't route a fee **yet** — no fee is
  taken on those chains until per-chain routing ships (a fund-critical change, coming next).
- **Fresh installs speak your language.** After a delete + re-download (or first run), the app now
  defaults to your **OS language** if it's translated (UK/RU/ZH/ES/DE), instead of always English.
- **the fear logo — with its blue background** again (as requested).
- **+13 display currencies** — CAD, AUD, CHF, BRL, KRW, MXN, ZAR, SEK, NOK, AED, SGD, HKD, KZT
  (23 total), all live via open.er-api.com.

### Verified
138/138 tests pass (incl. the updated developer-fee vector). Anonymity/security unchanged.

## [3.0.4] — 2026-08-09

### Desktop
- **Update-safety hardened.** The installer now explicitly excludes any `data` folder, so a new version
  can never overwrite your wallets, theme, linked addresses or history. (They already lived in a
  separate data folder — `%APPDATA%\UmbrellaWallet` for installs, `data/` beside the exe for portable —
  untouched by updates; this is a belt-and-braces guarantee.)
- **Brand logos finalized** from the supplied artwork in `docs/assets` (the fear mark + Telegram-channel
  icon), blue background removed.
- README refreshed: new logo, corrected theme count (27), ambient-motion options, and an explicit
  "updates keep your data" note.

## [3.0.3] — 2026-08-09

### Desktop
- **Much faster balances.** Account balances are now fetched **concurrently** instead of one after
  another, and the portfolio total shows right after the (fast) native pass — no more long wait before
  the sum appears on unlock or wallet-switch.
- **Instant totals (no $0 flash).** Balances are cached per wallet on this device, so switching or
  unlocking shows your last-known total immediately while the live refresh updates it in the background.
- **Centered notifications.** Errors and important notices now pop as a **toast at the top-center**
  (auto-hides), instead of only landing in the status bar.
- **Real brand logos.** The **the fear** mark and the **Telegram channel** icon now use the supplied
  artwork with the blue background removed (transparent).

### Verified
Anonymity/security unchanged (no accounts, no telemetry, keys local + encrypted, Tor optional). 138/138
tests pass.

## [3.0.2] — 2026-08-09

### Desktop
- **Settings fully translated.** The **Wallets** tab and the **Maintenance / Danger zone** cards were
  hardcoded English regardless of the chosen language — now every label, hint and button is localized
  (Ukrainian + others).
- **Clearer, more honest wording.** "Delete vault" → **"Erase from this PC"**, and the danger-zone text
  now spells out that this does **not** delete your on-chain wallet (a wallet lives on its recovery
  phrase and can't be deleted) — it only wipes this device's local copy, which your phrase restores.
- **Wallet switching from anywhere.** When the nav sits at the top or bottom, a **wallet switcher chip**
  (shows the active wallet, one tap to switch) now lives in the bar — no more digging into Settings.
- **Distinct theme names** — no two themes share a name/colour label anymore (27 themes).
- **New "Aurora glow" ambient animation** (opt-in) — two soft, slowly drifting glows behind the content,
  toggled independently of the rain and stickers in Settings → Appearance.

### Verified
Anonymity/security model unchanged: no accounts, no telemetry, keys encrypted locally (Argon2id +
AES-256-GCM), Tor optional, app password held only in memory. 138/138 tests pass.

## [3.0.1] — 2026-08-09

### Project
- **Desktop-only from here.** The web frontend and NestJS backend were removed from the repository —
  development is focused entirely on the native desktop apps (Windows & Linux now, **Android planned**).
  Your wallet was always self-custody and local-first, so nothing about it changes.
- **Official Telegram channel.** Added an in-app News card and link to **t.me/UmbrellaWallet** — the one
  official place for news, releases and contact. A News notice explains the web pause.
- README, docs index and CI updated for the desktop-only layout; added a GitHub Actions release workflow
  that builds the Windows installer/portable and the Linux tarball on their native runners.

## [3.0.0] — 2026-08-09

### Desktop
- **Import your Telegram / TON wallet.** Umbrella now understands the TON-native 24-word standard used
  by **Telegram Wallet, Tonkeeper and TON Space** — paste that phrase and it imports as a Toncoin wallet,
  showing the exact same TON address (and balance) those wallets display. The whole derivation
  (mnemonic → ed25519 seed → wallet v4R2 address) is pinned byte-for-byte against `@ton/crypto` +
  `@ton/ton`. You can receive and send TON from it.
- **Full Ukrainian (and RU/ZH) navigation.** Every menu item is now translated — Buy, Swap, P2P & DEX,
  NFTs, Staking and Transactions no longer sit in English next to the translated ones.
- **Individual animation toggles.** Turn the ambient rain and the animated stickers on or off
  independently in Settings → Appearance (under the master motion switch).
- **Six more themes** — Solana, Ethereum, Monero, Kraken, Nord and Dracula — bringing the total to 27.

### Verified
138/138 tests pass, including the TON reference vector, all BIP39/TON validation, every chain's address
derivation and signing, developer fees, and the multi-wallet / password flows.

## [2.8.8] — 2026-08-08

### Desktop
- **Fixed: couldn't create or import *any* wallet.** "Add wallet" locked the current wallet (wiping the
  app password) and then the create/import screen cleared the pre-filled password — leaving the password
  fields hidden with a "enter a vault password" error and no way forward, which blocked creating or
  importing anything. The app password is now retained across the add-lock and the create/import flow
  reuses it directly, so it can't be cleared out from under you. If the fields ever have no password to
  reuse, they simply show again.
- **Self-healing on startup.** If an add-wallet was interrupted before its seed was written, the app now
  switches to a wallet that actually has a vault (so you're never stranded on onboarding) and clears the
  leftover empty wallet.
- **Change password (Settings → Wallets).** Set a new app password; every wallet on your current password
  is re-encrypted so one password keeps unlocking them all.
- **Forgot your password?** The unlock screen now has a recovery path: enter the wallet's recovery phrase
  and a new password to restore access — the same phrase restores the same addresses and funds.
- Note: a truly password-less vault isn't offered, because it would leave your seed effectively
  unencrypted on disk. Use a simple password you write down, plus the new recovery path, instead.

## [2.8.7] — 2026-08-08

### Desktop
- **Import is far more forgiving, and tells you what's wrong.** A phrase from any BIP39 wallet — Kraken
  Wallet, MetaMask, Trust, Ledger, Exodus, Coinbase Wallet, etc. — now imports even if it was pasted
  with numbering ("1. word 2. word"), commas or line breaks (the words are extracted cleanly). If a
  single word is mistyped, the error names that exact word instead of a blanket "invalid". A short note
  on the import screen lists which wallets are compatible.
- **Clearer non-BIP39 message.** A valid-looking phrase that fails the checksum now explains it's likely
  from a non-BIP39 wallet (Telegram Wallet / Tonkeeper on TON), which can't be imported here — your
  funds stay safe in that wallet. (Native TON-wallet import is still planned separately.)

## [2.8.6] — 2026-08-08

### Desktop
- **Rain animation, properly fixed.** The previous pass still pulsed: it used a shared start-delay plus
  repeating durations, so the drops re-synchronised on a cycle and showed a recurring "wave" the longer
  it ran. Every drop now has a unique, non-repeating fall duration and a unique start height with no
  delay, so they scatter immediately and never line back up — steady, subtle rain at any runtime.

## [2.8.5] — 2026-08-08

### Desktop
- **One login password for every wallet.** Adding a wallet no longer asks for a separate password —
  it reuses your app password, and switching between wallets unlocks instantly instead of re-prompting.
  (If a wallet happens to use a different password, it still asks.) The password is held only in memory
  while unlocked and wiped on lock, same as the seed.
- **Clearer "invalid phrase" for TON/Telegram wallets.** Importing a recovery phrase from Telegram
  Wallet / Tonkeeper (TON) now explains *why* it's rejected: those wallets use the same wordlist but the
  TON mnemonic standard, which isn't BIP39 — so it can't be imported here, and your funds stay safe in
  that wallet. (Full TON-wallet import is planned as a separate, test-pinned feature.)
- **Fixed the rain animation.** The ambient streaks used to fall in three synchronised rows (they all
  shared one animation phase). They now have varied speeds and per-drop delays, so it reads as real rain,
  and travels the full height of a maximised window. Kept deliberately subtle.
- **Settings search.** A search box at the top of Settings finds any option by name or keyword and jumps
  straight to its pane.

## [2.8.4] — 2026-08-08

### Desktop
- **Multiple wallets (Binance-style).** Keep several independent wallets on this device and switch
  between them from **Settings → Wallets** (or the new **⇄ Wallets** button in the sidebar). Each wallet
  is its own encrypted seed with its own password; switching locks the current one and asks for the
  other's password. Add a new wallet (create or import), rename the active one, or remove another —
  the active wallet and the original "Main" seed file are protected from deletion.
  - Safety: the pre-existing wallet is always preserved and auto-registered as "Main"; a corrupt index
    can never lock you out (it falls back to the Main vault); a full data-wipe now also clears the
    wallet index, every additional vault and the activity log.
  - This ships **Stage 1** (independent wallets). Sub-accounts *within* one seed (one phrase, Account
    1/2/3 like MetaMask) are the next stage.

## [2.8.3] — 2026-08-08

### Desktop
- **Fixed: sidebar overflow.** On a shorter window the nav list ran into the footer, so *Settings*
  overlapped "Keys encrypted on this PC" and the Lock button. The nav list now scrolls inside its own
  area, and the status footer + Lock vault button stay pinned at the bottom at any window height.
- **New Buy section (fiat on-ramps).** Top up with a card or bank transfer via regulated on-ramps that
  deliver straight to your own address — **Onramper** (aggregator), MoonPay, Ramp, Transak, Banxa,
  Mercuryo and Guardarian. A 3-step "how it works" and one-tap copy of your receive address; Umbrella
  holds nothing and takes no fee.
- **NFTs — clearer provenance.** Added a "Where these come from" note (read-only from the Ethereum
  chain via a public explorer against your own 0x address; more chains on the roadmap).

## [2.8.1] — 2026-08-08

### Desktop
- **Responsive chart — nothing gets clipped.** The Market detail chart now scales to fit whatever
  width the window gives it (wrapped in a Viewbox), so on a narrow or restored-down window it shrinks
  to fit instead of having its price axis and right edge cut off, and it grows cleanly on a maximised
  one. The hover crosshair stays pixel-accurate at any scale.

## [2.8.0] — 2026-08-08

### Desktop
- **Pro-grade charts.** The Market detail chart now has an interactive **hover crosshair** with a
  floating price + time readout, a **Line ⇄ Candles** toggle, a soft **gradient area fill** under the
  line (fading in the up/down colour, Kraken/TradingView-style) and a **change-over-window badge**
  (first→last, e.g. `▲ 4.21% · 7D`).
- **New P2P & DEX section.** A curated directory of **non-custodial** ways to trade — the in-wallet
  THORChain swap up top, then on-chain DEXes (Uniswap, THORSwap, Jupiter, 1inch, PancakeSwap) and
  peer-to-peer escrow venues (Bisq, Hodl Hodl, RoboSats, Peach). Each shows its custody model and opens
  in your own browser; no custodial exchanges are listed, and Umbrella takes no fee and holds nothing.
- **Market search.** A live filter box matches by ticker or name, plus a one-tap refresh — the coin
  list stays fully live while you type.

## [2.7.0] — 2026-08-01

### Desktop
- **Watch-only linking now auto-detects the network** from the address you paste (a `T…` address is
  TRON, `bc1…` is Bitcoin, `0x…` is EVM, and so on), so a linked address is always tracked on the
  right chain instead of whatever the dropdown happened to show. (Connect tracks an *external* address
  read-only; it never changes your own receive addresses.)
- **Activity & transaction history now persist** across restarts (stored on this device only, never a
  server) with real timestamps — they no longer vanish when you close the wallet, and the Transactions
  section keeps your sends/swaps.
- **Danger zone expanded** beyond delete-wallet: *Clear history* (wipe the local activity/transaction
  log) and *Disconnect all* (remove every linked watch address and exchange) — both keep the vault and
  funds intact.
- **Eight branded themes**, each with real brand colours: Uniswap (exact `#FF007A` pink), Binance
  (gold), Bybit (amber), OKX (mono black/white), Telegram (blue), TON · Gram (blue), TRON (red),
  WhiteBit (green) and Bitcoin (orange) — 21 themes total.
- LICENSE and README updated: independent/experimental self-custody framing, a trademark &
  non-affiliation clause (the branded themes and coin names imply no endorsement), and a
  not-a-regulated-service / no-advice clause. Author: **the fear**.

## [2.6.0] — 2026-08-01

### Desktop
- **Display currency.** Settings → Appearance lets you show balances and prices in USD, EUR, UAH, RUB,
  GBP, CNY, JPY, PLN, TRY or INR — the total, holdings, breakdown and market all convert (USD→currency
  rate via a keyless API, routed through Tor when on). Coins are unchanged; only how their value reads.
- **Transactions section + Activity filters.** A dedicated Transactions view lists money movements
  (sends, receives, swaps) with a copy-explorer-link on each; the Activity feed gains a filter
  (All / Transactions / Connections / Settings / System).
- **Market fixed + fuller.** Prices now come from Binance first (CoinGecko's free tier was rate-limiting
  and blanking every row), with CoinGecko filling only what Binance lacks — so BNB/MATIC/AVAX/FTM/LINK/
  UNI/XRP/DOT/BCH/USDC all price and chart. Market-only coins (XRP/DOT/BCH) now say so honestly instead
  of claiming a wallet address.
- **Two more themes** — Uniswap (magenta-pink) and Ocean (teal) — bringing the palette to 13.

## [2.5.0] — 2026-08-01

### Coins — sending on every EVM chain
- **Send native BNB, MATIC, AVAX, FTM and CRO**, not just Ethereum. The wallet already showed these
  balances (same 0x address); now it signs and broadcasts their transfers too, reusing the exact
  EIP-155 signer that is pinned byte-for-byte to the official test vector — only the chain id, RPC and
  explorer differ. Nonce, gas price and the balance check come from each chain's public RPCs (with
  fallbacks), and everything routes through the bundled Tor when it is on. So a MetaMask-imported
  wallet can now spend across Ethereum, BSC, Polygon, Avalanche, Fantom and Cronos from one place.
- The full 24h portfolio change now shows in the overview ring; Recent Activity, Market (12 more
  popular coins), News and the Guide were all filled in.

## [2.4.0] — 2026-08-01

### Desktop
- **All tokens now show, not just USDT.** Every TRC-20 token on TRON and every ERC-20 token on
  Ethereum (via Koios-style public APIs / Blockscout, no keys) appears in Holdings — reward tokens,
  other stablecoins, any token — which is what most "my balance is missing" reports actually were.
  Unpriced tokens show their real amount at $0 rather than an invented price.
- **Many more coins.** Every major EVM network at the same 0x address as Ethereum — native BNB (BSC),
  MATIC (Polygon), AVAX (Avalanche), FTM (Fantom), CRO (Cronos), plus ETH on the Arbitrum, Optimism
  and Base L2s — queried in parallel. Combined with the automatic ERC-20 / TRC-20 token display, a
  MetaMask-imported wallet now shows essentially everything it holds.
- **Portfolio-overview ring breakdown.** The right-rail ring now shows what the balance is made of:
  a proportional bar plus a per-asset legend (symbol · share · value), top assets with the rest
  folded into "Other".
- **Hide-balance now hides everything.** Masking the balance blanks every money figure — the overview
  ring, the breakdown values and each Holdings row's amount + value — not just the top total (the
  public market price stays visible).
- **NFTs** — ERC-721 / ERC-1155 collections at your Ethereum address are listed (names + counts;
  no images are fetched, so it never leaks your IP).
- **Staking** — the stakeable coins you hold keys for, with each network's typical (approximate)
  reward and how staking is done.
- The Windows app now ships as **`Umbrella.exe`**.

### Coins — Cardano (ADA) sending
- **Real ADA sending.** Cardano payment transactions are now built, signed and broadcast on-device:
  the CBOR transaction body, the BIP32-Ed25519 signature (extended-key ed25519, implemented from the
  group operations) and the assembled signed transaction are all pinned **byte-for-byte** against
  Emurgo's cardano-serialization-lib for a fixed key and transaction, so a single wrong byte fails the
  tests before any ADA can move. UTXOs, the chain tip (TTL) and submission go through Koios; fee and
  change are computed from the real signed-tx size, change returns to the sender. ADA is now fully
  supported (receive + balance + send) — Monero alone remains receive-only.
- Fixed a latent guard that blocked confirming TRON / USDT (TRC-20) sends.

## [2.3.0] — 2026-07-31

### Desktop
- **In-wallet swaps (THORChain).** New Swap section for decentralised, non-custodial cross-chain
  swaps — no account, no API key, no KYC. The source coin is sent to a THORChain inbound vault with a
  signed OP_RETURN memo, and the network delivers the target coin to the wallet's own receive address;
  funds are never held by a third party. Pay from BTC/LTC, receive BTC/ETH/LTC/DOGE. Live quote (rate,
  fee, slippage, ETA, expiry) reviewed before sending, with a fresh re-quote and a rate-moved guard at
  confirm time. Quote parser pinned to real THORChain responses; the OP_RETURN memo path is tested.

## [2.2.3] — 2026-07-31

### Security (desktop — fund-critical)
- **TON address checksum now verified.** `ParseFriendlyAddress` decoded the 36-byte address but
  ignored its trailing CRC-16 — a mistyped recipient that still decoded with a valid tag would have
  been accepted and funds sent to the wrong account. The checksum is now enforced (mistyped/corrupted
  addresses are rejected before a send). Regression-tested.
- **BoC parser hardened.** `TonCell.FromBoc` now bounds-checks every read and validates all counts and
  ref/root indices, so malformed or truncated cell bytes fail with a clean error instead of an
  IndexOutOfRange / overflow / out-of-memory crash. Covered by a new deterministic fuzz harness
  (120k random + mutated inputs across the address, BoC and base58 parsers).

### Security (tooling — whole repo)
- Fixed a high-severity web dependency advisory (postcss path-traversal, GHSA-r28c-9q8g-f849) and
  pinned patched versions of two vulnerable test-only .NET transitives.
- Added CI security gates: CodeQL SAST (C# + JS/TS), Dependabot (npm + NuGet + Actions), secret
  scanning (gitleaks), PR dependency review, and enforced npm-audit / NuGet-vulnerable gates.

## [2.2.2] — 2026-07-31

### Desktop
- Full wallet + data deletion (Danger zone); Tor/Monero bundles preserved.
- Settings text overflow fix; localized delete confirmation keyword.

## [2.2.1] — 2026-07-31

### Desktop
- Delete-vault keyword localized; settings layout overflow fixed.
- README header refresh.

## [2.2.0] — 2026-07-31

A large desktop pass over 2.1.x, consolidated.

### Desktop
- **Real TON sending** — wallet v4R2 transfers, pinned byte-for-byte against `@ton/ton`.
- **Live candlestick market** — real OHLC candles from Binance klines, timeframes 1H–1Y; prices
  and 24h change from CoinGecko. No mock data.
- **Editorial monochrome redesign** — cinematic hero, right-hand dashboard rail (overview ring,
  activity, market), floating price-ticker dock, coin-symbol badges, glassy hover motion + parallax.
- **Profile customization** — name, avatar, banner, and sidebar / lock-screen backgrounds, all
  picked from your own image files (fixed: file dialogs were never wired, so uploads and backup were
  silently dead).
- **NFTs / Staking** sections (in-development pages); configurable auto-lock; **11 themes** whose
  accent now drives the buttons; a localized guide (full Ukrainian) and a detailed
  Cybersecurity / Danger-zone settings pass.
- **Linux** build from the same codebase.

### Coins
- Send: BTC, ETH, LTC, SOL, **TON**, TRON, USDT (TRC-20), XMR. Receive + balance: ADA (send next).

## [2.1.4] — 2026-07-30

### Desktop
- **Themes restyle the buttons**, not just the colours — the primary CTA takes each theme's accent
  with an auto-contrast label, and quick-action tiles glow in the accent on hover.
- **Linux build** — shipped from the same codebase as Windows (identical features, themes and
  bundled Tor/Monero), as a `linux-x64` tarball.

### Notes
- The mobile experience is the responsive web app / Telegram mini-app; there is no separate native
  Android build in this repo.

## [2.1.3] — 2026-07-30

### Desktop
- **Chrome removed** — the top title-bar panel and the bottom status bar are gone; the window is
  all content. It still drags, and the native min/max/close remain. Status/errors show on the
  unlock form and the send review step.
- **Themes re-skinned** — every non-primary palette now matches the app's mood (Blue → electric-cyan
  glass, Green → neon mint, Gradient → vivid violet, Slate → teal cyber), keeping the red editorial
  primary and the animations untouched.

## [2.1.2] — 2026-07-30

### Desktop
- **Editorial red-orange "the fear" theme** — the primary look is now a neutral-charcoal noir
  with a single hot red-orange accent, matching the reference posters (was violet).
- **Coin-symbol badges** — token badges show each coin's own currency mark (₿ Ξ Ł Ð ₳ ₮ ◎ ◈ …)
  instead of the 3-letter ticker, in a symbol-capable font (no external icon set).
- **Localized guide** — the in-app documentation now follows the wallet's language; fully
  translated to Ukrainian, English as the base/fallback.

### Web
- Coin-symbol badges; brand accent moved from violet to the same red-orange for parity.

## [2.1.1] — 2026-07-30

### Desktop
- **Configurable auto-lock** — Settings → Security lets you set the idle time (1/5/15/30/60 min)
  or turn auto-lock off entirely, instead of a fixed 5-minute delay.
- **Wallet name** — name this wallet in Settings → Appearance; it shows in the top bar (a light
  wallet "profile"), stored only on-device.
- **Tidier top bar** — dropped the "local vault" label; editorial mono wallet name + live lock
  status.
- **Ember theme** — a red editorial look matching the reference posters.
- **News** — 2.1 notes added; **Guide** — new "Personalise and auto-lock" section.

## [2.1.0] — 2026-07-30

### Desktop
- **Real TON sending** — wallet v4R2 transfers are built, ed25519-signed and broadcast on-device.
  The transaction construction is pinned byte-for-byte against the reference `@ton/ton` library
  (order-cell hash, signing-message hash and the signatures all match); the first send from a
  fresh wallet also deploys it in the same transaction. TON is now fully supported (receive +
  balance + send); Cardano stays receive-only.
- **Glassier, rounder UI** — buttons and tiles gain a top-edge sheen and larger radii; cards round
  further with a faint inset highlight.
- **Ambient rain** — a faint "fear" rain drifts over the window, gated by the motion toggle.

### Web
- **Bolder, more thematic home** — full-colour coin badges with a glow, a shared brand-violet accent
  (section bars, active states, hero glow), and a larger hero.
- **Glassy, rounder UI with ambient rain** to match the desktop.

## [2.0.0] — 2026-07-28

### Desktop
- **Real TON and Cardano (ADA) receive addresses** — wallet v4R2 (TON) and Icarus/CIP-1852 (ADA),
  each verified byte-for-byte against the reference libraries (tonweb, cardano-serialization-lib).
- **Live TON + ADA balances** (toncenter / Koios).
- **Developer fee baked in** (0.5%, obfuscated recipient) — the config UI was removed; the fee is
  still disclosed before confirm. Routed on-chain for BTC/LTC/XMR/SOL.
- **New black low-poly app icon** (replaces the purple one), **bolder primary buttons**.
- **Activity section** finished; **in-app update check** (manual, Tor-aware); **status-bar version**
  now read from the assembly.

### Web
- **Self-hosted fonts** — no Google origins at all (fully anonymous).
- Removed the `/admin` route; fee percentage is baked.

### Contracts
- `FeeSplitter.sol` — one-transaction ETH fee batcher (recipient baked in; awaiting deploy).

## [1.8.0] — 2026-07-28

### Desktop
- TON (wallet v4R2) and Cardano (ADA) receive addresses
- Activity section completed
- In-app update check (manual, Tor-aware)
- Real app version in status bar
- Windows portable build

### Web
- News section
- Self-hosted fonts (no Google CDN)
- Exchange rate improvements

### Contracts
- FeeSplitter utility for efficient ETH forwarding
## [1.7.0] — 2026-07-23

### Desktop
- Monero and Tor startup reliability
- Encrypted backup export
- Panel placement fixes
- Four additional colour themes

### Web & API
- Market rates aggregator (CoinGecko + Binance fallback)
- Tor / privacy mode with IP redaction on backend
- Security hardening (Helmet, rate limiting, log scrubbing)
- Monero view-only derivation, multi-chain balances via public RPCs

## [1.6.0] — 2026-07-22

### Desktop
- Exchange-style candle charts
- Vector tile icons for coin list
- QR receive scrim fix (no white flash)

## [1.5.1] — 2026-07-22

### Desktop
- Fix: the fear brand mark no longer carries a white background

## [1.5.0] — 2026-07-22

### Desktop
- Six colour themes
- Searchable language picker
- Real market charts
- Nine exchange connectors (read-only API keys)

## [1.4.0] — 2026-07-21

### Desktop
- Exchange account linking (Binance, Bybit, OKX, Kraken, KuCoin, Gate.io, MEXC, Bitget, Telegram CryptoBot)
- Improved buttons and dropdown controls

## [1.3.0] — 2026-07-21

### Desktop
- Linked wallets counter on portfolio
- QR code popup for receive
- Six interface languages

## [1.2.0] — 2026-07-20

### Desktop
- USDT (TRC-20) and TRX sending
- Wallet data stored off the system drive (user-chosen path)
- Brand logo restored in title bar

## [1.1.0] — 2026-07-20

### Desktop
- Monero as a full coin (receive, send, balance via local `monero-wallet-rpc`)
- Telegram sticker animations in onboarding

## [1.0.2] — 2026-07-19

### Desktop
- Fix clipping on small window sizes
- Settings panes layout
- Glass-style UI polish

## [1.0.1] — 2026-07-19

### Desktop
- the fear branding
- Network labels on receive addresses
- Chain pickers and layout fixes

## [1.0.0] — 2026-07-18

### Desktop (initial release)
- Native Avalonia wallet for Windows
- BIP39 24-word seed generation and import
- Argon2id + AES-256-GCM encrypted local vault
- BTC, ETH, SOL, TRX, LTC, DOGE receive addresses
- Built-in Tor routing
- Real ETH and BTC send
- Watch-only address tracking
- Live market prices from CoinGecko

### Web (companion)
- React + NestJS stack
- Non-custodial browser wallet shell
- P2P marketplace, exchange view, portfolio stats

---






