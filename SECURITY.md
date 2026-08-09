# Security Policy

## Supported versions

| Version | Supported |
|---------|-----------|
| 3.0.x   | ✅        |
| 2.4.x   | ✅ (critical fixes only) |
| < 2.4   | ❌        |

## Reporting a vulnerability

**Do not** open a public GitHub issue for security bugs.

1. Use [GitHub Private Vulnerability Reporting](https://github.com/kiurakku/umbrella-wallet/security/advisories/new) on this repository, **or**
2. Contact the maintainer through GitHub (profile → contact).

Include:

- Affected version (desktop build or commit hash)
- Platform (Windows / Linux)
- Steps to reproduce
- Impact assessment (funds at risk, key leakage, remote code execution, etc.)

## What we protect

Umbrella Wallet is **non-custodial**. Reports involving theft of user funds through:

- Incorrect address derivation or signing
- Vault encryption bypass
- Seed phrase exposure through logs, backups, or network
- Tor / privacy mode leaks

are treated as **critical** and prioritised.

## Safe harbour

Good-faith security research on your own wallet instance is welcome. Do not test against other users' devices or mainnet funds you do not own.

## Response targets

| Severity | Target response |
|----------|-----------------|
| Critical (fund loss / key leak) | 48 hours |
| High | 7 days |
| Medium / Low | best effort |

We do not offer a paid bug-bounty programme at this time.

## Repository hardening

This repository uses:

- Protected `main` branch (pull request + required status checks + CODEOWNER review)
- Global rules against force-push and branch deletion
- CodeQL, Gitleaks, dependency review, and NuGet vulnerability scanning in CI
- Dependabot security updates for npm, NuGet, and GitHub Actions
