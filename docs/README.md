# Umbrella Wallet — documentation

Umbrella is a **desktop-only**, non-custodial crypto wallet (.NET 8 + Avalonia, Windows & Linux;
Android planned). There is **no web app and no backend server** — the earlier web product was
discontinued and its docs are archived (see below). These pages describe the shipped desktop wallet.

## Current documents

| File | What's inside |
|------|----------------|
| [`../desktop/README.md`](../desktop/README.md) | The desktop app: what it does, network support, security model, build & run, honest limitations |
| [`04-desktop.md`](./04-desktop.md) | Desktop architecture: project structure, vault, multi-chain send, bundled Tor & Monero, packaging |
| [`07-financial.md`](./07-financial.md) | Network fees, TRC-20 costs, user disclosure |
| [`TOR.md`](./TOR.md) | How Tor is bundled and routed |
| [`CLAUDE_IMPLEMENTATION_ROADMAP_UK.md`](./CLAUDE_IMPLEMENTATION_ROADMAP_UK.md) | **Source of truth** for product direction and the current work plan (Ukrainian) |
| [`telegram-news-uk.md`](./telegram-news-uk.md) | Telegram channel news, Ukrainian |
| [`../THIRD_PARTY_NOTICES.md`](../THIRD_PARTY_NOTICES.md) | Pinned + hash-verified Tor and Monero binaries |
| [`../CHANGELOG.md`](../CHANGELOG.md) · [`../VERSION`](../VERSION) | Release history and current version |

## Planned documents

The roadmap (§5.2) calls for a fuller desktop set that does not yet exist: product scope,
desktop architecture, security & privacy, supported networks, build/release/verify, backup &
recovery, and troubleshooting. Until those land, the pages above are authoritative and the in-app
per-asset support badge is the source of truth before sending or receiving.

## Archived — do not use for the current product

The `archive/legacy-web-2026-07/` folder holds documentation for the **discontinued** React /
NestJS / Prisma / P2P web application. It is kept only as historical record; anything there about a
backend, API, IndexedDB, JWT, KYC, Prisma, Docker, or Vercel/Fly.io does **not** apply to the
shipped desktop wallet. See [`archive/legacy-web-2026-07/README.md`](./archive/legacy-web-2026-07/README.md).
