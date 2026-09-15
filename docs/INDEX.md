# Umbrella documentation

This is the **central index** for all official Umbrella Wallet documents.

Umbrella is a **desktop-only**, non-custodial crypto wallet (.NET 8 + Avalonia; Windows and Linux;
Android planned). There is no web app and no backend that knows who you are.

---

## Which document should I read?

| If you want to… | Start here |
|---|---|
| Install and use the wallet | [Getting started](getting-started.md) |
| Understand the philosophy | [MANIFESTO.md](../MANIFESTO.md) |
| Know what is defended (and what is not) | [THREAT_MODEL.md](../THREAT_MODEL.md) |
| See what data leaves the device | [PRIVACY.md](../PRIVACY.md) |
| Report a vulnerability | [SECURITY.md](../SECURITY.md) |
| Build from source | [building.md](building.md) |
| Add a new coin | [adding-a-chain.md](adding-a-chain.md) |
| See what is left to build | [ROADMAP.md](ROADMAP.md) |
| Contact / support | [CONTACT.md](../CONTACT.md) · [SUPPORT](../.github/SUPPORT.md) |

---

## For users

| Document | Description |
|---|---|
| [Getting started](getting-started.md) | Download, verify, first wallet, Tor, first send |
| [Troubleshooting](troubleshooting.md) | Slow balances, Monero sync, failed sends, spam tokens |
| [SECURITY.md](../SECURITY.md) | Verify downloads, report vulnerabilities |
| [PRIVACY.md](../PRIVACY.md) | What leaves your device, when, and why |
| [PRIVACY_POLICY.md](../PRIVACY_POLICY.md) | Formal privacy policy (store / public) |
| [TERMS_OF_SERVICE.md](../TERMS_OF_SERVICE.md) | Terms of use |
| [CHANGELOG.md](../CHANGELOG.md) | What changed in each release |

## For developers

| Document | Description |
|---|---|
| [architecture.md](architecture.md) | Layers, data flow, Core vs Infrastructure |
| [building.md](building.md) | Compile, test, package installers |
| [testing.md](testing.md) | What the suite protects |
| [adding-a-chain.md](adding-a-chain.md) | Full procedure for a new cryptocurrency |
| [12-coins-and-chains.md](12-coins-and-chains.md) | Capability matrix for every chain |
| [localization.md](localization.md) | Languages and parity tests |
| [theming.md](theming.md) | Themes and contrast rules |
| [forking.md](forking.md) | Licence limits and rebrand checklist |
| [CONTRIBUTING.md](../CONTRIBUTING.md) | How to contribute |
| [ROADMAP.md](ROADMAP.md) | Single backlog (English) |

## For security researchers

| Document | Description |
|---|---|
| [THREAT_MODEL.md](../THREAT_MODEL.md) | Attack vectors, residual risk |
| [security-model.md](security-model.md) | Crypto choices and explicit non-goals |
| [SECURITY.md](../SECURITY.md) | Private reporting process |
| [AUDIT_STATUS.md](../AUDIT_STATUS.md) | External audit: none yet |
| [BUILD_VERIFY.md](BUILD_VERIFY.md) | Verify a release against source |
| [TOR.md](TOR.md) | Bundled Tor, kill-switch, circuits |
| [REPO_HARDENING.md](REPO_HARDENING.md) | GitHub security + legal checklist (live status) |
| [PRE_BETA_CHECKLIST.md](PRE_BETA_CHECKLIST.md) | What is done vs what blocks a public beta |
| [SECURITY/](../SECURITY/README.md) | Coordinated disclosure + vulnerability history |
| [LEGAL/](../LEGAL/README.md) | Legal map + App Store privacy pointer |


## Philosophy & legal (repository root)

| Document | Purpose |
|---|---|
| [MANIFESTO.md](../MANIFESTO.md) | The rules the code is held to |
| [LICENSE](../LICENSE) | Free-use, no-derivatives (source-available) |
| [LICENSE_SUMMARY.md](../LEGAL/LICENSE_SUMMARY.md) | Plain-English license map |
| [TRADEMARK_POLICY.md](../TRADEMARK_POLICY.md) | Name and logo protection |
| [CODE_OF_CONDUCT.md](../CODE_OF_CONDUCT.md) | Community standards |
| [GEO_BLOCKING.md](../GEO_BLOCKING.md) | Restricted jurisdictions (store policy) |
| [APP_STORE_NOTES.md](../APP_STORE_NOTES.md) | Approved store wording |
| [CONTACT.md](../CONTACT.md) | GitHub, Telegram, TikTok, Reddit |
| [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md) | Tor, Monero, and other components |

## Machine-readable map

See [`_index.yml`](_index.yml) for a structured index used by tooling and humans.

## Repository layout (docs-related)

```
UmbrellaWallet/
├── README.md                 ← product overview (start here)
├── MANIFESTO.md              ← core philosophy
├── SECURITY.md · PRIVACY.md · THREAT_MODEL.md
├── TERMS_OF_SERVICE.md · PRIVACY_POLICY.md · TRADEMARK_POLICY.md
├── docs/
│   ├── INDEX.md              ← you are here
│   ├── getting-started.md
│   ├── ROADMAP.md
│   ├── architecture.md · building.md · …
│   └── archive/              ← discontinued web product (history only)
└── .github/SUPPORT.md
```

---

*Last updated: 2026-09-15 · Wallet version [4.7.0](../VERSION)*

📖 This page is the documentation hub. Product overview: [README.md](../README.md).
