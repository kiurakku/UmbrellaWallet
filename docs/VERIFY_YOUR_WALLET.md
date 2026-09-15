# Verify your wallet

**Status:** outline for roadmap **P1.20** / §10 — expand when self-verify mode ships.

Until then, use what already exists:

| Goal | Where |
|------|--------|
| Verify a GitHub Release download | [SECURITY.md](../SECURITY.md) · checksums on the Release |
| Compare build to source | [BUILD_VERIFY.md](BUILD_VERIFY.md) |
| Threat / residual risk | [THREAT_MODEL.md](../THREAT_MODEL.md) |
| What leaves the device | [PRIVACY.md](../PRIVACY.md) |
| Development order | [WORKFLOW.md](WORKFLOW.md) · [ROADMAP.md](ROADMAP.md) |

## Planned contents (P1.20)

When written fully, this page should cover:

1. Confirm Tor-only: no clearnet from the wallet process (`curl --socks5-hostname`, OS firewall / netstat).  
2. Export an xpub / address → check balance in a **third-party** scanner you chose.  
3. Restore dry-run from seed on a clean machine (ties to **P0.0**).  
4. Cross-check release checksums and (later) signatures / SBOM (R.3 / R.4).

Do **not** send seeds to anyone while “verifying”.

---

📖 Back to [Documentation Index](INDEX.md)
