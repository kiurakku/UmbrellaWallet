# Umbra Wallet — Technical Overview

> Compact reference. Update when architecture changes. See also: `UMBRA_BACKEND_SPEC.md`, `UMBRA_TELEGRAM_BOT_SPEC.md`, `DEPLOY.md`.

## Architecture

```
Browser / Telegram Mini App
        │ same-origin fetch (credentials: include)
        ▼
TanStack Start SSR server (Vercel)  ── src/server.ts
        │  /auth /users /wallets /bank-accounts /p2p /rates /kyc /webhooks /telegram /health
        │  proxied to API_ORIGIN (env)          otherwise → SSR routes
        ▼
NestJS API (Render: umbra-api)  ── backend/
        ├─ PostgreSQL (Prisma)     — users, refresh tokens, wallets, P2P offers/orders, KYC
        ├─ Redis (optional)        — cache/rates
        └─ Telegram Bot (webhook)  — backend/src/telegram/
```

- **Non-custodial**: seed phrase generated client-side (`src/lib/wallet/seedManager.ts`, BIP-39 via `@scure/bip39`), encrypted in local vault (`vault.ts`); server never sees keys.
- **Demo mode**: `VITE_DEMO_MODE=true` (frontend, localStorage-backed `demoApi`) / `DEMO_MODE=true` (backend, in-memory store). Both **off by default** since commit `274de1e`.

## Tech Stack

| Layer | Tech |
|---|---|
| Frontend | React 19, TanStack Start/Router, Tailwind 4, Radix/shadcn, Recharts, Vite 8 |
| Wallet crypto | `@scure/bip39`, `@scure/bip32`, `@noble/curves`, `hash-wasm` |
| Backend | NestJS, Prisma (PostgreSQL), Redis, Throttler, JWT |
| Auth | Username+password (Argon-style hash in `crypto.util.ts`), Google/Apple id_token, Telegram initData |
| Deploy | Vercel (web, SSR proxy) + Render (`render.yaml`, service `umbra-api`) |

## Auth Flow

1. `POST /auth/register|login` → `{ accessToken, user }` + httpOnly refresh cookie `umbra_refresh` (path=/auth, sameSite=lax, 30d, rotated on refresh).
2. Access token kept **in memory only** (`src/lib/api/client.ts`); on 401, client auto-calls `/auth/refresh` and retries once.
3. `bootstrapSession()` in `authStore.ts` runs on load: refresh cookie → else Telegram initData → else logged out.
4. Register without email synthesizes `<username>@umbra.local` (backend `auth.service.ts`).
5. Email verification: settings → Email → `POST /auth/email/request-verification` (sets email, sends 24h link via SMTP when `SMTP_URL` set, else logs) → `/verify-email?token=…` → `POST /auth/email/verify`.

## State Management

- **Session/profile**: custom external stores + `useSyncExternalStore` — `src/lib/authStore.ts`, `profileStore.ts` (metadata persisted to localStorage, tokens never).
- **Server data**: TanStack Query (`useMarketRates`, `usePortfolio`, `useP2pOrderStream` — SSE stream for P2P order updates).

## Routing (file-based, TanStack Router)

| Route | File | Purpose |
|---|---|---|
| `/` | `src/routes/index.tsx` | Dashboard (or `Welcome` onboarding when logged out) |
| `/exchange` | `exchange.tsx` | Swap/exchange |
| `/p2p` | `p2p.tsx` | P2P offers + orders |
| `/stats` | `stats.tsx` | Market stats/charts |
| `/settings` | `settings.tsx` | Profile, security, linked wallets/banks |
| `/help`, `/legal/*` | | Static/legal content |

## Backend Modules (`backend/src/`)

`auth` (JWT + OAuth + Telegram), `users`, `linked-wallets` (challenge-signature linking), `linked-bank-accounts` (incl. Monobank), `p2p` (offers, orders, state machine in `p2p-transitions.ts`, SSE events), `rates`, `kyc`, `telegram` (bot, webhook, notify), `webhooks`, `demo`, `redis`, `prisma`.

## Environments

| Var | Where | Purpose |
|---|---|---|
| `API_ORIGIN` | Vercel | Upstream for SSR proxy (e.g. `https://umbra-api.onrender.com`) |
| `VITE_API_URL` | build | Direct API base (bypasses proxy; usually empty) |
| `VITE_DEMO_MODE` | build | Frontend demo mode |
| `DEMO_MODE`, `DATABASE_URL`, `REDIS_URL`, `JWT_*_SECRET`, `TELEGRAM_BOT_TOKEN`, `CORS_ORIGIN`, `COOKIE_SECURE` | Render | Backend runtime |
