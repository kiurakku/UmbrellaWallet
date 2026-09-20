# Security Policy

Umbrella is a self-custody wallet. A bug here can cost somebody everything they hold, so this page is
specific rather than reassuring.

The technical detail — threat model, cryptography, what is deliberately not protected — is in
**[docs/security-model.md](docs/security-model.md)**.

## Reporting a vulnerability

**Report privately:**
**[github.com/thefear078/UmbrellaWallet/security/advisories/new](https://github.com/thefear078/UmbrellaWallet/security/advisories/new)**

Please do **not** open a public issue for anything that could put funds at risk. A public report on a
wallet is a race between the fix and whoever reads it first.

If GitHub advisories are not available to you, contact [t.me/UmbrellaWallet](https://t.me/UmbrellaWallet)
and ask for a private channel. Do not put details in a public chat.

### What to include

- What you found, and why it matters.
- Steps to reproduce, or a proof of concept.
- Affected version — the installer version, or the commit if you built it.
- Your OS.
- Whether you believe it is already being exploited.

### What to expect

| | |
|---|---|
| Acknowledgement | within **72 hours** |
| Initial assessment | within **7 days** |
| Fix for a critical issue | as fast as it can be done correctly — days, not weeks |
| Credit | yes, by name or handle, unless you prefer otherwise |
| Disclosure | coordinated. We will agree a date with you; we will not sit on it indefinitely. |

This is a small project. The timelines above are commitments, not an SLA backed by a team — if
something slips, you will hear why rather than nothing.

## Scope

### In scope

Anything that could lose, expose, or lock up funds:

- Key derivation producing a wrong or unspendable address
- Vault encryption weaknesses — KDF parameters, cipher misuse, key handling in memory
- Transaction construction: wrong recipient, wrong amount, wrong change, wrong fee
- Signing flaws, nonce reuse, key material leaking anywhere
- Privacy leaks: anything that bypasses `PublicHttp`, defeats the Tor kill-switch, or sends data we do
  not document as leaving the device
- Seed or key material reaching disk, logs, the clipboard, or a crash dump unintentionally
- Screen-capture protection being bypassed on seed/key screens
- Supply-chain problems: a dependency, the build scripts, or the release pipeline
- Spoofing the wallet's identity or update path

### Out of scope

- Attacks requiring malware **already running as the user**. No desktop wallet defends against this,
  and we say so in [docs/security-model.md](docs/security-model.md) rather than implying otherwise.
- Physical access to an **unlocked** machine.
- The user voluntarily giving away their 24 words (phishing sites, fake support). We defend by
  detection and education — spam-token folding, address-poisoning warnings — but a user who types
  their seed into a website cannot be saved by the wallet.
- Third-party explorer or RPC outages and rate limits.
- The fact that Bitcoin, Ethereum and other transparent chains are public ledgers. That is the chain's
  design, not our bug. Privacy Radar tells you what a send reveals.
- Zcash shielded addresses. Not implemented, and listed as transparent-only in the wallet.
- Missing features, UI preferences, and "you should use X instead".

## Bug bounty

There is no funded bounty programme yet. Being straight about that rather than implying one:

- **Critical** findings — anything that lets an attacker take funds or extract a seed — will be
  rewarded from project funds, and credited publicly.
- All valid findings get credit and a fix.
- If you would like to see a funded programme, [sponsorship](https://github.com/sponsors/thefear078)
  is what would pay for it.

## Supported versions

| Version | Supported |
|---|---|
| 4.6.x | ✅ |
| 4.5.x | ⚠️ critical fixes only |
| < 4.5 | ❌ |

Always run the latest release. The wallet tells you when one is available; it does not auto-update,
because a wallet that can silently replace its own binary is a wallet with a very attractive update
channel.

## Verifying what you run

Every release ships `SHA256SUMS-<version>.txt`. Check your download before running it:

```bash
sha256sum -c SHA256SUMS-4.6.0.txt
```

```powershell
Get-FileHash .\UmbrellaWallet-Setup-4.6.0.exe -Algorithm SHA256
```

Better still, [build it yourself](docs/building.md). Reproducible builds with published attestations
are on the roadmap and are not done yet — this page will not claim them until they are.

## What we do on our side

| | |
|---|---|
| Static analysis | CodeQL on every push |
| Secret scanning | enabled, with push protection |
| Dependency alerts | Dependabot, with security updates |
| Vulnerable packages | build fails on a known-vulnerable dependency |
| Secrets in history | gitleaks in CI |
| Branch protection | `main` requires review + all checks green; no force-push, no deletion |
| Tests | 526 offline tests, required before merge |

No external audit has been performed. Status and future links live in
**[AUDIT_STATUS.md](AUDIT_STATUS.md)**. When an audit exists, it will be linked there and here with the
full report, including findings — not a badge alone.

## Security update process

When a vulnerability is confirmed:

1. **Within 72 hours:** acknowledge receipt.  
2. **Within 7 days:** assessment and a fix timeline.  
3. **Fix released** under coordinated disclosure.  
4. **CVE** requested when severity is High or Critical and appropriate.  
5. **Credit** publicly unless the reporter asks otherwise.

## Release integrity

Each release should include:

| Asset | Status |
|---|---|
| `SHA256SUMS-<version>.txt` | ✅ shipped |
| Detached GPG / Sigstore signature on the sums file | ⏳ roadmap R.3 |
| SBOM (CycloneDX / SPDX) | ⏳ roadmap R.4 |

## Historical vulnerabilities

Full log: **[SECURITY/VULNERABILITY_HISTORY.md](SECURITY/VULNERABILITY_HISTORY.md)**.  
Process: **[SECURITY/COORDINATED_DISCLOSURE.md](SECURITY/COORDINATED_DISCLOSURE.md)**.

| Date | ID | Severity | Fixed in |
|---|---|---|---|
| — | — | — | None disclosed yet |

When vulnerabilities are disclosed, they will be listed here and in `SECURITY/VULNERABILITY_HISTORY.md`
with links to advisories.

## Bug bounty

**Currently unfunded.** There is no paid bounty programme yet. Responsible disclosure is still
welcome under the process above; researchers will be credited publicly unless they ask otherwise.

When funding exists, the intended structure is:

| Severity | Example | Target reward | Acknowledgement |
|---|---|---|---|
| Critical | Loss or theft of funds / seed | TBD (aim: meaningful fixed range) | ≤ 72 hours |
| High | Privacy bypass of kill-switch / Tor-only, vault crypto flaw | TBD | ≤ 72 hours |
| Medium | Non-fund UX / honesty bugs that mislead about risk | Credit | ≤ 7 days |

This table is a **promise of structure**, not a funded programme. It will not be described as “active
bounty” until rewards are actually available.

---

📖 Documentation hub: [docs/INDEX.md](docs/INDEX.md) · Support: [.github/SUPPORT.md](.github/SUPPORT.md)

## Our promises

These are the things that would make everything else on this page untrue, so they are stated plainly:

- Your seed and keys never leave your device. There is no server that could receive them.
- No telemetry, no analytics, no crash reporting, no advertising. Ever.
- Umbrella takes **no cut** of your transfers.
- We cannot freeze, seize, or recover your funds — and neither can anyone else holding this software.
- If we ever find that one of these was broken, we will say so publicly, including how long it was
  broken and what we know about the impact.
