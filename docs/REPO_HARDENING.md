# Repository hardening status

**Last verified:** 2026-09-15  
**Repo:** [thefear078/UmbrellaWallet](https://github.com/thefear078/UmbrellaWallet)

Closes stale audits that claim “missing LICENSE / CODEOWNERS / Dependabot / trademark”. Those items
are **present**. Code licence is **MIT**; brand protection is [TRADEMARK_POLICY.md](../TRADEMARK_POLICY.md).

---

## P0 / P1 / P2 checklist (all terms)

### P0 — done

- [x] `LICENSE` present (**MIT** + trademark notice — see LICENSE_CHANGE.md)
- [x] `.github/CODEOWNERS`
- [x] `.github/dependabot.yml` (NuGet `/desktop` + GitHub Actions; no Docker — none in repo)
- [x] Dependabot security updates enabled
- [x] Secret scanning + push protection enabled
- [x] CodeQL workflow active
- [x] Private vulnerability reporting enabled

### P1 — done

- [x] Branch rulesets: PR required, CI required, no force-push / deletion
- [x] `docs/INDEX.md` + getting-started + `_index.yml`
- [x] `CONTRIBUTING.md` (no “fork then PR” — conflicts with LICENSE)
- [x] `CODE_OF_CONDUCT.md` ← **exists at repo root** (audits that say “missing” are stale)
- [x] `TRADEMARK_POLICY.md` ← **exists at repo root** (+ `LEGAL/` pointer)
- [x] PR + issue templates (+ security redirect)
- [x] `APP_STORE_NOTES.md` §5–§7 filled (Apple 3.1.5 + Google Play + Microsoft Store)
- [x] ROADMAP §8 link-check (all sources of truth resolve)
- [x] [PRE_BETA_CHECKLIST.md](PRE_BETA_CHECKLIST.md) — honest split: docs/security ✅ vs P0 code ⏳

### P2 — done

- [x] `THIRD_PARTY_NOTICES.md` (+ `LEGAL/` pointer)
- [x] `SECURITY/VULNERABILITY_HISTORY.md`
- [x] `SECURITY/COORDINATED_DISCLOSURE.md`
- [x] `LEGAL/PRIVACY_POLICY_APPSTORE.md`
- [x] README badges (CI, CodeQL, Security, release, **MIT** license)
- [x] `dependency-review` required on `main`
- [x] Hardening script: [.github/scripts/apply-repo-hardening.ps1](../.github/scripts/apply-repo-hardening.ps1)

---

## Documentation spider

| Item | Status | Location |
|------|--------|----------|
| Central docs hub | ✅ | [docs/INDEX.md](INDEX.md) |
| Getting started | ✅ | [getting-started.md](getting-started.md) |
| Machine-readable map | ✅ | [_index.yml](_index.yml) |
| README navigation hub | ✅ | [README.md](../README.md) |
| ROADMAP (English) | ✅ | [ROADMAP.md](ROADMAP.md) |
| Threat model + TOC | ✅ | [THREAT_MODEL.md](../THREAT_MODEL.md) |

## Community / GitHub templates

| Item | Status | Location |
|------|--------|----------|
| CODEOWNERS | ✅ | [.github/CODEOWNERS](../.github/CODEOWNERS) |
| Dependabot | ✅ | [.github/dependabot.yml](../.github/dependabot.yml) |
| PR template | ✅ | [.github/pull_request_template.md](../.github/pull_request_template.md) |
| Bug / feature / security templates | ✅ | [.github/ISSUE_TEMPLATE/](../.github/ISSUE_TEMPLATE/) |
| Support | ✅ | [.github/SUPPORT.md](../.github/SUPPORT.md) |
| CONTRIBUTING | ✅ | [CONTRIBUTING.md](../CONTRIBUTING.md) |
| Code of Conduct | ✅ | [CODE_OF_CONDUCT.md](../CODE_OF_CONDUCT.md) |
| FUNDING | ✅ | [.github/FUNDING.yml](../.github/FUNDING.yml) |

## Legal / brand

| Item | Status | Location |
|------|--------|----------|
| LICENSE | ✅ | [LICENSE](../LICENSE) |
| License summary | ✅ | [LEGAL/LICENSE_SUMMARY.md](../LEGAL/LICENSE_SUMMARY.md) |
| Trademark | ✅ | [TRADEMARK_POLICY.md](../TRADEMARK_POLICY.md) · [LEGAL/TRADEMARK_POLICY.md](../LEGAL/TRADEMARK_POLICY.md) |
| Third-party notices | ✅ | [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md) |
| App Store privacy pointer | ✅ | [LEGAL/PRIVACY_POLICY_APPSTORE.md](../LEGAL/PRIVACY_POLICY_APPSTORE.md) |
| Coordinated disclosure | ✅ | [SECURITY/COORDINATED_DISCLOSURE.md](../SECURITY/COORDINATED_DISCLOSURE.md) |
| Vulnerability history | ✅ | [SECURITY/VULNERABILITY_HISTORY.md](../SECURITY/VULNERABILITY_HISTORY.md) |

## GitHub security features

| Feature | Status | Notes |
|---------|--------|-------|
| Ruleset main: PR + CI, no force-push/delete | ✅ | id `22907748` |
| Ruleset all branches: no force-push | ✅ | id `22907762` |
| Required checks | ✅ | `desktop`, `gitleaks`, `dotnet-vulnerable`, `dependency-review`, `analyze (csharp)` |
| Approvals required | ⚠️ Solo | Count = **0** (PR still required; self-approve blocked if count≥1) |
| Signed commits required | ⚠️ Off | Enable when signing is routine |
| Dependabot / secret scanning / push protection | ✅ | Enabled |
| CodeQL + Security workflows | ✅ | Active |

### Deliberate non-goals

- **GPL / MIT LICENSE** — rejected.
- **“Fork then PR” CONTRIBUTING** — rejected.
- **Fake CODEOWNERS teams** — not created; sole owner `@thefear078`.
- **Dependabot Docker** — no Dockerfile in this repo.

---

## Re-apply settings

```powershell
pwsh .github/scripts/apply-repo-hardening.ps1
```

Requires `gh` authenticated as repo admin.

---

📖 Back to [Documentation Index](INDEX.md)
