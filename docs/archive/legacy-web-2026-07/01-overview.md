# 01 — Overview

## What is Umbra Wallet?

Umbra Wallet is a **non-custodial, privacy-first crypto wallet and P2P exchange** that runs entirely on the user's own machine or browser. The server never sees a private key, seed phrase, or card number — ever.

Three distinct products share one codebase:

| Product | Entry Point | Runtime | Description |
|---------|-------------|---------|-------------|
| **Web App** | `npm run dev` → localhost:5173 | Browser | React 19 + TanStack Start. Wallet dashboard, P2P market, exchange. Seed is encrypted in the browser's IndexedDB. |
| **Backend API** | `cd backend && npm run start:dev` → localhost:3001 | Node.js / NestJS | Auth, P2P matchmaking, rates cache, KYC status, notifications. Zero crypto keys. |
| **Telegram Mini App / Bot** | via `@UmbraWBot` | Telegram + same backend | Optional access layer. Balance read-only. Deal notifications. |

---

## Philosophy

### 1. Non-custodial by design
The architecture is structurally incapable of losing user funds because it never holds them:
- Seed phrases live **only** in the user's IndexedDB, encrypted with Argon2id + AES-256-GCM.
- Private keys are **never** transmitted to any server (not even the Umbra backend).
- The backend stores only: public wallet addresses, bank account references, P2P deal proofs.

### 2. Aggregator, not custodian
Umbra links existing external wallets (WalletConnect v2) and bank accounts (Monobank, PrivatBank Open Banking). It does not create wallets or hold balances on behalf of users.

### 3. Privacy layers
- **Username-only registration** — no email, no phone, no real name required.
- **Tor / .onion** support — all third-party connections can be routed through Tor.
- **Privacy mode** toggle — disables Telegram SDK and all non-essential external connections.
- **No analytics, no tracking pixels, no Google/Facebook SDKs.**

### 4. Self-sovereign
A user can export their seed phrase (after password-decrypt) and immediately import it into any BIP39-compatible wallet (Trust Wallet, MetaMask, etc.). Zero lock-in.

### 5. Transparent fees
- **Network fees only** — Umbra adds zero markup on send/receive.
- See `07-financial.md` for the full fee model.

---

## Supported chains (Web App)

| Chain | Coin | Derivation Path | Notes |
|-------|------|-----------------|-------|
| Ethereum | ETH, ERC-20 | m/44'/60'/0'/0/n | Full send/receive |
| Bitcoin | BTC | m/44'/0'/0'/0/n | P2PKH addresses |
| Tron | TRX, TRC-20 (USDT) | m/44'/195'/0'/0/n | High network fees on USDT — see 07-financial |
| Solana | SOL, SPL | SLIP-0010 m/44'/501'/0'/n | Ed25519 |
| (extensible) | Any BIP44 coin | Add entry in walletCore.ts | See 10-extending |

---

## Non-goals (what Umbra deliberately does NOT do)

- Store private keys, seed phrases, or PAN on the server.
- Custody user funds at any point during P2P trades.
- Provide centralized escrow (P2P is proof-based: `cryptoTxHash` + `fiatPaymentReference`).
- Collect identity (KYC is optional and provider-handled — Sumsub/Veriff keeps the documents).
- Operate as a Money Services Business or VASP. See `08-security.md` legal section.

