# Contributing to Umbrella Wallet

Thank you. Umbrella is MIT-licensed source for a non-custodial desktop wallet by **the fear**
(thefear078). Contributions that improve fund safety, privacy honesty, and auditability are welcome.

Please follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Licence and brand

- **Code:** [MIT](LICENSE) — you may fork, patch, and redistribute under MIT terms.
- **Brand:** [TRADEMARK_POLICY.md](TRADEMARK_POLICY.md) — do **not** ship a look-alike named
  “Umbrella Wallet” / “the fear”. Rebrand derivatives; see [docs/forking.md](docs/forking.md).
- Background: [LICENSE_CHANGE.md](LICENSE_CHANGE.md).

## What we welcome

- **Bug reports** — [GitHub Issues](https://github.com/thefear078/UmbrellaWallet/issues) with version, OS, steps.
- **Security reports** — privately via [GitHub Security Advisories](https://github.com/thefear078/UmbrellaWallet/security/advisories/new) only.
- **Pull requests** — small, tested, phase-aligned with [docs/ROADMAP.md](docs/ROADMAP.md) /
  [docs/WORKFLOW.md](docs/WORKFLOW.md). Prefer discussing large features in an issue first.
- **Coin proposals** — use the “Add a new coin” issue template.
- **Sponsorship** — [GitHub Sponsors](https://github.com/sponsors/thefear078).

## PR expectations

1. `dotnet test` / CI green.
2. No secrets in logs or git history.
3. New network calls go through the shared Tor-aware HTTP path.
4. Do not announce a capability in README/CHANGELOG/UI before the end-to-end path works.
5. Keep MANIFESTO honesty: state limits next to promises (especially Tor vs transparent chains).
6. Preserve publisher attribution in assembly metadata / About (see `PublisherAttributionTests`).

## What we push back on

- Silent fee recipients, RNG weakening, or Tor kill-switch bypasses.
- Marketing claims that contradict [THREAT_MODEL.md](THREAT_MODEL.md) / [APP_STORE_NOTES.md](APP_STORE_NOTES.md).
- Drive-by renames that strip “the fear” attribution without a full honest rebrand.

## Questions

[t.me/UmbrellaWallet](https://t.me/UmbrellaWallet) · [CONTACT.md](CONTACT.md)

---

📖 Back to [Documentation Index](docs/INDEX.md)
