# 09 — Build & Deploy

## Prerequisites

```bash
# Required on your machine:
node --version   # 20.x or higher
docker --version # Docker Desktop (for local dev)
bun --version    # optional, used for faster package install

# Clone the repo
git clone <repo-url>
cd "Umbra Wallet"
```

---

## Local development (full stack)

### Step 1: Start infrastructure (PostgreSQL + Redis)
```bash
docker compose up -d
# → PostgreSQL on localhost:5432
# → Redis on localhost:6379
```

### Step 2: Backend setup
```bash
cd backend
cp .env.example .env
# Edit .env:
#   DATABASE_URL=postgresql://umbra:password@localhost:5432/umbra
#   REDIS_URL=redis://localhost:6379
#   JWT_ACCESS_SECRET=your-secret-min-32-chars
#   JWT_REFRESH_SECRET=your-different-secret

npm install
npx prisma migrate dev --name init
npm run prisma:seed   # inserts demo P2P offers

npm run start:dev     # → http://localhost:3001
```

### Step 3: Frontend setup
```bash
cd ..  # back to project root
cp .env.example .env.local
# VITE_API_BASE_URL is proxied via vite.config.ts (no change needed for local)

npm install
npm run dev           # → http://localhost:5173
```

### Single command (both at once)
```bash
npm run dev:all
# Uses concurrently to run backend + frontend simultaneously
```

---

## Environment variables reference

### Backend (`backend/.env`)

```env
# === Database ===
DATABASE_URL=postgresql://umbra:password@localhost:5432/umbra

# === Cache ===
REDIS_URL=redis://localhost:6379

# === JWT (generate with: openssl rand -hex 32) ===
JWT_ACCESS_SECRET=<32+ random chars>
JWT_REFRESH_SECRET=<different 32+ random chars>

# === OAuth (optional — leave empty for dev) ===
GOOGLE_CLIENT_ID=
APPLE_CLIENT_ID=
APPLE_TEAM_ID=
APPLE_KEY_ID=
APPLE_PRIVATE_KEY=  # multiline, use \n

# === Telegram (optional) ===
TELEGRAM_BOT_TOKEN=
TELEGRAM_WEBHOOK_SECRET=
TELEGRAM_WEBAPP_URL=http://localhost:5173
TELEGRAM_WEBHOOK_URL=   # empty = polling mode

# === Bank token encryption (openssl rand -hex 32) ===
BANK_TOKEN_ENCRYPTION_KEY=<64 hex chars>

# === KYC (optional) ===
SUMSUB_APP_TOKEN=
SUMSUB_SECRET_KEY=
VERIFF_API_KEY=

# === Payments (optional) ===
STRIPE_SECRET_KEY=
STRIPE_WEBHOOK_SECRET=

# === App ===
PORT=3001
NODE_ENV=development
CORS_ORIGIN=http://localhost:5173
```

### Frontend (`.env.local`)

```env
# API is proxied in dev — no VITE_API_URL needed for localhost
# Only set these for production builds:

VITE_WALLETCONNECT_PROJECT_ID=your-walletconnect-project-id
# Register at https://cloud.walletconnect.com (free)

VITE_DEMO=false  # set true to enable demo mode
```

---

## Database commands

```bash
# Create / apply migrations
cd backend
npx prisma migrate dev --name <description>
# Example: npx prisma migrate dev --name add-swap-spread

# Generate Prisma client after schema change
npx prisma generate

# Open Prisma Studio (visual DB browser)
npx prisma studio  # → http://localhost:5555

# Seed with demo data
npm run prisma:seed

# Reset DB (drops all data, re-applies migrations)
npx prisma migrate reset
```

---

## Production build

```bash
# === Frontend ===
npm run build
# Output: .output/public/ (static assets)
# Nitro server: .output/server/index.mjs

# === Backend ===
cd backend
npm run build
# Output: backend/dist/
# Start: node dist/main.js
```

---

## Desktop app (build & release)

The desktop wallet is **.NET 8 + Avalonia** (`desktop/`). Requires the .NET 8 SDK on PATH.

```bash
# From desktop/ — debug build (fast, for development):
dotnet build src/Umbrella.Wallet.App/Umbrella.Wallet.App.csproj -c Debug
```

**Release (Windows)** — one script stages the bundled Tor + Monero binaries, publishes the
installer payload and the portable single-file exe, and builds the Inno Setup installer:

```powershell
pwsh desktop/scripts/release-windows.ps1
# → D:\umbrella-dist\portable\Umbrella.Wallet.App.exe
# → D:\umbrella-dist\UmbrellaWallet-Setup-<version>.exe   (needs Inno Setup 6 / ISCC.exe)
```

**Release (Linux)** — self-contained tar.gz:

```bash
desktop/scripts/publish-linux.sh   # → dist/linux/umbrella-wallet-<version>-linux-x64.tar.gz
```

The version comes from `<Version>` in `Umbrella.Wallet.App.csproj`; keep `VERSION`, the csproj, and
`desktop/installer/umbrella.iss` (`#define AppVersion`) in sync. The Tor (~35 MB) and monero-wallet-rpc
(~39 MB) binaries are **not** committed — the `fetch-tor.ps1` / `fetch-monero.ps1` (and the Linux
publish script) download them at build time.

> **Troubleshooting:** if `dotnet` reports *"command not found"* while the SDK folders exist, the
> `dotnet.exe` host is missing (antivirus quarantine is common on security-tooling machines).
> Restore it with `winget install Microsoft.DotNet.SDK.8 --force`.

---

## Docker Compose (development)

```yaml
# docker-compose.yml (already in repo)
services:
  postgres:
    image: postgres:15-alpine
    environment:
      POSTGRES_DB: umbra
      POSTGRES_USER: umbra
      POSTGRES_PASSWORD: password
    ports: ["5432:5432"]
    volumes: ["pgdata:/var/lib/postgresql/data"]

  redis:
    image: redis:7-alpine
    ports: ["6379:6379"]
    command: redis-server --save 60 1  # persist to disk

volumes:
  pgdata:
```

---

## Production deployment: Vercel (frontend) + Fly.io or Render (backend)

### Frontend → Vercel

```bash
npm i -g vercel
vercel login
vercel --prod
# Framework: TanStack Start (Vite)
# Build command: npm run build
# Output: .output
```

Set in Vercel dashboard → Environment Variables:
- `VITE_WALLETCONNECT_PROJECT_ID`

### Backend → Fly.io

```bash
# Install flyctl
curl -L https://fly.io/install.sh | sh

# First deploy
flyctl launch --name umbra-backend --region iad
# → creates fly.toml

flyctl secrets set \
  DATABASE_URL="postgresql://..." \
  REDIS_URL="redis://..." \
  JWT_ACCESS_SECRET="..." \
  JWT_REFRESH_SECRET="..." \
  BANK_TOKEN_ENCRYPTION_KEY="..."

flyctl deploy
```

**fly.toml (backend):**
```toml
app = "umbra-backend"
primary_region = "iad"

[build]
  dockerfile = "Dockerfile"

[env]
  PORT = "3001"
  NODE_ENV = "production"

[[services]]
  protocol = "tcp"
  internal_port = 3001

  [[services.ports]]
    port = 443
    handlers = ["tls", "http"]
```

**Dockerfile (backend):**
```dockerfile
FROM node:20-alpine AS builder
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npx prisma generate
RUN npm run build

FROM node:20-alpine
WORKDIR /app
COPY --from=builder /app/dist ./dist
COPY --from=builder /app/node_modules ./node_modules
COPY --from=builder /app/prisma ./prisma
EXPOSE 3001
CMD ["sh", "-c", "npx prisma migrate deploy && node dist/main.js"]
```

### Database → Neon.tech (managed PostgreSQL, free tier)

```bash
# Create DB at https://neon.tech → copy connection string
# Set in fly secrets:
flyctl secrets set DATABASE_URL="postgresql://user:pass@ep-xxx.us-east-2.aws.neon.tech/umbra?sslmode=require"
```

### Redis → Upstash (managed Redis, free tier)

```bash
# Create at https://upstash.com → REST endpoint or Redis URL
flyctl secrets set REDIS_URL="redis://default:password@xxx.upstash.io:6379"
```

---

## CI/CD pipeline (.github/workflows/ci.yml)

Current CI is minimal. Here's the recommended full pipeline:

```yaml
name: CI

on:
  push:
    branches: [main, develop]
  pull_request:

jobs:
  frontend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: 20 }
      - run: npm ci
      - run: npm run lint
      - run: npm run build

  backend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: 20 }
      - run: npm ci
        working-directory: backend
      - run: npx prisma generate
        working-directory: backend
      - run: npm run build
        working-directory: backend
      - run: npm test
        working-directory: backend

  security:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: 20 }
      - run: npm audit --audit-level=high
      - run: npm audit --audit-level=high
        working-directory: backend
```

---

## Tor hidden service (self-hosted)

If you want to run Umbra accessible via .onion (no domain registration, no IP exposure):

```bash
# On your server (Linux):
apt install tor

# /etc/tor/torrc:
HiddenServiceDir /var/lib/tor/umbra_hidden_service/
HiddenServicePort 80 127.0.0.1:5173
HiddenServicePort 443 127.0.0.1:3001

systemctl restart tor

# Get your .onion address:
cat /var/lib/tor/umbra_hidden_service/hostname
# → xxxxxxxxxxxxxxxxxxxxx.onion
```

Users access `xxxxxxxxxxxxxxxxxxxxx.onion` in Tor Browser — no exit node, no IP leak, end-to-end encryption.

Set in frontend env:
```env
VITE_ONION_HOST=xxxxxxxxxxxxxxxxxxxxx.onion
```

The `isOnionHost()` function in `src/lib/privacyMode.ts` detects `.onion` TLD and forces privacy mode.

---

## SSL/TLS certificates

### Vercel / Fly.io / Render
Auto-provisioned via Let's Encrypt. No action needed.

### Self-hosted
```bash
apt install certbot
certbot certonly --standalone -d yourdomain.com
# → /etc/letsencrypt/live/yourdomain.com/fullchain.pem

# Auto-renew (cron):
0 0 1 * * certbot renew --quiet && nginx -s reload
```

---

## Monitoring

```bash
# Basic health check endpoint
curl https://your-backend.fly.dev/health
# → { "status": "ok", "uptime": 12345, "timestamp": "2025-01-01T..." }

# Recommended: UptimeRobot (free) — check /health every 5min, alert on Telegram
# Set up at https://uptimerobot.com

# Logs (Fly.io)
flyctl logs

# DB size (Neon)
# Check dashboard or:
SELECT pg_size_pretty(pg_database_size('umbra'));

# Redis memory
redis-cli info memory | grep used_memory_human
```
