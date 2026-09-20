# Umbrella Wallet — unified roadmap

**Product version:** see [`VERSION`](../VERSION) (currently **4.7.0**).  
**Consolidation date:** 2026-09-15 · **last status pass:** 2026-09-20 (P0.0, P0.2–P0.4, P0.6–P0.8, P1.1, P1.2, P1.4–P1.7, P1.9–P1.13 and L.1/L.2/L.3/L.8 closed).  
**Purpose:** one document for “what remains to do” — compiled from README, CHANGELOG, `SECURE_ANON_ROADMAP`, `CLAUDE_IMPLEMENTATION_ROADMAP_UK`, `12-coins-and-chains`, `PRIVACY`, `THREAT_MODEL`, `security-model`, `BUILD_VERIFY`, and notes on alignment with the manifesto philosophy.

Legend: ✅ done · 🟡 partial · ⏳ next · 📅 planned · ❌ not planned / blocked.

Long technical implementation phases remain in [`CLAUDE_IMPLEMENTATION_ROADMAP_UK.md`](CLAUDE_IMPLEMENTATION_ROADMAP_UK.md).  
Privacy details — in [`SECURE_ANON_ROADMAP.md`](SECURE_ANON_ROADMAP.md).  
Network matrix — in [`12-coins-and-chains.md`](12-coins-and-chains.md).  
How to verify yourself — [`VERIFY_YOUR_WALLET.md`](VERIFY_YOUR_WALLET.md).

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
| **P0.0** | **Full restore proof (critical).** Test: new wallet → ≥20 receive addresses → funds to address **#15** → delete local state → restore **from seed only** + BIP44/gap limit → full balance found. Without state — full HD scan only. Without this, self-custody is fiction. | Audit vs MANIFESTO §6 | ✅ (`RestoreFromSeedProofTests`, BTC/LTC/BCH/DOGE: 20 issued, funds on #15, state deleted, rediscovered **and spent**; the past-gap limit is pinned too) |
| P0.1 | Close HD UTXO edge-case gaps (gap scan, change not lost in UI) — after / together with P0.0 | Claude §3 | 🟡 partial in 4.5–4.7 |
| P0.2 | Pin + SHA/PGP verification for **Tor** and **monero-wallet-rpc** in fetch scripts (fail-closed) | Claude §4.2, BUILD_VERIFY | ✅ the fetch scripts verify each project's SIGNED sums file (Tor `.asc`, Monero clearsigned) against a pinned key fingerprint before trusting the hash; release builds pass `-RequireSignature`; `scripts/check-pinned-binaries.sh` keeps the scripts and THIRD_PARTY_NOTICES from drifting (they had: the pinned Tor version was gone from the mirror) |
| P0.3 | CI: verify `SHA256SUMS` ↔ attached artifacts byte-for-byte | Claude §4.1 | ✅ the release job already self-verifies before publishing; `scripts/verify-published-release.sh` now checks what the page **serves afterwards**, including that no attached artifact is missing from the manifest |
| P0.4 | Single machine-readable **capability matrix** → UI + README + tests (no discrepancies) | Claude §5.1, coins doc | ✅ `SendableSymbols` is the one source the picker, the send guard, the coins doc, the README table and §4 below are all checked against (`CapabilityMatrixTests`) |
| P0.5 | zkSync Era **send** or honestly leave Receive-only with gas explanation (not “Ready”) | coins / CHANGELOG 4.7 | 🟡 |
| **P0.6** | **Fail-closed balance.** If all RPC/nodes are unreachable (e.g. 3+ attempts) → UI: “Balance unavailable (network error)” + Refresh. **Do not** show cache as the current balance; cache only with an explicit “last synced …” / “unknown” label, never as a live `0.0000` caused by an error. Offline test mandatory. | MANIFESTO §4, audit | ✅ (`BalanceReadout`: unread → “—” + reason, cache marked “last known”, unread rows excluded from the total and counted out loud) |
| **P0.7** | **Send-path transport gate.** Before Send/Review, verify that the actual path (Tor / Clearnet / Custom) matches the user’s settings (+ kill-switch). Violation → **FAIL**, send blocked; no silent clearnet fallback. | MANIFESTO §3–4, audit | ✅ (`SendTransportGate` at Review **and** Confirm: Tor on-but-down, routed elsewhere, unapplied proxy or armed kill-switch with no proxy all refuse) |
| **P0.8** | **Network isolation CI.** Run in a sandbox / firewall without clearnet: with Tor-only enabled, the wallet process **must not** attempt clearnet connections. Without this, the kill-switch is not proven in production. | Audit | ✅ (`Category=Isolation` CI job: a loopback listener proves no socket is opened at all, with the kill-switch off as the counter-proof; plus a scan for any HttpClient built outside `PublicHttp`) |

### P1 — privacy, trust, core UX (UI honesty)

| # | Task | Source | Status |
|---|---|---|---|
| P1.1 | **Duress / decoy password** (second password → decoy vault) | README, SECURE 4.4–4.5, MANIFESTO | ✅ the vault is now the two-slot deniable file for **every** wallet (so setting one does not change the file's shape); Settings → Security sets or removes a decoy, and never reports whether one exists |
| P1.2 | Restore/finish **hidden wallet** unlock UI (BIP39 passphrase), if the product promises it | SECURE 4.4 | ✅ the derivation, scan and spend paths already honoured a passphrase; the unlock screen had no field to type one into, so the feature existed and nobody could use it. Folded behind “Advanced”, and pinned by a test |
| P1.3 | **Panic / duress wipe** with an explicit trigger | SECURE 4.5 | 🟡 DataWiper without UX |
| P1.4 | Transaction **simulation** before Confirm (what exactly changes on-chain) | README Next | ✅ already shipped — the Send review renders `SendSimulation` rows (what leaves, what returns as change, what the fee costs) |
| P1.5 | One-switch private send — bring UX to “one toggle = full checklist” on all UTXO chains | README / CHANGELOG 4.7 | ✅ the three UTXO chain lists (balance scan, fee selector, coin control + private-send plan) had drifted and left Bitcoin Cash out of coin control and out of the linkage checklist; they are now one list |
| P1.6 | Connection status in primary UI: Tor / Direct / Custom / Offline | Claude §7 | ✅ a chip in the sidebar (and in the top/bottom nav) reading TOR / PROXY / DIRECT / BLOCKED from the same live state the send gate uses — amber when Tor is on in Settings but is not carrying the traffic |
| P1.7 | Mark screenshot-guard honestly as **Windows-only** until Linux exists | Claude §7 | ✅ already honest — the Security Center says capture blocking is a Windows feature and is unavailable elsewhere, and does not score it |
| P1.8 | Verify backup (without revealing seed) + guided restore dry-run | Claude §6.5 | 🟡 verify exists partially |
| P1.9 | Address book: local, with format-check, confirm on first send | Claude §6.3 | ✅ already shipped — local encrypted book on the Send screen, wrong-network shape check, and a first-time-to-this-address confirmation (`SendSafetyIntegrationTests`) |
| P1.10 | Consolidate Activity into one screen + filters + honest partial-history labels | Claude §6.4 | ✅ one screen with filters, a last-synced line, and a note naming the held coins whose history this build does not read — computed from the capability catalog, so it cannot claim coverage the code lacks |
| **P1.11** | **Privacy Radar — limits under every status.** Mandatory text: *what is protected* and *what is not* (e.g. “Tor hides your IP, but the selected explorer sees your addresses”). Difference: “Tor works” ≠ “IP is hidden from whoever already received your address.” Without this, Radar violates MANIFESTO §1–2. | MANIFESTO, audit | ✅ every Privacy Radar finding renders its limit underneath (`PrivacyScoreFinding.LimitCode`), so no status can appear as a bare reassurance |
| **P1.12** | **“What leaked?” after send.** Short report: IP hidden yes/no · addresses seen by Node X · broadcast via Tor/Direct · coin control / fresh change on or off. The user sees the privacy cost of that operation. | MANIFESTO §1–2, audit | ✅ `SendLeakReport` renders under the send result: IP, kill-switch, ledger, input linkage **with the count**, change freshness, coin control, and the one nobody can fix (the recipient knows) |
| **P1.13** | **Duress test scenario.** QA scenario: “inspector coerces” → decoy vault opens, real funds remain inaccessible with the decoy password. Without this, P1.1–P1.3 are features, not verified solutions. | MANIFESTO intro, audit | ✅ `DuressWalletScenarioTests` — coercion opens the decoy, the real phrase stays unreachable, the two wallets share no address, the file's size is identical with and without a decoy, and removal is as deniable as adding |
| **P1.20** | **Self-verify mode** (long, but required by philosophy). CLI or Debug panel: verify no clearnet in wallet connections; export xpub → balance in a third-party scanner; Tor via `curl --socks5-hostname`; cross-check with `VERIFY_YOUR_WALLET.md`. | MANIFESTO, audit | ✅ watch-only account xpub export in Settings → Security (pinned by a test that the addresses it yields are the wallet's own, external **and** change), and [`VERIFY_YOUR_WALLET.md`](VERIFY_YOUR_WALLET.md) written in full: balance from a third party, live route, download, build, counterparties — and what none of it proves |

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
| N.1 | Send **any ERC-20** (not only native ETH / separate USDT) | ✅ the picker lists every held ERC-20 and routes on the **contract**, never the ticker; the amount is scaled by the decimals that contract reports (a row without them is refused, not assumed to be 18), the token balance is read from the contract at quote time rather than from a cached row, and the review says the fee comes out of ETH. Verify a first send with a small amount |
| N.2 | Send **any TRC-20** (not only USDT) | ✅ the same shape as N.1 — routed by contract, scaled by the decimals the contract reports, balance read from the contract with `triggerconstantcontract`. USDT is now just the TRC-20 whose contract was already known, and it gained the exact-scaling refusal the old path lacked (it multiplied through `Math.Pow` and truncated). Fee in TRX, said before Confirm |
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
| R.3 | Sign releases with **GPG / Sigstore** | ✅ from the next release — every artifact **and** the sums file carry a keyless build attestation (GitHub OIDC → public transparency log), verified with `gh attestation verify`. No signing key exists, so none can be stolen. Earlier releases have checksums only, and the docs say so |
| R.4 | SBOM / provenance as a release asset | 📅 |
| R.5 | External **security audit** — status in [`../AUDIT_STATUS.md`](../AUDIT_STATUS.md) | 📅 Planned |
| **R.6** | **EV Code Signing** (~$500–800/year) or OV for Windows build — without this SmartScreen / Store block unsigned exe | ⏳ |
| **R.7** | **Microsoft Store** (optional): separate Store build / Microsoft signature | 📅 |

### P2 / Legal — store compliance (see §9 + root legal files)

| # | Task | Status |
|---|---|---|
| **L.0** | Legal docs in repo: TOS, Privacy Policy, APP_STORE_NOTES, GEO, CONTACT, TRADEMARK, CoC, LICENSE, LEGAL/, SECURITY/ | ✅ docs (2026-09-15) |
| **L.1** | **Apple / first-run:** screen — “we do not store keys; you are responsible for backup” | ✅ code (first-run page, gates create/import/unlock) |
| **L.2** | **No “Fully Private” claims** in UI/listing — follow APP_STORE_NOTES | ✅ UI strings audited in all 6 languages; a test refuses absolute claims |
| **L.3** | **Age gate 18+** on first launch | ✅ code (separate tick) |
| **L.4** | **Google Play / store copy:** “Not a financial service…” | ⏳ at submission |
| **L.5** | **Jurisdictional blocklist** — policy in GEO_BLOCKING.md | 📅 enforce |
| **L.6** | **Linux package signing** (PGP for Flatpak/Snap / distro repos) | ⏳ |
| **L.7** | **Geo-blocking** in store builds (IP/locale) after legal consultation | 📅 |
| **L.8** | **ToS / Privacy Policy acceptance** on first launch | ✅ code (versioned acceptance; changed wording asks again) |

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
| Explorer sees the session address set | Already: node choice + Tor, and the Radar now says so under every status (P1.11); next: fewer addresses per request, Silent Payments |
| UTXO linkage | PayJoin → CoinJoin; Silent Payments; the post-send report now names the count (P1.12 ✅) |
| Broadcast timing ↔ IP | Dandelion++ |
| Release substitution on GitHub | ✅ keyless build attestations (R.3) + the reproducible-build check; what remains is that a user must still choose to verify |
| $5 wrench | Duress / decoy password ✅ — with its limits stated in the app: it does not hide that other wallets exist on the machine, nor help against being watched typing |
| User forced to *trust* the client | ✅ watch-only xpub export + [`VERIFY_YOUR_WALLET.md`](VERIFY_YOUR_WALLET.md) + release attestations (R.3) |
| No external audit | Commission audit (R.5); until then do not write “audited” |
| Docs drifted from code | Annual documentation audit (M.7) |
| App Store / Play rejection | §9 compliance (L.1–L.8, wording) |
| SmartScreen / unsigned Windows | R.6 EV code signing |

---

## 6. Execution order (user perspective + philosophy)

1. ✅ **P0.0** — Full restore proof (without this, all self-custody security is in question).  
2. ✅ **P0.6–P0.8** — fail-closed balance + send transport gate + network isolation CI.  
3. ✅ **P0.2–P0.4** — supply chain helpers + checksum CI + capability matrix.  
4. ✅ **P1.11 / P1.12** — Privacy Radar limits + “What leaked?” (UI honesty).  
5. ✅ **P1.1 / P1.13** — duress/decoy + verified “under coercion” scenario. **P1.2–P1.3** (hidden-wallet unlock UI, panic wipe) remain.  
6. ✅ **P1.2, P1.4–P1.7, P1.9, P1.10** — hidden-wallet unlock, simulation, private-send parity across UTXO chains, connection status, honest capture wording, address book, Activity coverage. **P1.3** (panic wipe) is the last P1 open. ← **next**  
7. ✅ **P1.20** + §10 — self-verify (xpub export) and the public guide “how to check you are not being lied to.”  
8. ✅ **L.1 / L.3 / L.8** (+ L.2 wording) — disclaimers / age / ToS in the app; **L.4** is store-listing copy, written at submission.  
9. **R.6 / L.6** — EV signing for Windows + PGP for Linux packages. R.3 attestations already cover “did this come from the project”; EV covers SmartScreen, which is a different problem and needs a legal entity and money. ← **next (human/process)**  
10. ✅ **N.1 / N.2** ERC-20 and TRC-20 — **N.3** (SPL / Jetton send) remains. ← **next**  
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
| App Store: non-custodial confirmation + L.1/L.3 in **UI** | ✅ | First-run screen shipped; listing copy still to be written at submission |
| Google Play: crypto publisher rules + L.4 | ⏳ | Business verification; **no** KYC-less on-ramp in core |
| Microsoft Store: code signing | ⏳ R.2/R.6 | Without EV — keep GitHub side-load |
| Linux repos: PGP (L.6) | ⏳ | Fedora / Debian / Flathub |

### 9.3. Disclaimers (mandatory for store / public release)

| Element | ID | Status |
|---|---|---|
| On-start: not a financial institution + 18+ + non-custodial | L.1, L.3 | ✅ |
| On-start: no guarantee of anonymity (transparent chains) | L.2, text below | ✅ |
| Privacy warning before Send (address forever on-chain; RPC may see metadata) | — / P1.12 | 📅 |
| Geo-blocked list | L.5, L.7 | 📅 |
| ToS / Privacy Policy acceptance | L.8 | ✅ |

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

## 10. How to verify you are not being lied to

Written: **[`VERIFY_YOUR_WALLET.md`](VERIFY_YOUR_WALLET.md)** — the balance from a third party
(watch-only xpub, with its privacy cost stated), the live route (`curl --socks5-hostname`, and the
wallet's own connections), the download (`SHA256SUMS`), the build (reproducible-build check and the
pinned third-party binaries), and who the wallet talks to.

It ends with what none of it proves: a clean machine, a private transparent chain, or an audit that
has not happened.

**Still missing from that page, and named there rather than glossed over:** a GPG/Sigstore signature
over the checksum manifest (R.3), so a checksum proves the file matches the release page but not who
published it.

---

📖 Back to [Documentation Index](INDEX.md)

