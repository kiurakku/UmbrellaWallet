# 11 — Glossary

**Aggregator** — A platform that links external wallets and accounts without holding user funds. Umbra is an aggregator, not a custodian.

**Argon2id** — A password hashing algorithm resistant to GPU brute-force attacks. Used for seed encryption KDF and backend password storage.

**AES-256-GCM** — Authenticated encryption standard. Used to encrypt the seed phrase in IndexedDB. Requires 32-byte key + 12-byte IV.

**BIP32** — Bitcoin Improvement Proposal 32: Hierarchical Deterministic (HD) wallets. Allows deriving infinite child keys from one master seed.

**BIP39** — Standard for generating human-readable mnemonic phrases (12 or 24 words) from entropy. Mnemonics encode a 128-bit or 256-bit seed.

**BIP44** — Defines derivation paths for multi-coin HD wallets: `m/44'/coin'/account'/change/index`. Example: Ethereum = coin 60, Bitcoin = coin 0.

**Chain analysis** — Forensic analysis of blockchain transaction graphs to identify wallet owners. Public blockchains are pseudonymous, not anonymous.

**CSP (Content Security Policy)** — HTTP header that restricts which scripts/styles can execute, mitigating XSS attacks. Umbra uses `default-src: 'self'`.

**Custodian** — An entity that holds private keys or funds on behalf of users. Umbra is NOT a custodian (non-custodial wallet).

**DAO** — Decentralized Autonomous Organization. Not relevant to Umbra (we're a software tool, not a governance system).

**DPAPI** — Windows Data Protection API. Encrypts data using OS-managed keys. Used in desktop app for keychain-equivalent on Windows.

**Escrow** — A third party holds funds until conditions are met. Current Umbra P2P has NO escrow (users trade directly). Smart contract escrow is in roadmap.

**GDPR** — General Data Protection Regulation (EU). Requires: right to access, delete, portability. Umbra complies via DELETE /users/me and data minimization.

**HD Wallet (Hierarchical Deterministic)** — A wallet that derives infinite addresses from one seed phrase (BIP32). Umbra generates ETH, BTC, SOL, TRX from the same seed.

**HSTS (HTTP Strict Transport Security)** — Header that forces browsers to use HTTPS. Umbra backend sets `max-age=31536000`.

**IndexedDB** — Browser storage API for structured data. Umbra stores encrypted seed here (key: `seed:{userId}`).

**JWT (JSON Web Token)** — Access token format. Umbra uses: 15-min access JWT (in memory), 30-day refresh token (httpOnly cookie, hashed in DB).

**KDF (Key Derivation Function)** — Converts a password into a cryptographic key. Umbra uses Argon2id with 64MB memory, 3 iterations.

**Keychain** — macOS secure storage for passwords and keys. Desktop app would use this instead of IndexedDB.

**KYC (Know Your Customer)** — Identity verification (passport, selfie). Umbra requires KYC only for P2P above thresholds. Documents stored by Sumsub/Veriff, not us.

**Libsecret** — Linux equivalent of macOS Keychain. GNOME Keyring / KWallet backend.

**Mnemonic** — 12 or 24 words that encode a seed. BIP39 standard uses 2048-word English wordlist. Umbra generates 12-word by default (128-bit entropy).

**MSB (Money Services Business)** — US FinCEN term for businesses transmitting money. Non-custodial wallets are generally NOT MSBs (but consult lawyer).

**Non-custodial** — User controls private keys; platform cannot access funds. Opposite: custodial (exchange holds your keys).

**OAuth** — Open Authorization protocol. Umbra supports Google / Apple OAuth for account creation (seed is still generated separately, locally).

**Open Banking** — APIs that allow third parties to access user bank accounts with consent. Umbra uses Monobank / PrivatBank Open Banking for balance display.

**P2PKH (Pay-to-Public-Key-Hash)** — Bitcoin address format (starts with `1`). Umbra derives these for BTC.

**PAN (Primary Account Number)** — The 16-digit card number. Umbra never stores PAN (uses Stripe/Corefy tokens instead).

**Passkey** — WebAuthn credential using biometrics (Face ID / Touch ID). In roadmap for Umbra wallet unlock.

**PCI DSS** — Payment Card Industry Data Security Standard. Required if you store card numbers. Umbra avoids this by never storing PAN.

**Prisma** — TypeScript ORM for Node.js. Generates type-safe DB client from `schema.prisma`.

**Seed phrase** — Another term for mnemonic. The 12 or 24 words that can recover a wallet.

**SLIP-0010** — Ed25519 HD key derivation standard (alternative to BIP32 for curves that don't support it). Solana uses this.

**SOCKS5** — Proxy protocol. Tor provides a SOCKS5 proxy on `127.0.0.1:9050`. Umbra recommends routing API calls through it in privacy mode.

**Subaddress** — Monero's term for derived receive addresses (privacy feature). Not yet implemented in Umbra.

**TRC-20** — Token standard on Tron (like ERC-20 on Ethereum). USDT on Tron is a TRC-20 token.

**VASP (Virtual Asset Service Provider)** — EU MiCA term for crypto exchanges, custodians. Non-custodial wallets are NOT VASPs (but check with lawyer).

**ViewKey** — Monero: a key that allows viewing incoming transactions without spending. Used for balance scanning.

**WalletConnect** — Protocol for connecting web apps to mobile wallets via QR code. Umbra supports WalletConnect v2.

**WebAuthn** — W3C standard for passwordless authentication using biometrics or security keys. In roadmap for Umbra.

**Webhook** — HTTP callback from third party when event occurs. Umbra receives webhooks from: Sumsub (KYC status), Stripe (payment status), Telegram (bot updates).

**WASM (WebAssembly)** — Binary format for running compiled code in browsers. `hash-wasm` (Argon2id) uses WASM; crypto libs (`@scure`, `@noble`) are pure JS.

**XSS (Cross-Site Scripting)** — Attack where malicious script runs on victim's page. Mitigated by CSP + input sanitization. React auto-escapes JSX.

**Zero-knowledge proof** — Cryptographic proof without revealing the secret. Not yet used in Umbra (but could be added for P2P reputation without identity exposure).
