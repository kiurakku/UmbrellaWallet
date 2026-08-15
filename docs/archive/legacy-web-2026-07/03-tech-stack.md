# 03 — Tech Stack

## Frontend (Web App)

| Layer | Technology | Why |
|-------|------------|-----|
| Runtime | React 19 | Latest stable, hooks, concurrent features |
| Framework | TanStack Start | SSR + file-based routing, Nitro adapter |
| Router | TanStack Router | Type-safe search params, loaders |
| State | Zustand | Session (authStore), profile (profileStore) |
| Server state | TanStack Query | Async cache, deduplication, refetch |
| Styling | Tailwind CSS 4 + CVA | Utility-first, dark/light themes, oklch colors |
| UI primitives | Radix UI | Accessible, unstyled, headless components |
| Drawer/sheet | Vaul | Bottom drawer for mobile-first |
| Toasts | Sonner | Toast notifications |
| Charts | Recharts | Sparklines, portfolio distribution |
| Icons | Lucide React | Consistent icon set |
| Forms | React Hook Form + Zod | Type-safe validation |

### Crypto libraries (all client-side, no WASM)

| Package | What | Used for |
|---------|------|----------|
| `@scure/bip39` | BIP39 mnemonic (JS-only) | Generate / validate seed phrases |
| `@scure/bip32` | HD key derivation | BIP44 path → private keys (ephemeral) |
| `@scure/base` | Base58 / Bech32 | Bitcoin / Solana address encoding |
| `@noble/curves` | secp256k1 + ed25519 | ECDSA (ETH,BTC,TRON), Ed25519 (SOL) |
| `@noble/hashes` | SHA256, Keccak, RIPEMD | Address derivation, signing |
| `hash-wasm` | Argon2id (WebAssembly) | Seed encryption KDF |
| `@walletconnect/ethereum-provider` | WalletConnect v2 | Link external wallets (MetaMask, Trust, etc.) |
| `qrcode` | QR code generation | Receive address QR |

No `@trustwallet/wallet-core` (it's C++ → WASM, too heavy). Instead, we use lightweight `@scure` + `@noble` — same authors (Paul Miller), fully audited, smaller bundle.

---

## Backend (API)

| Layer | Technology | Why |
|-------|------------|-----|
| Runtime | Node.js 20+ | Same ecosystem as frontend |
| Framework | NestJS | DI, guards, modular, scalable |
| ORM | Prisma | Type-safe migrations, PostgreSQL |
| Database | PostgreSQL 15+ | ACID, JSON fields, UUID primary keys |
| Cache / Sessions | Redis | Rate-limit, JWT refresh hashes, rates cache |
| Queues | BullMQ (Redis) | Async webhooks, notifications |
| Auth | Passport.js + JWT | OAuth (Apple/Google), custom JWT |
| Password hashing | argon2 (npm package) | Argon2id — GPU-resistant |
| Telegram | grammY | Bot framework, webhook + polling |
| HTTP security | Helmet | CSP, HSTS, XSS headers |
| CORS | @nestjs/platform-express | Origin whitelist from .env |
| Validation | class-validator + class-transformer | DTO validation |
| Logging | Winston / Pino | Structured logs, scrub secrets |
| Testing | Jest | Unit tests for P2P state machine |

### External APIs (backend proxies them for privacy)

| Service | Usage | Credentials |
|---------|-------|-------------|
| **CoinGecko** | Live crypto prices | Free API (no key needed, rate-limited) |
| **Monobank API** | Open Banking balance | User's personal token (stored encrypted) |
| **PrivatBank** | Merchant balance | API key (merchant account) |
| **Sumsub / Veriff** | KYC verification | Webhook + API key |
| **Stripe / Corefy** (optional) | Card tokenization | Publishable + secret key |

---

## Infrastructure

| Component | Tech | Notes |
|-----------|------|-------|
| Container | Docker Compose | postgres + redis in dev |
| CI | GitHub Actions (`.github/workflows/ci.yml`) | Lint, build, test |
| Deployment | Vercel / Fly.io / Render | Frontend + backend can deploy separately |
| Domain | Custom or .onion | Tor hidden service option |

---

## Security tools

| Tool | What | When |
|------|------|------|
| `npm audit` | Vulnerability scan | Every install |
| `Snyk` / Dependabot | Automated alerts | GitHub integration |
| CSP | Content Security Policy | `backend/src/main.ts` Helmet config |
| SRI | Subresource Integrity | `<link>` / `<script>` hashes (TODO in build) |
| HSTS | HTTP Strict Transport Security | `max-age=31536000` in Helmet |

---

## Optional: Monero integration (not in MVP)

Monero does not use BIP39/44. It needs:
- `monero-javascript` (JS lib for wallet creation)
- Separate seed format (25-word Monero mnemonic)
- ViewKey / SpendKey distinction
- Monero daemon RPC connection for balance / send

See `10-extending.md` for integration recipe.

---

## Development tools

```bash
# Frontend hot reload
npm run dev           # → localhost:5173

# Backend hot reload
npm run dev:backend   # → localhost:3001

# Both at once (concurrently)
npm run dev:all

# Docker infra
npm run docker:up     # postgres + redis

# Migrations
npm run db:migrate

# Build production
npm run build          # frontend
npm run build:backend  # backend
```

---

## Bundle size (frontend, production build)

| File | Size | Notes |
|------|------|-------|
| `client-*.js` | ~380 KB gzip | React + Router + Query |
| `walletCore-*.js` | ~45 KB gzip | @scure + @noble crypto |
| `index-*.css` | ~18 KB gzip | Tailwind purged |
| **Total** | **~450 KB gzip** | First load |

Subsequent navigations: <20 KB per route (code-split).

No analytics, no Google Tag Manager, no Facebook Pixel — only the code needed for the wallet.
