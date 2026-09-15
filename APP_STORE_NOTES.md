# App Store & Play — wording and reviewer notes

**Purpose:** Approved language for listings, screenshots, first-run screens, and reviewer Q&A.  
**Tone rule:** Keep [MANIFESTO.md](MANIFESTO.md) honesty. Do **not** use marketing that oversells privacy on transparent chains.

**Contacts for store accounts:** [CONTACT.md](CONTACT.md)

---

## 1. One-line description (listing)

> Non-custodial desktop crypto wallet. Keys stay on your device. No account, no KYC, no tracking.

## 2. Required claims (use these)

| Topic | Use this wording |
|---|---|
| Custody | **Umbrella is non-custodial software. Keys never leave your device.** |
| Not a bank | **We do not hold funds, manage accounts, or provide banking or money-transmitter services.** |
| Age | **Users must be 18+ (or age of majority) to use this application.** |
| Liability | **Lost recovery phrases cannot be recovered by us. Use at your own risk.** |
| Privacy (general) | **Privacy-enhanced (non-custodial).** Tor and local encryption reduce metadata exposure; they do not make a public ledger private. |
| Monero | **Monero is supported as a full local wallet** (private by protocol). |
| Transparent coins | **Bitcoin, Ethereum, and similar chains are public ledgers.** Amounts and addresses are permanent. |

## 3. Forbidden / high-risk claims (do not use in store copy)

| Avoid | Why |
|---|---|
| Fully private / anonymous / untraceable | Misleading on BTC/ETH/etc.; store risk |
| Secure from government / untouchable | Overclaim; regulatory risk |
| No logging (absolute, without context) | Prefer: no cloud account; no telemetry; local-only storage |
| Guaranteed anonymity with Tor | Tor hides IP; addresses still go to explorers |

## 4. First-run screen (checklist for implementation — L.1 / L.3 / L.8)

Must show and require acknowledgement of:

1. Non-custodial / not a financial institution  
2. No guarantee of anonymity on transparent chains  
3. Age 18+  
4. Links to [TERMS_OF_SERVICE.md](TERMS_OF_SERVICE.md) and [PRIVACY_POLICY.md](PRIVACY_POLICY.md)  
5. “Lost seed = lost funds; we cannot help recover”

Suggested short body (EN) — same spirit as ROADMAP §9.3.

## 5. Apple App Store notes (Guideline 3.1.5 — Cryptocurrencies)

**Official source:** [App Store Review Guidelines §3.1.5](https://developer.apple.com/app-store/review/guidelines/#cryptocurrencies)  
**Umbrella today:** desktop Windows/Linux via GitHub Releases. These notes apply when/if an **iOS** build is submitted (roadmap mobile). Desktop side-load is not App Store distribution.

### 5.1. What Apple allows (and our mapping)

| Guideline | Rule (summary) | Umbrella stance |
|-----------|----------------|-----------------|
| **3.1.5 (i) Wallets** | May facilitate virtual-currency *storage* if published by a developer enrolled as an **organization** (not an individual) | Non-custodial: keys generated and stored **only on device**. No Umbrella-hosted balances. **Requires a legal entity + org Apple Developer account before submission** (see ROADMAP **R.2**). |
| **3.1.5 (ii) Mining** | No on-device mining | We do not mine. |
| **3.1.5 (iii) Exchanges** | Crypto exchange / transmission only where licensed for that region | Umbrella is **not** a licensed exchange. Any swap UI must be framed as user-initiated, non-custodial protocol use — **not** “Umbrella exchange”. Geo-limit if counsel requires (see [GEO_BLOCKING.md](GEO_BLOCKING.md)). |
| **3.1.5 (iv) ICOs / crypto-securities** | Restricted to approved financial institutions | We do **not** offer ICOs, futures, or securities trading. |
| **3.1.5 (v) Task rewards** | No crypto for downloading apps / social spam | We do **not** reward installs or referrals in crypto. |
| **3.1.1 IAP** | Crypto/wallets must not unlock paid app features outside IAP | Umbrella must not sell “premium unlock” via coin transfers. Optional future tips/sponsors stay outside App Store entitlements (GitHub Sponsors). |

### 5.2. Financial model — answers reviewers ask

| Question | Approved answer |
|----------|-----------------|
| Who holds the user’s funds? | **Nobody except the user.** Keys never leave the device. Umbrella cannot freeze, reverse, or recover funds. |
| How does Umbrella make money? | **No cut of transfers.** Optional sponsorship ([GitHub Sponsors](https://github.com/sponsors/kiurakku)). Any future in-app service fee (if added) must be disclosed in UI and still non-custodial — see [LICENSE](LICENSE) §3. |
| Is there fiat on-ramp / card buy? | **Not planned** without proper licensing. Do not advertise “buy crypto with card” in the listing. |
| Are swaps custody? | **No.** If present, swaps are user-signed transactions to third-party protocols; Umbrella never takes possession. |
| Demo account for review? | Provide a **test vault / dry-run path** if requested. **Never** ship a backdoor, hardcoded seed, or bypass of Tor/kill-switch for reviewers. |

### 5.3. Reviewer notes field (paste-ready)

> Umbrella Wallet is non-custodial software. Private keys and recovery phrases are generated and stored only on the user’s device; we cannot access them. We do not provide banking, brokerage, or money-transmitter services. Tor (when enabled) hides the device IP from remote endpoints; it does not make transparent blockchains private. Please see in-app Terms and Privacy Policy links, and the first-run acknowledgement of 18+ / lost-seed risk.

### 5.4. Pre-submit checklist (Apple)

- [ ] Apple Developer membership is an **Organization**, not Individual  
- [ ] Legal entity ready (ROADMAP R.2); seller name matches product  
- [ ] Listing uses only §2 wording; no §3 forbidden claims  
- [ ] Privacy Policy URL live; Terms linked  
- [ ] First-run L.1 / L.3 / L.8 acknowledgement implemented  
- [ ] No mining, no ICO, no task-for-crypto, no unlicensed fiat on-ramp  
- [ ] Counsel reviewed geo targeting ([GEO_BLOCKING.md](GEO_BLOCKING.md))

---

## 6. Google Play — crypto / financial policy

**Official source:** [Cryptocurrency Exchanges and Software Wallets Policy](https://support.google.com/googleplay/android-developer/answer/16329703) (Play Console Help)  
**Umbrella today:** desktop-first; Android is planned. Apply these notes before Play submission.

### 6.1. Non-custodial vs custodial (critical)

Google’s Cryptocurrency Exchanges and Software Wallets policy targets **exchanges and custodial** software wallets in listed jurisdictions. Google has clarified that **non-custodial wallets are out of scope** of that licensing matrix — *provided the app truly never takes custody of keys or funds*.

Umbrella **must remain non-custodial** in architecture and copy. If a future feature adds custodial behaviour, licensing requirements change overnight.

### 6.2. What we still must do on Play

| Requirement | Umbrella action |
|-------------|-----------------|
| Verified Play developer / business verification | Complete before production listing |
| Honest Financial Features declaration | Declare wallet functionality accurately; do **not** claim to be a licensed exchange |
| Privacy Policy URL | Point to published [PRIVACY_POLICY.md](PRIVACY_POLICY.md) (raw GitHub or hosted page) |
| Listing copy | Only §2 claims; age 18+; lost-seed disclaimer |
| Local law | Even when out of scope of Google’s matrix, counsel may still require geo limits — see [GEO_BLOCKING.md](GEO_BLOCKING.md) |
| No KYC-less card on-ramp | Do not ship unlicensed fiat buy |

### 6.3. Reviewer / declaration notes (paste-ready)

> This app is a **non-custodial** cryptocurrency wallet: private keys are created and stored on the user’s device only. The developer never holds user funds or keys and cannot recover a lost recovery phrase. There is no fiat on-ramp and no custodial exchange. Privacy features (local vault encryption, optional Tor) reduce metadata exposure; they do not anonymise public ledger transactions.

### 6.4. Pre-submit checklist (Google Play)

- [ ] Developer account identity verified  
- [ ] App Content → Financial features declared correctly (wallet; non-custodial)  
- [ ] Privacy Policy URL resolves; Terms linked in-app  
- [ ] No “fully private / anonymous” store text  
- [ ] Target countries reviewed with counsel  
- [ ] Data safety form matches [PRIVACY_POLICY.md](PRIVACY_POLICY.md) (no account, no tracking SDKs)

---

## 7. Microsoft Store / Windows packaging notes

**Primary distribution today:** GitHub Releases (side-load). Microsoft Store is **optional** (ROADMAP **R.7**).

### 7.1. Signing

| Path | Requirement | Umbrella plan |
|------|-------------|----------------|
| GitHub `.exe` / portable | Users see SmartScreen until reputation builds | Ship `SHA256SUMS`; document verify steps in [SECURITY.md](SECURITY.md) |
| Trusted SmartScreen / enterprise | **OV or EV** code signing certificate | ROADMAP **R.2 / R.6** — order **≥60 days** before relying on Store trust |
| Microsoft Store listing | Store-compliant package + Microsoft association / signing rules | Only after legal entity + signing; do not list an unsigned “official” Store app |

### 7.2. Listing claims (same honesty bar)

Use §2 wording only. Emphasize:

- Non-custodial; keys on device  
- Not a bank / money transmitter  
- 18+  
- Lost seed = lost funds  

### 7.3. Reviewer note (paste-ready)

> Umbrella Wallet for Windows is non-custodial desktop software. Private keys never leave the device. We do not custody funds. Optional Tor hides IP from remote endpoints; transparent chains remain public ledgers. Verify downloads via SHA-256 sums on the GitHub Release.

### 7.4. Pre-submit checklist (Microsoft)

- [ ] OV/EV certificate issued to the correct legal entity  
- [ ] Installer and portable artifacts signed; checksums published  
- [ ] Privacy Policy + Terms URLs in Store listing  
- [ ] Age / non-custodial first-run still required in-app (L.1–L.8)

---

## 8. Screenshots for reviewers

Include at least one frame where:

- Tor / privacy status shows **limits**, not only a green “private” badge;  
- Settings → Privacy lists servers;  
- First-run disclaimer is visible.

## 9. Support URL for stores

- Community: https://t.me/UmbrellaWallet  
- Source / releases: https://github.com/kiurakku/UmbrellaWallet  
- Security: https://github.com/kiurakku/UmbrellaWallet/security/advisories/new  
- Trademark / brand: [TRADEMARK_POLICY.md](TRADEMARK_POLICY.md)  
- Conduct: [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)  
- Pre-beta gate: [docs/PRE_BETA_CHECKLIST.md](docs/PRE_BETA_CHECKLIST.md)

---

Related: [TERMS_OF_SERVICE.md](TERMS_OF_SERVICE.md) · [PRIVACY_POLICY.md](PRIVACY_POLICY.md) · [GEO_BLOCKING.md](GEO_BLOCKING.md) · [LEGAL/PRIVACY_POLICY_APPSTORE.md](LEGAL/PRIVACY_POLICY_APPSTORE.md) · [docs/ROADMAP.md](docs/ROADMAP.md) §9

---

📖 Back to [Documentation Index](docs/INDEX.md)
