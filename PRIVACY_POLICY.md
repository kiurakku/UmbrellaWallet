# Privacy Policy (store / public distribution)

**Last updated:** 2026-09-15  
**Applies to:** Umbrella Wallet app listings and public downloads  
**Publisher:** the fear (thefear078)

This is the **formal privacy policy** for app stores and distributors.  
For the honest technical breakdown (what each server learns), see **[PRIVACY.md](PRIVACY.md)** — that document is source-of-truth for engineers and users who want detail. This page is the short legal form stores expect.

---

## 1. Who we are

Umbrella Wallet is non-custodial desktop software. There is **no Umbrella account server**, no customer database of users, and no analytics backend operated by us.

## 2. Data we collect

**We collect none on our servers.**

| Category | Collected by Umbrella servers? |
|---|---|
| Name, email, phone | No — never asked |
| Account / KYC data | No — no accounts |
| Analytics / crash reports | No — build fails if those SDKs appear |
| Advertising IDs | No |
| Seed phrases / private keys | Never leave your device; never sent to us |

## 3. Data processed on your device

The app stores wallet data **locally** (encrypted vault, settings, optional notes). See [PRIVACY.md — Data on disk](PRIVACY.md#data-on-disk).

## 4. Data shared with third parties (by the app on your machine)

A wallet must query a blockchain. When you use balances, history, or send, **your device** may send:

| What | To whom | Why |
|---|---|---|
| Public addresses | Block explorers / RPC nodes (defaults or ones you choose) | Read balances / history; broadcast signed txs |
| Signed transactions | Same | Publish to the network |
| Coin symbols (not your identity) | Price providers (e.g. CoinGecko / exchange public APIs) | Display fiat values |
| Optional update check | GitHub | Know if a newer release exists |

**Tor** (if enabled) hides your IP from those servers; it does **not** un-send an address on a public chain.

Bundled components (Tor, `monero-wallet-rpc`) run locally under their own licences; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## 5. Data retention

- **Our servers:** we retain nothing about you — there is no user database.  
- **Your device:** data remains until you delete the app and its data directory, or use in-app wipe (Danger zone).  
  - Windows (typical): `%APPDATA%\UmbrellaWallet`  
  - Linux (typical): `~/.config/UmbrellaWallet` or `data/` beside a portable build  
- **Encrypted backups** you export persist until **you** delete those files.

## 6. Children’s privacy

Not directed at children under 13. Use requires age of majority (see [TERMS_OF_SERVICE.md](TERMS_OF_SERVICE.md) — 18+).

## 7. Your rights

Because we hold no personal account data, there is nothing to export from “our servers”. You can:

- delete all local data via the app wipe / uninstall;
- choose which RPC/explorer each chain uses;
- turn Tor / kill-switch on;
- stop using price or update features.

## 8. International transfers

We do not operate a cloud that transfers your personal data. Third-party nodes you query may be in other countries; choosing your own node or Tor is how you reduce that exposure.

## 9. Changes

We will update the “Last updated” date when this policy changes. Material changes for store builds will also be noted in release notes where practical.

## 10. Contact

| | |
|---|---|
| Privacy / product questions | [t.me/UmbrellaWallet](https://t.me/UmbrellaWallet) |
| Security issues (private) | [GitHub Security Advisories](https://github.com/thefear078/UmbrellaWallet/security/advisories/new) |
| Author contacts | [CONTACT.md](CONTACT.md) |

**Never** send a recovery phrase or private key to any contact.

---

📖 Back to [Documentation Index](docs/INDEX.md)

