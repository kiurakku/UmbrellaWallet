# Development workflow

How to work on Umbrella Wallet **without losing the roadmap or the philosophy**.

Legal / store docs (TOS, Privacy Policy, App Store notes, geo) are **already in the repo** and stay
as sources of truth — you do not need to rewrite them while shipping coins. Focus engineering on
**P0 → P1 → coins**, and touch legal docs only when behaviour or data flows change.

Hub: [INDEX.md](INDEX.md) · Backlog: [ROADMAP.md](ROADMAP.md) · Beta gate: [PRE_BETA_CHECKLIST.md](PRE_BETA_CHECKLIST.md)

---

## Phases (keep control)

| Phase | Focus | Do now? |
|-------|--------|---------|
| **1 — Core** | P0.0 restore proof, P0.6–P0.8 fail-closed, repo hardening | **Yes** |
| **2 — Coins** | Add chains via [adding-a-chain.md](adding-a-chain.md); update ROADMAP + matrix | **Yes** |
| **3 — Stabilize** | P1 duress/UX, then P2 Taproot/PayJoin, optional hardware | After core + coins settle |
| **4 — Release prep** | EV signing (start ≥60 days out), store UI (L.1–L.8), audit | Before public store push |

Repo hardening (CODEOWNERS, Dependabot, rulesets, secret scanning) is **already on** — see
[REPO_HARDENING.md](REPO_HARDENING.md).

---

## When adding a new coin

1. Check [ROADMAP.md](ROADMAP.md) section **N** (tokens / chains). Update status or add a row.
2. Follow the full procedure in [adding-a-chain.md](adding-a-chain.md).
3. Implement: derivation → balance/HD scan → receive → send (if claimed) → fees → history honesty.
4. Tests: balance / send / discovery as applicable (`desktop/tests/…`).
5. Update the capability matrix in [12-coins-and-chains.md](12-coins-and-chains.md).
6. Ship notes in [CHANGELOG.md](../CHANGELOG.md); bump [VERSION](../VERSION) only when cutting a release.
7. Run doc link check (below) if you added markdown links.

Request form for outsiders: [.github/ISSUE_TEMPLATE/coin_request.yml](../.github/ISSUE_TEMPLATE/coin_request.yml).

---

## When changing behaviour

| If you change… | Also update… |
|----------------|--------------|
| Attack surface / residual risk | [THREAT_MODEL.md](../THREAT_MODEL.md), maybe [security-model.md](security-model.md) |
| What leaves the device / who is contacted | [PRIVACY.md](../PRIVACY.md); if store-facing claims change → [PRIVACY_POLICY.md](../PRIVACY_POLICY.md) |
| A roadmap promise | Status cell in [ROADMAP.md](ROADMAP.md) |
| User-visible capability | README / matrix — **never** announce before end-to-end path works ([CONTRIBUTING.md](../CONTRIBUTING.md)) |

Always keep [MANIFESTO.md](../MANIFESTO.md) honesty: say limits next to promises.

---

## When closing a roadmap item

1. Mark the row in [ROADMAP.md](ROADMAP.md) (`✅` / version note).
2. If user-visible → entry in [CHANGELOG.md](../CHANGELOG.md).
3. If it was a beta blocker → tick [PRE_BETA_CHECKLIST.md](PRE_BETA_CHECKLIST.md) section E.

---

## Documentation link check

From repo root:

```bash
bash scripts/check-doc-links.sh
```

Windows (PowerShell):

```powershell
pwsh scripts/check-doc-links.ps1
```

Run before merging doc-heavy PRs, or weekly.

---

## PR expectations (short)

- Prefer small, phase-isolated PRs.
- `dotnet test` / CI must pass (required on `main`).
- No secrets in logs or git history (gitleaks in Security workflow).
- Template: [.github/pull_request_template.md](../.github/pull_request_template.md).

---

📖 Back to [Documentation Index](INDEX.md)
