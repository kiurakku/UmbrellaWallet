# Umbra Wallet — Action History & Todo

> Append-only log + living checklist. One line per action, newest last.

## Action History

| Date | Action | Result |
|---|---|---|
| 2026-07-11 | Step 1: Full codebase audit (frontend `src/`, backend `backend/src/`, deploy configs) | `docs/TECH_OVERVIEW.md` created |
| 2026-07-11 | Diagnosed broken web Registration/Login | Root cause: no live backend at `API_ORIGIN` (Render `umbra-api` returns `x-render-routing: no-server`); demo fallback disabled since `274de1e`. Verified via live probes of `umbra-wallet-web.vercel.app` and `umbra-api.onrender.com` |
| 2026-07-11 | Step 2: fixed backend boot crash (root cause of dead Render deploy) | (1) `RedisThrottlerStorage` not resolvable in `ThrottlerModule.forRootAsync` context → moved into global `RedisModule`; (2) circular file-import chain `p2p.service → telegram-notify → telegram-bot → p2p.service` → added `@Inject(forwardRef(...))` on both injection sites; (3) pino logger serialized `Error` as `{}` → added Error normalization in `scrubPii` |
| 2026-07-11 | Step 2: fixed seed vault CSP block | `script-src` lacked `'wasm-unsafe-eval'` → hash-wasm (argon2id) failed → seed encryption impossible in prod. Fixed `src/lib/csp.ts` |
| 2026-07-11 | Step 2: fixed ETH address derivation | `walletCore.ts` passed a compressed pubkey to `getPublicKey()` (expects privkey) → ETH watch-only linking silently failed. Now decompresses via `secp256k1.Point.fromBytes`. Verified against BIP-44 test vectors (ETH+BTC MATCH) |
| 2026-07-11 | Step 2: fixed /p2p routing collision | Bare `/p2p` (page) was proxied to API → 404 on refresh/deep-link. Vite proxy key → `^/p2p/`, `isApiPath` excludes exact `/p2p` |
| 2026-07-11 | Step 2: fixed P2P schema drift | Migration `20260709150000` created snake_case columns (`reserved_amount`…) while schema.prisma expects camelCase → every offers query 500'd. Migration corrected (no deployed DB existed) |
| 2026-07-11 | Step 2: fixed P2P order creation validation | Optional `paymentMethod` was effectively required by dual-alias `ValidateIf` in `CreateOrderDto` → `@IsOptional()` |
| 2026-07-11 | Step 2: fixed P2P market semantics + sorting | "Купити" tab now shows counterparty (sell-side) offers and vice versa; best price sorts first |
| 2026-07-11 | E2E verified on local full stack (Postgres+Redis+Nest+Vite) | register → seed create (12 words, argon2id vault) → logout → login → refresh-cookie restore → P2P order create (500 UAH @41.31) → cancel → reservation released. All pass |
| 2026-07-11 | Step 3: stats page — real interactive charts | Fake sparkline/dead range buttons removed; recharts AreaChart on live `/rates/chart` (CoinGecko), coin chips BTC/ETH/SOL/USDT, ranges 1Д–1Р, tooltip, 60s live refresh, market tiles switch the chart |
| 2026-07-11 | Step 4: Telegram bot audited | 13 commands implemented (/start /help /balance /rates /orders /receive /p2p /link /wallet /send /support /notifications /ping) with retry reliability; unit tests pass; syncs with web via shared DB + TelegramNotifyService. Needs only deploy env (`TELEGRAM_BOT_TOKEN`, webhook/polling) |
| 2026-07-11 | Cleanup | Deleted unused `src/hooks/useTelegramUi.ts`; fixed `/p2p` Link search-param type. tsc (front+back), backend tests, both prod builds — green |
| 2026-07-11 | P2P full cycle E2E (2 users) | seller testkiur01 × buyer cryptoking (dev password via `backend/scripts/dev-set-merchant-password.ts`): created → awaiting_fiat_payment → fiat_payment_confirmed → crypto_sent → completed; reservation released. Merchant names now shown in order lists (offer join includes merchant) |
| 2026-07-11 | Email verification — implemented E2E | `MailerService` (nodemailer, env `SMTP_URL`/`SMTP_FROM`; logs letters when SMTP off), `POST /auth/email/request-verification` (JWT, throttled, email-uniqueness check), `POST /auth/email/verify` (public, token 24h, sha256-hash lookup), settings → Email sheet, `/verify-email` page. Verified: request → token → verify → `emailVerified=true` in DB and UI |
| 2026-07-11 | Fix: API error messages on 401-retry path | retry branch in `client.ts` returned raw JSON body; now parsed via `toApiError` |
| 2026-07-11 | Mobile QA (375×812) | dashboard / stats chart / P2P / settings / verify-email — layout correct, no fixes needed |
| 2026-07-11 | Vercel git-deploy stuck | deployment from push `c3b9a9d` sits in status UNKNOWN >10 min; owner must Redeploy in dashboard or approve CLI `vercel deploy --prod` |
| 2026-07-13 | Rebrand → Umbrella Wallet | Vector umbrella logo (`UmbrellaLogo`), monochrome+teal theme, anonymous-first onboarding (Google/Apple removed, CSP tightened) |
| 2026-07-14 | Seed vault per-account scoping | Vault + skip-flag keyed by `userId` (`seed:<id>`); legacy single-slot vault claimable by password (`unlockDeviceVault`); native `confirm()` replaced by tap-to-arm delete. Fixes cross-account vault leak in [[umbra-dev-gotchas]] |
| 2026-07-14 | Fail-fast DB (no silent demo) | `PrismaService` now retries 5× then throws unless `ALLOW_DEMO_FALLBACK=true` — a real app never silently serves fake data when the DB is down |
| 2026-07-14 | Live market stats | Stats headline shows live spot price + 24h change (refetch 20s) with an "updated HH:MM:SS" stamp; chart is the range trend only. Was showing the chart's last historical point |
| 2026-07-14 | Wallet card button overlap fixed | Refresh + hide-balance icons moved into one top-right row (were stacked in the same corner) |
| 2026-07-14 | P2P crypto↔crypto + pre-deal warning | New `quoteKind` (fiat\|crypto) on offers (migration `p2p_quote_kind`); crypto quotes settle on-chain (buyer submits a tx hash as payment proof, verified when Alchemy key present); mandatory risk-acknowledgement gate before every order. Both flows E2E-verified (cryptoking×demo) on the real stack |
| 2026-07-14 | Tor / privacy phase | `privacyMode` auto-on for `.onion`; Telegram script now lazy-injected only inside Telegram (zero third-party requests otherwise); onion-aware CSP/HSTS + `Onion-Location`; `docker-compose.tor.yml` + `torrc` + Dockerfiles + `docs/TOR.md` |
| 2026-07-14 | 24-word seeds | New wallets generate 256-bit (24-word) phrases; import still accepts 12/15/18/21/24. SeedOnboarding confirm-grid + copy handle any length. Verified in browser (24-word grid, confirm asked #3/#11/#24) |
| 2026-07-14 | Real multi-chain derivation | **Fixed broken Solana derivation** (was a seed-hash placeholder → wrong address) to real ed25519 SLIP-0010 (m/44'/501'/0'/0'); added TRON (base58check, m/44'/195'/0'/0/0). New wallet now derives+links ETH, BTC, SOL, TRON. All 4 verified against canonical `abandon…about` vectors AND against a live-created account's DB rows (`chaintest01` → 4 correct addresses) |
| 2026-07-14 | More chains priced + balances | Backend balance fetch for Solana (public RPC) + TRON (TronGrid); `COIN_IDS` gains TRX/BNB/MATIC/AVAX; portfolio maps tron→TRX, EVM L2s→ETH native |
| 2026-07-14 | Onboarding: setup chooser | Post-account screen now presents 3 clear paths — Create new wallet / Import seed phrase / Link existing wallet or card (the last skips seed setup → LinkAccountsPrompt). Account (nick+pass or Telegram) is still the gate before any of them |
| 2026-07-17 | **Phase A — independence from third-party APIs** | (A1) Purged remaining Google/Apple OAuth: deleted `oauth.service.ts`, removed `OAuthService` wiring + `OAuthDto` + demo `oauthLogin`, uninstalled `google-auth-library`, dropped `GOOGLE/APPLE_CLIENT_ID` from env example, updated legal copy. DB columns `oauthProvider/oauthSub` kept (nullable — dropping needs a destructive migration). (A2) Pluggable rates: `IRatesProvider` + CoinGecko/CustomHttp adapters + `RATES_PROVIDER=coingecko\|custom\|auto\|none`, `RATES_URL`; orchestration = fresh cache → provider chain → **stale cache (7d)** → empty fallback, never throws. (A3) Public RPC failover per chain + `EVM_RPC_<CHAIN>` override; ERC-20 balances now plain `eth_call balanceOf` over a token registry (no Alchemy enhanced API). **Proven with zero keys:** vitalik.eth ETH balance via public RPC; Binance USDT/USDC via eth_call; rates survive `RATES_PROVIDER=none` + Redis down, and serve 14 stale assets when providers die |
| 2026-07-14 | Security review / pentest | Runtime probes vs live API: IDOR (non-participant with a valid minted JWT) → **403 on read/complete/cancel/fiat-proof/dispute**; wrong-role → 403; unauth/forged-JWT → 401; mass-assignment (extra buyerId/isAdmin) → 400 (forbidNonWhitelisted); SQL-ish query param → safe empty; login rate-limit → 429. Code review confirmed: argon2id passwords + seed vault, timing-safe HMAC webhooks, EVM sig recovery, httpOnly+SameSite=lax path-scoped refresh cookie, nonce CSP + HSTS + helmet, no SSRF (fixed-host indexer fetches). **Fix:** removed dead Google/Apple OAuth endpoints + `api.oauth` (unused since anonymous-first redesign, extra surface) — now 404. Seed encrypt→reveal round-trip verified in-browser (24 words match). Granite badge redesigned (compact, theme-aligned) |

## Analyzed (Step 1 coverage)

- [x] Frontend auth: `authStore.ts`, `api/client.ts`, `oauth.ts`, `Welcome.tsx`, `demoMode.ts`
- [x] SSR proxy: `src/server.ts`, `api/config.ts`, `vercel.json`
- [x] Backend auth: `auth.controller.ts`, `auth.service.ts`, refresh-token rotation, Prisma schema
- [x] Deploy: `render.yaml`, `DEPLOY.md`, env examples
- [ ] Deep review: P2P engine (`p2p.service.ts`, transitions) — Step 2
- [ ] Deep review: seed phrase vault (`seedManager.ts`, `vault.ts`) — Step 2
- [ ] Deep review: Telegram bot service — Step 4

## Todo

### Deployment (needs owner's Render dashboard — everything else is ready)
- [ ] Deploy Render blueprint: https://dashboard.render.com/blueprint/new?repo=https://github.com/kiurakku/umbrella-wallet — set `DATABASE_URL` (Render Postgres), `REDIS_URL` (Render Key-Value), `TELEGRAM_BOT_TOKEN`; JWT secrets auto-generate; `COOKIE_SECURE=true` already in render.yaml
- [ ] Confirm `GET https://umbra-api.onrender.com/health` → 200 (`database:true`)
- [ ] Vercel `API_ORIGIN` already set — redeploy frontend after push, then verify register/login on prod
- [ ] Set `TELEGRAM_WEBHOOK_URL=https://umbra-api.onrender.com/telegram/webhook` + `TELEGRAM_WEBHOOK_SECRET` for the bot (or `TELEGRAM_USE_POLLING=true` for quick start)

### Done in Step 2–4 (2026-07-11)
- [x] Registration / Login / seed generation+validation — fixed & E2E-verified locally
- [x] P2P: order creation, state machine, reservation, cancel/dispute — verified
- [x] Dead code cleanup (repo was already lean; `backend/dist`, `.env` git-ignored)
- [x] Interactive price charts + live market refresh (stats page)
- [x] Telegram bot commands + reliability — code-complete, tests green
