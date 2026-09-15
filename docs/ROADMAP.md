# Umbrella Wallet — unified roadmap

**Product version:** see [`VERSION`](../VERSION) (currently **4.7.0**).  
**Consolidation date:** 2026-09-15 (updated: UI honesty / fail-closed / verify-yourself from the audit vs [`MANIFESTO.md`](../MANIFESTO.md)).  
**Purpose:** one document for “what remains to do” — compiled from README, CHANGELOG, `SECURE_ANON_ROADMAP`, `CLAUDE_IMPLEMENTATION_ROADMAP_UK`, `12-coins-and-chains`, `PRIVACY`, `THREAT_MODEL`, `security-model`, `BUILD_VERIFY`, and notes on alignment with the manifesto philosophy.

Legend: ✅ done · 🟡 partial · ⏳ next · 📅 planned · ❌ not planned / blocked.

Long technical implementation phases remain in [`CLAUDE_IMPLEMENTATION_ROADMAP_UK.md`](CLAUDE_IMPLEMENTATION_ROADMAP_UK.md).  
Privacy details — in [`SECURE_ANON_ROADMAP.md`](SECURE_ANON_ROADMAP.md).  
Network matrix — in [`12-coins-and-chains.md`](12-coins-and-chains.md).  
How to verify yourself — planned as [`VERIFY_YOUR_WALLET.md`](VERIFY_YOUR_WALLET.md) (not written yet; see §10 and P1.20).

---

## 1. Primary rule

Fund safety, correct balances, and **UI honesty** matter more than coin count or screen count.  
Do not announce “ready / full / private” until the end-to-end scenario works and is covered by tests.

Philosophy ([`MANIFESTO.md`](../MANIFESTO.md)): the user must **verify**, not **trust**. A promise without a self-verification mechanism and without text stating “what is not protected” is marketing fog — even when the code is solid.

**Release gate (short):** green `dotnet test` + `Release` build · same version everywhere · Windows/Linux artifacts + `SHA256SUMS` · send-path has vectors · coin limitations visible in UI · fail-closed: network error ≠ zero balance.

**Philosophy coverage (audit orientation):** ~85% — gaps are specifically in communicating limits, fail-closed for balance/send, and “verify yourself.”

---

## 2. Already done (do not re-list as “todo”)

| Area | Status |
|---|---|
| Non-custodial vault (Argon2id + AES-256-GCM), BIP39, screenshot-guard seed | ✅ |
| Tor + kill-switch fail-closed, Verify Tor, custom SOCKS5 | ✅ |
| Per-network RPC/explorer selection, list of “who can see addresses” | ✅ (4.7) |
| Monero full wallet; BTC/LTC/BCH/DOGE HD scan + fresh receive | ✅ |
| ETH + L2 (Arbitrum/Base/Optimism/Linea) send; zkSync — balance only | ✅ / 🟡 |
| THORChain swaps, Security Center, Privacy Radar, coin control (BTC/LTC/DOGE) | ✅ (Radar exists; **limit explanations — see P1.11**) |
| Address poisoning / EIP-55 / reuse warnings | ✅ |
| Desktop Win/Linux, themes, 6 languages, encrypted backup | ✅ |
| SHA256SUMS on releases | ✅ |

---

## 3. Priority queue (what to do next)

### P0 — fund safety and honesty (fail-closed)

| # | Task | Source | Status |
|---|---|---|---|
| **P0.0** | **Full restore proof (critical).** Test: new wallet → ≥20 receive addresses → funds to address **#15** → delete local state → restore **from seed only** + BIP44/gap limit → full balance found. Without state — full HD scan only. Without this, self-custody is fiction. | Audit vs MANIFESTO §6 | 🏆 ⏳ |
| P0.1 | Close HD UTXO edge-case gaps (gap scan, change not lost in UI) — after / together with P0.0 | Claude §3 | 🟡 partial in 4.5–4.7 |
| P0.2 | Pin + SHA/PGP verification for **Tor** and **monero-wallet-rpc** in fetch scripts (fail-closed) | Claude §4.2, BUILD_VERIFY | ⏳ |
| P0.3 | CI: verify `SHA256SUMS` ↔ attached artifacts byte-for-byte | Claude §4.1 | ⏳ |
| P0.4 | Single machine-readable **capability matrix** → UI + README + tests (no discrepancies) | Claude §5.1, coins doc | ⏳ |
| P0.5 | zkSync Era **send** or honestly leave Receive-only with gas explanation (not “Ready”) | coins / CHANGELOG 4.7 | 🟡 |
| **P0.6** | **Fail-closed balance.** If all RPC/nodes are unreachable (e.g. 3+ attempts) → UI: “Balance unavailable (network error)” + Refresh. **Do not** show cache as the current balance; cache only with an explicit “last synced …” / “unknown” label, never as a live `0.0000` caused by an error. Offline test mandatory. | MANIFESTO §4, audit | ⏳ |
| **P0.7** | **Send-path transport gate.** Before Send/Review, verify that the actual path (Tor / Clearnet / Custom) matches the user’s settings (+ kill-switch). Violation → **FAIL**, send blocked; no silent clearnet fallback. | MANIFESTO §3–4, audit | ⏳ |
| **P0.8** | **Network isolation CI.** Run in a sandbox / firewall without clearnet: with Tor-only enabled, the wallet process **must not** attempt clearnet connections. Without this, the kill-switch is not proven in production. | Audit | ⏳ |

### P1 — privacy, trust, core UX (UI honesty)

| # | Task | Source | Status |
|---|---|---|---|
| P1.1 | **Duress / decoy password** (second password → decoy vault) | README, SECURE 4.4–4.5, MANIFESTO | ⏳ wipe/passphrase code partial |
| P1.2 | Restore/finish **hidden wallet** unlock UI (BIP39 passphrase), if the product promises it | SECURE 4.4 | 🟡 |
| P1.3 | **Panic / duress wipe** with an explicit trigger | SECURE 4.5 | 🟡 DataWiper without UX |
| P1.4 | Transaction **simulation** before Confirm (what exactly changes on-chain) | README Next | ⏳ |
| P1.5 | One-switch private send — bring UX to “one toggle = full checklist” on all UTXO chains | README / CHANGELOG 4.7 | 🟡 |
| P1.6 | Connection status in primary UI: Tor / Direct / Custom / Offline | Claude §7 | ⏳ |
| P1.7 | Mark screenshot-guard honestly as **Windows-only** until Linux exists | Claude §7 | ⏳ |
| P1.8 | Verify backup (without revealing seed) + guided restore dry-run | Claude §6.5 | 🟡 verify exists partially |
| P1.9 | Address book: local, with format-check, confirm on first send | Claude §6.3 | 🟡 / verify state |
| P1.10 | Consolidate Activity into one screen + filters + honest partial-history labels | Claude §6.4 | ⏳ |
| **P1.11** | **Privacy Radar — limits under every status.** Mandatory text: *what is protected* and *what is not* (e.g. “Tor hides your IP, but the selected explorer sees your addresses”). Difference: “Tor works” ≠ “IP is hidden from whoever already received your address.” Without this, Radar violates MANIFESTO §1–2. | MANIFESTO, audit | ⏳ |
| **P1.12** | **“What leaked?” after send.** Short report: IP hidden yes/no · addresses seen by Node X · broadcast via Tor/Direct · coin control / fresh change on or off. The user sees the privacy cost of that operation. | MANIFESTO §1–2, audit | ⏳ |
| **P1.13** | **Duress test scenario.** QA scenario: “inspector coerces” → decoy vault opens, real funds remain inaccessible with the decoy password. Without this, P1.1–P1.3 are features, not verified solutions. | MANIFESTO intro, audit | ⏳ |
| **P1.20** | **Self-verify mode** (long, but required by philosophy). CLI or Debug panel: verify no clearnet in wallet connections; export xpub → balance in a third-party scanner; Tor via `curl --socks5-hostname`; cross-check with `VERIFY_YOUR_WALLET.md`. | MANIFESTO, audit | 📅 Long |

### P2 — on-chain privacy “heavy artillery”

| # | Task | Source | Status |
|---|---|---|---|
| P2.1 | **Taproot (BIP-341/86)** — scanner `m/86'` + key-path spend, not derivation only | PRIVACY, coins | 📅 |
| P2.2 | **PayJoin (BIP-78)** | PRIVACY, threat model | 📅 |
| P2.3 | **CoinJoin** (after PayJoin) | PRIVACY | 📅 |
| P2.4 | **Dandelion++** (broadcast timing privacy) | coins roadmap | 📅 |
| P2.5 | **Silent Payments** (BTC) | coins long-term | 📅 |
| P2.6 | Zcash **shielded** send (currently transparent t-addr only) | coins / CHANGELOG | 📅 |

### P2 — tokens and networks (full cycle or do not add)

Rule: new network/token = derive + validate + balance + send + fee + history/status + tests + matrix row.

| # | Task | Status |
|---|---|---|
| N.1 | Send **any ERC-20** (not only native ETH / separate USDT) | 📅 near |
| N.2 | Send **any TRC-20** (not only USDT) | 📅 near |
| N.3 | Send **any SPL** / full Jetton send (Jetton balances already exist) | 📅 near / 🟡 |
| N.4 | **XRP** | 📅 |
| N.5 | **Stellar (XLM)** | 📅 |
| N.6 | **Cosmos (ATOM)** / IBC | 📅 |
| N.7 | **NEAR** | 📅 |
| N.8 | **Polkadot (DOT)** | 📅 |
| N.9 | THORChain expansion / swap reliability (expiry, slippage, refund, failed broadcast) | 📅 after core |

### P2 — hardware, platforms, release trust

| # | Task | Status |
|---|---|---|
| H.1 | Bitcoin **PSBT** export/import + watch-only xpub | 📅 |
| H.2 | **Ledger / Trezor** (sign on device, no seed in Umbrella) | 📅 |
| H.3 | **Multisig** 2-of-3 | 📅 long |
| H.4 | **Android** (separate mobile threat model + UX, not a desktop copy) | 📅 Planned |
| R.1 | **Reproducible builds** + published attestations (honestly: .NET single-file installer is not bit-identical) | 🟡 docs / ⏳ attestations |
| R.2 | **Code signing OV/EV (SmartScreen)** | ⏳ **start ≥60 days before store release**; legal entity required; ~$300–1000/year (see R.6) |
| R.3 | Sign releases with **GPG / Sigstore** | 📅 |
| R.4 | SBOM / provenance as a release asset | 📅 |
| R.5 | External **security audit** — status in [`../AUDIT_STATUS.md`](../AUDIT_STATUS.md) | 📅 Planned |
| **R.6** | **EV Code Signing** (~$500–800/year) or OV for Windows build — without this SmartScreen / Store block unsigned exe | ⏳ |
| **R.7** | **Microsoft Store** (optional): separate Store build / Microsoft signature | 📅 |

### P2 / Legal — store compliance (see §9 + root legal files)

| # | Task | Status |
|---|---|---|
| **L.0** | Legal docs in repo: TOS, Privacy Policy, APP_STORE_NOTES, GEO, CONTACT, TRADEMARK, CoC, LICENSE, LEGAL/, SECURITY/ | ✅ docs (2026-09-15) |
| **L.1** | **Apple / first-run:** screen — “we do not store keys; you are responsible for backup” | ⏳ code |
| **L.2** | **No “Fully Private” claims** in UI/listing — follow APP_STORE_NOTES | ⏳ audit UI strings |
| **L.3** | **Age gate 18+** on first launch | ⏳ code |
| **L.4** | **Google Play / store copy:** “Not a financial service…” | ⏳ at submission |
| **L.5** | **Jurisdictional blocklist** — policy in GEO_BLOCKING.md | 📅 enforce |
| **L.6** | **Linux package signing** (PGP for Flatpak/Snap / distro repos) | ⏳ |
| **L.7** | **Geo-blocking** in store builds (IP/locale) after legal consultation | 📅 |
| **L.8** | **ToS / Privacy Policy acceptance** on first launch | ⏳ code |

### P3 — product / maintainability

| # | Task | Status |
|---|---|---|
| M.1 | Split `MainWindow` / `MainViewModel` into feature views (one screen each) | 🟡 partial classes already exist |
| M.2 | UI automation smoke: create → unlock → receive → send validate → backup → lock | ⏳ |
| M.3 | Design system + WCAG AA on critical screens | ⏳ |
| M.4 | Price alerts | 📅 |
| M.5 | More exchange connectors (read-only) on request | 📅 |
| M.6 | NFT / Staking — **do not return** to primary nav without a full model + spam policy | ❌ / later |
| **M.7** | **Documentation audit (annual):** independent review of README, PRIVACY, THREAT_MODEL, MANIFESTO, and this ROADMAP for consistency with the code. Documents age faster than code. | Audit | 📅 recurring |

---

## 4. Coin matrix (summary)

| Symbol | Receive | Balance | Send | History | Note |
|---|:---:|:---:|:---:|:---:|---|
| BTC | ✅ | ✅ | ✅ | ✅ | coin control; Taproot spend not yet |
| LTC | ✅ | ✅ | ✅ | ✅ | |
| BCH | ✅ | ✅ | ✅ | ✅ | HD scan since 4.7 |
| DOGE | ✅ | ✅ | ✅ | ✅ | HD scan since 4.7 |
| ETH | ✅ | ✅ | ✅ | ✅ | general ERC-20 send still 📅 |
| Arb / Base / OP / Linea | ✅ | ✅ | ✅ | 🟡 | |
| zkSync Era | ✅ | ✅ | ❌ | 🟡 | Receive only (gas) |
| TRX + USDT TRC-20 | ✅ | ✅ | ✅ | ✅ | other TRC-20 — 📅 |
| SOL | ✅ | ✅ | ✅ | ✅ | arbitrary SPL send — 📅 |
| TON | ✅ | ✅ | ✅ | ✅ | Jetton balance ✅; Jetton send — 📅 |
| ADA | ✅ | ✅ | ✅ | ✅ | |
| XMR | ✅ | ✅ | ✅ | ✅ | full private |
| AVAX / BNB / MATIC / FTM / CRO | ✅ | ✅ | ✅ | 🟡 | EVM family |
| ZEC | ✅ | ✅ | ❌ | 🟡 | transparent `t1…` only |
| XRP / XLM / ATOM / NEAR / DOT | — | — | — | — | 📅 Planned |

---

## 5. Open threat / privacy gaps (do not paper over)

| Gap | What to do |
|---|---|
| Malware on the user’s PC | Hardware wallet (H.1–H.2) |
| Explorer sees the session address set | Already: node choice + Tor; next: fewer addresses per request, Silent Payments; **Radar must say this (P1.11)** |
| UTXO linkage | PayJoin → CoinJoin; Silent Payments; post-send report (P1.12) |
| Broadcast timing ↔ IP | Dandelion++ |
| Release substitution on GitHub | GPG/Sigstore + reproducible attestations |
| $5 wrench | Duress / decoy (P1.1) + test scenario (P1.13) |
| User forced to *trust* the client | Self-verify (P1.20) + `VERIFY_YOUR_WALLET.md` (§10) |
| No external audit | Commission audit (R.5); until then do not write “audited” |
| Docs drifted from code | Annual documentation audit (M.7) |
| App Store / Play rejection | §9 compliance (L.1–L.8, wording) |
| SmartScreen / unsigned Windows | R.6 EV code signing |

---

## 6. Execution order (user perspective + philosophy)

1. **P0.0** — Full restore proof (without this, all self-custody security is in question).  
2. **P0.6–P0.8** — fail-closed balance + send transport gate + network isolation CI.  
3. **P0.2–P0.4** — supply chain helpers + checksum CI + capability matrix.  
4. **P1.11 / P1.12** — Privacy Radar limits + “What leaked?” (UI honesty).  
5. **P1.1 / P1.13** (+ P1.2–P1.3) — duress/decoy + verified “under coercion” scenario.  
6. **P1.4–P1.6** — simulation + connection status + private-send polish.  
7. **P1.20** + §10 — self-verify mode and a public guide “how to check you are not being lied to.”  
8. **L.1 / L.3 / L.4 / L.8** (+ L.2 wording) — disclaimers / age / ToS **before** any store submission.  
9. **R.6 / L.6** — EV signing for Windows + PGP for Linux packages.  
10. **N.1–N.3** — universal tokens, only with a full cycle.  
11. **P2.1–P2.2** — Taproot spend + PayJoin.  
12. **H.1 → H.2** — PSBT, then Ledger/Trezor.  
13. **H.4** + L.5/L.7 — Android only after a mobile spec **and** store/geo compliance.  
14. New L1s (XRP…) — only after a stable core + matrix.  
15. **R.7 / L.5** — Microsoft Store / geo-blocklist as needed.

Detailed PR schedule — §12 in [`CLAUDE_IMPLEMENTATION_ROADMAP_UK.md`](CLAUDE_IMPLEMENTATION_ROADMAP_UK.md).

---

## 7. Deliberately not planned

| | |
|---|---|
| ❌ | Ads, telemetry, analytics |
| ❌ | Custody of user funds |
| ❌ | Venture / “growth” features instead of security |
| ❌ | KYC / on-ramp inside the core wallet without a separate legal decision |
| ❌ | Returning web/backend as part of the product |
| ❌ | Forks / rebrands without permission (see LICENSE) |
| ❌ | Claims of “anonymous / fully private / untraceable” without limit text (and for stores — see §9.4) |
| ❌ | KYC-less **on-ramp** (card → crypto) inside the app without a license (Play / Apple / MSB) |

---

## 8. Sources of truth (where this was compiled from)

| Document | Role |
|---|---|
| This file — **`docs/ROADMAP.md`** | Single backlog of “what remains” |
| [`../MANIFESTO.md`](../MANIFESTO.md) | Rules the backlog must align with |
| [`CLAUDE_IMPLEMENTATION_ROADMAP_UK.md`](CLAUDE_IMPLEMENTATION_ROADMAP_UK.md) | Detailed phases + DoD for implementation |
| [`SECURE_ANON_ROADMAP.md`](SECURE_ANON_ROADMAP.md) | Security/anonymity pillars + statuses |
| [`12-coins-and-chains.md`](12-coins-and-chains.md) | Full network matrix and “coming soon” |
| [`../README.md`](../README.md) | Public short roadmap |
| [`../CHANGELOG.md`](../CHANGELOG.md) | What already shipped |
| [`../PRIVACY.md`](../PRIVACY.md), [`../THREAT_MODEL.md`](../THREAT_MODEL.md) | Open gaps |
| [`BUILD_VERIFY.md`](BUILD_VERIFY.md) | Reproducible / verify builds |
| [`../TERMS_OF_SERVICE.md`](../TERMS_OF_SERVICE.md) | Store Terms |
| [`../PRIVACY_POLICY.md`](../PRIVACY_POLICY.md) | Formal privacy policy |
| [`../APP_STORE_NOTES.md`](../APP_STORE_NOTES.md) | Approved store wording (incl. Apple 3.1.5 / Play) |
| [`../GEO_BLOCKING.md`](../GEO_BLOCKING.md) | Restricted jurisdictions |
| [`../AUDIT_STATUS.md`](../AUDIT_STATUS.md) | External audit: none yet |
| [`../CONTACT.md`](../CONTACT.md) | Public contacts |
| [`../TRADEMARK_POLICY.md`](../TRADEMARK_POLICY.md) | Brand / name protection |
| [`../CODE_OF_CONDUCT.md`](../CODE_OF_CONDUCT.md) | Community standards |
| [`../LICENSE`](../LICENSE) | Free-Use, No-Derivatives |
| [`../LEGAL/README.md`](../LEGAL/README.md) | Legal document map |
| [`../SECURITY/README.md`](../SECURITY/README.md) | Disclosure process folder |
| [`REPO_HARDENING.md`](REPO_HARDENING.md) | Live GitHub security / legal checklist |
| [`PRE_BETA_CHECKLIST.md`](PRE_BETA_CHECKLIST.md) | Public-beta gate (docs done; P0 code still open) |
| [`INDEX.md`](INDEX.md) | Documentation hub |

Update **this** file when priorities change; phase details belong in specialized docs, without duplicating conflicting statuses.

---

## 9. Compliance and legal safety (app stores)

Difference: **how the wallet works technically** (Tor, duress, non-custodial) ≠ **how it may be presented** in the App Store / Play / Microsoft Store. Desktop side-load (GitHub Releases) — low moderation risk; mobile stores — high.

| Platform | Moderation risk | Note |
|---|---|---|
| Apple App Store | **High** | §3.1.5 / payment apps; strict wording |
| Google Play | **Medium** | Crypto / Financial Services Policy since 2022 |
| Microsoft Store | **Medium** | signature required; without EV — mostly side-load |
| Windows/Linux side-load (GitHub) | **Low** | primary channel today |
| Flatpak / Snap / AUR | **Low** | PGP required (L.6) |

### 9.1. Platform rules (short)

| Platform | Allowed | Not allowed |
|---|---|---|
| **Apple** | Non-custodial (keys on device), access to own funds | Custody without a license; “hot” investment promises; misleading “fully private” when address clearnet leakage exists |
| **Google Play** | Non-custodial + clear disclaimer; publisher business verification | KYC-less on-ramp without a license; advertising “anonymous transactions” on transparent chains |
| **Microsoft Store** | Signed package | Unsigned exe as an “official” Store listing |
| **Linux repos** | Open-source + PGP | Unsigned packages in official repos |

### 9.2. Stores — backlog

| Requirement | Status | Comment |
|---|---|---|
| Formal TOS + Privacy Policy + App Store notes + Geo + Contact | ✅ | Root `.md` files (2026-09-15) |
| App Store: non-custodial confirmation + L.1/L.3 in **UI** | ⏳ | Documents ready; first-run screen still needed |
| Google Play: crypto publisher rules + L.4 | ⏳ | Business verification; **no** KYC-less on-ramp in core |
| Microsoft Store: code signing | ⏳ R.2/R.6 | Without EV — keep GitHub side-load |
| Linux repos: PGP (L.6) | ⏳ | Fedora / Debian / Flathub |

### 9.3. Disclaimers (mandatory for store / public release)

| Element | ID | Status |
|---|---|---|
| On-start: not a financial institution + 18+ + non-custodial | L.1, L.3 | ⏳ |
| On-start: no guarantee of anonymity (transparent chains) | L.2, text below | ⏳ |
| Privacy warning before Send (address forever on-chain; RPC may see metadata) | — / P1.12 | 📅 |
| Geo-blocked list | L.5, L.7 | 📅 |
| ToS / Privacy Policy acceptance | L.8 | ⏳ |

**On-start draft (EN, for implementing L.1/L.2/L.3):**

> **NOT A FINANCIAL INSTITUTION**  
> Umbrella Wallet is non-custodial software. We do not hold your funds, manage your keys, or provide banking services. You are solely responsible for your recovery phrase and for complying with local law.  
>  
> **NO GUARANTEE OF ANONYMITY**  
> Privacy features (Tor, encrypted local storage) do not make a public ledger private. Blockchain analysis can still link activity on transparent chains.  
>  
> **USE AT YOUR OWN RISK**  
> Lost recovery phrases cannot be recovered by anyone. We are not liable for loss due to user error, malware, or regulatory action.

**Pre-Send draft (EN):**

> **TRANSACTION EXPOSES DATA**  
> Confirming means: your address is permanently on the chain; servers you query may learn metadata even over Tor; coin-control choices can link UTXOs publicly.

### 9.4. Wording (store / marketing)

| Do not use | Use |
|---|---|
| Fully private / anonymous / untraceable | Privacy-enhanced (non-custodial) |
| Anonymous transactions | Non-KYC self-custody; on-chain privacy varies by coin |
| Secure from government | Local encryption; user-held keys |
| No logging (as an absolute) | No cloud account; local-only storage (no telemetry) — see PRIVACY.md |

Monero may be described as a privacy coin **honestly**; Bitcoin/ETH and similar — **never** as “anonymous.”

### 9.5. Licenses and certificates

| Certificate / step | Approximate cost | Why |
|---|---|---|
| EV Code Signing (R.6) | ~$500–800/year | Windows trust / Store |
| Apple Developer / Google Play Console | annual fees | Mobile listing |
| Business verification | depends on jurisdiction | Play / Apple publisher |
| Money Transmitter / VASP | expensive | **Only** if a custodial on-ramp appears — currently **not planned** |
| BitLicense (NY etc.) | — | Non-custodial often exempt, but a **lawyer** must confirm; L.5/L.7 |

### 9.6. Practical steps before store submission

1. Legal entity / developer account (EU, Switzerland, or another jurisdiction — with a lawyer).  
2. EV code signing (R.6).  
3. Terms of Service + Privacy Policy + L.8 in the app.  
4. Screenshots for reviewers: disclaimers; proof that seed/keys never go to a server.  
5. Avoid the words in §9.4 “Do not use.”  
6. Always keep a **side-load** channel: GitHub Releases, FlatHub, AUR — independent of any store.

**Geo-blocking (L.7)** — politically and technically sensitive (PRC etc.; certain US states). Implement only after legal consultation; do not confuse with Tor censorship.

---

## 10. How to verify you are not being lied to (document plan)

Separate file **`docs/VERIFY_YOUR_WALLET.md`** (create together with P1.20 / R.1–R.3):

1. Commands to verify Tor connectivity (`curl --socks5-hostname`, no clearnet in netstat for the wallet process).  
2. Export **xpub** (or watch-only) and cross-check balance in an independent scanner — without trusting the Umbrella UI.  
3. Download verification: `SHA256SUMS` + (when available) GPG/Sigstore; build from source per [`BUILD_VERIFY.md`](BUILD_VERIFY.md).  
4. Links to Settings → Privacy (“who can see addresses”) and to Privacy Radar limit text (P1.11).

Until the file exists, this section is a placeholder and a backlog item, not a finished guide.

---

📖 Back to [Documentation Index](INDEX.md)

