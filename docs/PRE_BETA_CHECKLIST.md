# Pre-beta checklist

**Last verified:** 2026-09-20 · Wallet [4.7.0](../VERSION)  
**Repo:** [kiurakku/UmbrellaWallet](https://github.com/kiurakku/UmbrellaWallet)

This replaces stale audits that still claim “missing LICENSE / CODEOWNERS / CoC / Dependabot”.
Those items are **done**. What remains before a **public beta** is mostly **code** (P0 fund-safety)
and **process** (EV certificate order) — not more markdown stubs.

---

## Phases (product first, stores later)

Legal and store markdown is **already written** — leave it alone while you ship coins, unless behaviour
changes. Engineering order:

1. **Core** — P0.0 + P0.6–P0.8 ([ROADMAP.md](ROADMAP.md))  
2. **Coins** — [WORKFLOW.md](WORKFLOW.md) + [adding-a-chain.md](adding-a-chain.md)  
3. **Stabilize** — P1 / P2 / hardware  
4. **Release prep** — EV cert (≥60 days), L.1–L.8 UI, audit  

Day-to-day: [WORKFLOW.md](WORKFLOW.md). Doc links: `pwsh scripts/check-doc-links.ps1`.

---

## A. Documentation spider — DONE

- [x] [docs/INDEX.md](INDEX.md) hub + back-links on root/docs pages
- [x] [README.md](../README.md) navigation table + badges (CI, CodeQL, Security, release, **Free-Use No-Derivatives**)
- [x] [getting-started.md](getting-started.md), [ROADMAP.md](ROADMAP.md), [REPO_HARDENING.md](REPO_HARDENING.md)
- [x] [CONTACT.md](../CONTACT.md) — real channels (GitHub, Telegram, TikTok, Reddit, Advisories). **No** fake `legal@…example` addresses.

## B. Legal / brand — DONE

| Item | Status | Note |
|------|--------|------|
| [LICENSE](../LICENSE) | ✅ | **Free-Use, No-Derivatives** (source-available). **Not** GPL/MIT by design. |
| [LEGAL/LICENSE_SUMMARY.md](../LEGAL/LICENSE_SUMMARY.md) | ✅ | Plain English |
| [TRADEMARK_POLICY.md](../TRADEMARK_POLICY.md) | ✅ | Brand protection |
| [CODE_OF_CONDUCT.md](../CODE_OF_CONDUCT.md) | ✅ | Community / issues / Telegram |
| [TERMS_OF_SERVICE.md](../TERMS_OF_SERVICE.md) | ✅ | Non-custodial, 18+, liability |
| [PRIVACY_POLICY.md](../PRIVACY_POLICY.md) | ✅ | Store formal policy |
| [APP_STORE_NOTES.md](../APP_STORE_NOTES.md) | ✅ | §5 Apple 3.1.5, §6 Play, §7 Microsoft below |
| [GEO_BLOCKING.md](../GEO_BLOCKING.md) | ✅ | Counsel review still required for enforcement |
| [AUDIT_STATUS.md](../AUDIT_STATUS.md) | ✅ | Explicitly **not audited** |
| [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md) | ✅ | Tor / Monero / NuGet |
| [LEGAL/](../LEGAL/README.md) | ✅ | Pointers + App Store privacy map |
| [SECURITY/](../SECURITY/README.md) | ✅ | Coordinated disclosure + history log |

## C. GitHub hardening — DONE

| Item | Status |
|------|--------|
| [.github/CODEOWNERS](../.github/CODEOWNERS) | ✅ |
| [.github/dependabot.yml](../.github/dependabot.yml) | ✅ |
| [.github/pull_request_template.md](../.github/pull_request_template.md) | ✅ |
| [.github/ISSUE_TEMPLATE/](../.github/ISSUE_TEMPLATE/) (bug, feature, security) | ✅ |
| [.github/SUPPORT.md](../.github/SUPPORT.md) | ✅ |
| [.gitleaks.toml](../.gitleaks.toml) | ✅ |
| Branch rulesets (PR + CI, no force-push/delete) | ✅ |
| Secret scanning + push protection | ✅ |
| Dependabot security updates | ✅ |
| CodeQL + Security workflows (gitleaks, dependency-review, nuget vulnerable) | ✅ |
| Private vulnerability reporting | ✅ |

Live status detail: [REPO_HARDENING.md](REPO_HARDENING.md).

## D. Store readiness — docs DONE / UI + org OPEN

| Platform | Docs | Remaining |
|----------|------|-----------|
| GitHub side-load (primary) | ✅ | Keep shipping checksums |
| Apple App Store | ✅ notes | Org Apple account + L.1–L.8 **UI** + legal entity (R.2) |
| Google Play | ✅ notes | Business verification + L.1–L.8 **UI** |
| Microsoft Store | ✅ notes (§7) | EV/OV signing (R.2/R.6); optional Store listing (R.7) |
| Linux packages | ✅ notes | PGP package signing (L.6 / R.3) |

## E. Code gates before **public** beta — OPEN

Do **not** call a public beta “fund-safe” until these are green:

| ID | Task | Status |
|----|------|--------|
| **P0.0** | Full restore proof (seed-only recover past gap) | ✅ `RestoreFromSeedProofTests` — finds **and spends** funds on issued address #15 after the local state is deleted |
| **P0.6** | Fail-closed balance (no fake `0.0000` on network error) | ✅ unread balances read “—” with a reason; the total says what it is missing |
| **P0.7** | Send-path transport gate (Tor settings enforced) | ✅ refused at Review **and** Confirm when the live route is not the chosen one |
| **P0.8** | Network isolation CI (Tor-only cannot clearnet) | ✅ own CI job; a loopback listener proves no socket is opened |
| L.1–L.3, L.8 | First-run disclaimer + 18+ + ToS/Privacy accept in UI | ✅ shipped — gates create/import/unlock, versioned acceptance, 6 languages |
| Tests | `dotnet test` green on CI | ✅ on `main` PRs |

Track in [ROADMAP.md](ROADMAP.md) §3.

## F. Process (human) — OPEN

| Task | Owner | Note |
|------|-------|------|
| Order **OV/EV code signing** (R.2 / R.6) | Founder | Start **≥60 days** before Windows Store / SmartScreen goal |
| Confirm legal entity if submitting to Apple as org | Founder | Required by Apple 3.1.5 (i) |
| External audit (R.5) | Later | After beta stability — keep [AUDIT_STATUS.md](../AUDIT_STATUS.md) honest |

---

## Verdict

| Layer | Ready for public beta? |
|-------|-------------------------|
| Docs / legal / GitHub hardening | **Yes** |
| Philosophy consistency | **Yes** |
| Fund-safety P0 code + first-run UI | **Yes** for P0.0 / P0.6–P0.8 and the first-run UI; P0.1–P0.5 (supply-chain pins, checksum CI, capability matrix) remain |
| Store submission (Apple/Play/MS) | **Not yet** — the in-app disclaimers are done; a legal entity and code signing are not |

**Private / friends beta** on GitHub Releases is fine **today** if testers accept experimental risk and never put more than they can lose — and if CONTACT / SECURITY channels are used for bugs.

---

📖 Back to [Documentation Index](INDEX.md) · Hardening: [REPO_HARDENING.md](REPO_HARDENING.md)
