# 02 — Architecture

## Golden rule: private keys NEVER cross the network boundary

```
╔══════════════════════════════════════════════════════════════════╗
║                        USER'S DEVICE (Browser)                    ║
║                                                                    ║
║  ┌──────────────────────────────────────────────────────────┐    ║
║  │  IndexedDB  (umbra-vault)                                  │    ║
║  │  ┌──────────────────────────────────────────────────┐    │    ║
║  │  │  seed:userId  →  { ciphertext, iv, salt, v:1 }    │    │    ║
║  │  │  Encrypted with AES-256-GCM                        │    │    ║
║  │  │  Key = Argon2id(userPassword, salt, 64MB, 3 iter)  │    │    ║
║  │  └──────────────────────────────────────────────────┘    │    ║
║  └──────────────────────────────────────────────────────────┘    ║
║                          │ decrypt (local only)                    ║
║  ┌───────────────────────▼──────────────────────────────────┐    ║
║  │  wallet/walletCore.ts  (in-browser, no WASM needed)       │    ║
║  │  • @scure/bip39  — mnemonic generate / validate           │    ║
║  │  • @scure/bip32  — HD key derivation (BIP44 paths)        │    ║
║  │  • @noble/curves — secp256k1, ed25519                     │    ║
║  │  • @noble/hashes — keccak256, sha256, ripemd160           │    ║
║  │  • Outputs: addresses only (never private key leaves)     │    ║
║  └──────────────────────────────────┬───────────────────────┘    ║
║                        public addr  │  only                        ║
╚═════════════════════════════════════╪════════════════════════════╝
                                      │  HTTPS / JWT
                  ╔═══════════════════▼════════════════════════╗
                  ║          UMBRA BACKEND  (NestJS)            ║
                  ║  Auth · Users · LinkedWallets · P2P · KYC  ║
                  ║  Rates · Cards · Webhooks · Telegram        ║
                  ║  PostgreSQL + Redis                         ║
                  ╚════════════════╤════════════════════════════╝
                                   │
              ┌────────────────────┼─────────────────────────┐
              ▼                    ▼                          ▼
       Monobank API        CoinGecko API            Sumsub / Veriff
       (balance read)      (rates cache)            (KYC docs — stored
       PrivatBank OB                                 by provider only)
```

---

## Data flows

### A. Wallet creation (fully local)
```
User clicks "Create wallet"
  → walletCore.generateMnemonic(128)      // 12 words, in browser
  → UI shows phrase for backup
  → User confirms backup (word quiz)
  → User sets encryption password (≥8 chars)
  → vault.encryptSeed(mnemonic, password, userId)
      → Argon2id KDF → 32-byte key
      → AES-256-GCM encrypt
      → save to IndexedDB under key "seed:{userId}"
  → walletCore.deriveAddress(mnemonic, 'ethereum', 0)  → public address
  → POST /wallets  { chain, address }     // only public address to server
```

### B. Unlock (open app again)
```
User enters PIN / password
  → vault.loadVault(userId) from IndexedDB
  → vault.decryptSeed(blob, password)     // purely local
  → addresses derived on-the-fly in memory only
  → memory cleared on tab close / auto-lock timer
```

### C. P2P trade flow
```
Buyer opens offer → POST /p2p/orders
  Server: creates order record, status = "created"
  ↓
Buyer sends fiat via bank app (outside Umbra)
  → PATCH /p2p/orders/:id/fiat-proof  { fiatPaymentReference }
  Server: status → "fiat_payment_confirmed"
  ↓
Seller sees notification (WebSocket / Telegram)
  → Seller sends crypto from their own wallet (outside Umbra)
  → PATCH /p2p/orders/:id/crypto-proof  { cryptoTxHash }
  Server: status → "crypto_sent"
  ↓
Buyer verifies on-chain receipt
  → PATCH /p2p/orders/:id/complete
  Server: status → "completed"
```
No escrow. No custody. Only public proof hashes stored on the backend.

### D. Rates flow (read-only, privacy-preserving)
```
Frontend hook useMarketRates()
  → GET /rates/market  (backend)
    → Redis cache check (TTL 60s)
      → if miss: fetch CoinGecko /simple/price (server-side, no user IP leaked)
      → store in Redis
    → return to client
```

---

## Component map

```
src/
├── routes/
│   ├── index.tsx          ← Wallet dashboard (balances, assets, recent P2P)
│   ├── p2p.tsx            ← P2P market, my deals, my offers
│   ├── exchange.tsx       ← Swap / exchange (rates + create P2P offer shortcut)
│   ├── stats.tsx          ← Portfolio analytics, sparklines
│   ├── settings.tsx       ← Wallets, seed, KYC, 2FA, privacy, language
│   ├── nft.tsx            ← NFT viewer (read-only)
│   ├── help.tsx           ← FAQ
│   └── legal/             ← Terms, Privacy, Agreement (static)
├── components/
│   ├── AppShell.tsx       ← Bottom nav, theme, layout wrapper
│   ├── Welcome.tsx        ← Onboarding (cover → slides → auth form)
│   ├── SeedOnboarding.tsx ← Generate / backup / import seed flow
│   ├── P2pOfferSheet.tsx  ← Create / edit offer bottom sheet
│   ├── P2pOrderSheet.tsx  ← Trade execution, status machine UI
│   ├── ActionSheet.tsx    ← Generic bottom drawer
│   └── wallet/            ← WalletSheets (deposit QR, withdraw form, send)
├── lib/
│   ├── wallet/
│   │   ├── vault.ts       ← Argon2id + AES-GCM + IndexedDB
│   │   ├── walletCore.ts  ← BIP39/44, address derivation, tx signing
│   │   ├── walletConnect.ts ← WalletConnect v2, injected wallet
│   │   └── seedManager.ts ← reveal/import/delete seed UI helpers
│   ├── api/
│   │   ├── client.ts      ← All API calls, typed
│   │   ├── config.ts      ← Base URL, timeout
│   │   ├── demo.ts        ← Demo mode stubs
│   │   └── errors.ts      ← Error formatting
│   ├── authStore.ts       ← Zustand session (access token in memory, refresh in cookie)
│   ├── profileStore.ts    ← Preferences (lang, push, 2fa, kyc)
│   ├── scrubSecrets.ts    ← Remove seed/key fields before any log
│   └── privacyMode.ts     ← Tor mode flag, .onion detection

backend/src/
├── auth/           ← register, login, oauth (Google/Apple), refresh, logout
├── users/          ← me, patch, delete (GDPR)
├── linked-wallets/ ← link/unlink wallets, balance proxy
├── linked-bank-accounts/ ← Monobank/PrivatBank OB link
├── p2p/            ← offers CRUD, orders state machine, SSE events
├── rates/          ← CoinGecko proxy with Redis cache
├── kyc/            ← Sumsub/Veriff link, webhook intake
├── cards/          ← payment token CRUD (no PAN stored)
├── webhooks/       ← KYC + payment provider webhooks
├── telegram/       ← bot, auth, notify
├── redis/          ← shared Redis provider
├── prisma/         ← PrismaService
└── common/         ← guards, logger, env validation, privacy middleware
```
