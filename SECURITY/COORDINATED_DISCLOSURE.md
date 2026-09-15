# Coordinated disclosure

How Umbrella Wallet handles privately reported vulnerabilities.

**Canonical user-facing policy:** [../SECURITY.md](../SECURITY.md)

## Channels

| Channel | Use |
|---------|-----|
| [GitHub Security Advisories](https://github.com/kiurakku/UmbrellaWallet/security/advisories/new) | **Preferred** — private by default |
| Public GitHub Issues | **Forbidden** for security bugs |

## Timeline (targets)

| Step | Target |
|------|--------|
| Acknowledge receipt | ≤ **72 hours** |
| Assessment + fix timeline | ≤ **7 days** |
| Fix released | Coordinated with reporter |
| CVE | Requested when severity ≥ High and appropriate |
| Public credit | Unless the reporter opts out |

## Scope

In scope: vault crypto, key derivation, Tor/kill-switch bypasses, send-path integrity, release
supply-chain issues in this repository’s published artifacts.

Out of scope (examples): social engineering of end users; compromised user devices (malware); third-
party explorers the user chose; transparent-chain analysis that any observer can do.

## After fix

1. Ship a release with checksums (`SHA256SUMS-<version>.txt`).
2. Publish / update the advisory.
3. Add a row to [VULNERABILITY_HISTORY.md](VULNERABILITY_HISTORY.md) and `SECURITY.md`.

Bug bounty: **currently unfunded** — see [SECURITY.md § Bug bounty](../SECURITY.md#bug-bounty).

---

📖 Back to [Documentation Index](../docs/INDEX.md)
