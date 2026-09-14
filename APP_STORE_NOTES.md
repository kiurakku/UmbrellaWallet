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

## 5. Apple App Store notes (§3.x payment / finance)

- Emphasize **user-controlled keys on device**; no Umbrella-hosted balances.  
- No in-app fiat on-ramp without proper licensing (not planned — see ROADMAP).  
- Swaps (if shown) are non-custodial protocol interactions, not Umbrella custody.  
- Provide a demo/test build path for reviewers if requested; never ship a backdoor.

## 6. Google Play — crypto / financial policy

- Publisher is a verified developer account (business verification as required).  
- Listing must state non-custodial and no recovery of keys.  
- No KYC-less card on-ramp inside the app without licences.  
- Link Privacy Policy URL to the raw or pages-hosted [PRIVACY_POLICY.md](PRIVACY_POLICY.md).

## 7. Screenshots for reviewers

Include at least one frame where:

- Tor / privacy status shows **limits**, not only a green “private” badge;  
- Settings → Privacy lists servers;  
- First-run disclaimer is visible.

## 8. Support URL for stores

- Community: https://t.me/UmbrellaWallet  
- Source / releases: https://github.com/kiurakku/UmbrellaWallet  
- Security: https://github.com/kiurakku/UmbrellaWallet/security/advisories/new
