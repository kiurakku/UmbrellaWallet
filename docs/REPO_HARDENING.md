# Repository hardening status

**Last verified:** 2026-09-15  
**Repo:** [kiurakku/UmbrellaWallet](https://github.com/kiurakku/UmbrellaWallet)

This closes the “missing LICENSE / CODEOWNERS / Dependabot / trademark” audit checklist against
**live** repository state. Items marked ✅ are present and active.

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
| Back-links to INDEX | ✅ | Root + docs markdown |

## Community / GitHub templates

| Item | Status | Location |
|------|--------|----------|
| CODEOWNERS | ✅ | [.github/CODEOWNERS](../.github/CODEOWNERS) |
| Dependabot | ✅ | [.github/dependabot.yml](../.github/dependabot.yml) |
| PR template | ✅ | [.github/pull_request_template.md](../.github/pull_request_template.md) |
| Bug / feature issue templates | ✅ | [.github/ISSUE_TEMPLATE/](../.github/ISSUE_TEMPLATE/) |
| Support | ✅ | [.github/SUPPORT.md](../.github/SUPPORT.md) |
| CONTRIBUTING | ✅ | [CONTRIBUTING.md](../CONTRIBUTING.md) |
| Code of Conduct | ✅ | [CODE_OF_CONDUCT.md](../CODE_OF_CONDUCT.md) |
| Private vuln reporting | ✅ | Advisories enabled + contact link in issue config |
| FUNDING | ✅ | [.github/FUNDING.yml](../.github/FUNDING.yml) |

## Legal / brand

| Item | Status | Location |
|------|--------|----------|
| LICENSE (Free-Use, No-Derivatives) | ✅ | [LICENSE](../LICENSE) — **not** MIT/GPL |
| Plain-English license map | ✅ | [LEGAL/LICENSE_SUMMARY.md](../LEGAL/LICENSE_SUMMARY.md) |
| Trademark policy | ✅ | [TRADEMARK_POLICY.md](../TRADEMARK_POLICY.md) |
| Third-party notices | ✅ | [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md) |
| Terms / Privacy policy | ✅ | root `TERMS_OF_SERVICE.md`, `PRIVACY_POLICY.md` |

## GitHub security features

| Feature | Status | Notes |
|---------|--------|-------|
| Branch ruleset (main): PR + CI, no force-push, no deletion | ✅ | Ruleset `22907748` |
| All-branches: no force-push | ✅ | Ruleset `22907762` |
| Required checks | ✅ | `desktop`, `gitleaks`, `dotnet-vulnerable`, `analyze (csharp)`, `dependency-review` |
| Approvals required | ⚠️ Solo | Count = **0** (single maintainer; still requires a PR) |
| Require signed commits | ⚠️ Off | Optional; enable when signing is routine for all pushes |
| Dependabot security updates | ✅ | Enabled |
| Secret scanning | ✅ | Enabled |
| Push protection | ✅ | Enabled |
| Private vulnerability reporting | ✅ | Enabled |
| CodeQL | ✅ | [.github/workflows/codeql.yml](../.github/workflows/codeql.yml) |
| Gitleaks + dependency-review + nuget vulnerable | ✅ | [.github/workflows/security.yml](../.github/workflows/security.yml) |
| `.gitleaks.toml` | ✅ | Repo root |
| Hardening script | ✅ | [.github/scripts/apply-repo-hardening.ps1](../.github/scripts/apply-repo-hardening.ps1) |

### Deliberate non-goals

- **GPL / MIT LICENSE** — rejected; philosophy is source-available audit, no public forks ([LICENSE](../LICENSE)).
- **“Fork then PR” CONTRIBUTING** — rejected; conflicts with LICENSE. Local clone + audit welcome; publishing forks is not.
- **Fake CODEOWNERS teams** (`@docs-team`) — not created; sole owner `@kiurakku`.

---

## Re-apply settings

```powershell
pwsh .github/scripts/apply-repo-hardening.ps1
```

Requires `gh` authenticated as repo admin.

---

📖 Back to [Documentation Index](INDEX.md)
