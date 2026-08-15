# 08 — Security & Legal Model

## Threat model

| Attack vector | Risk | Mitigation |
|--------------|------|------------|
| XSS (Cross-site scripting) | High | Strict CSP (no unsafe-inline), scrubSecrets on all logs, React auto-escapes JSX |
| SQL injection | Medium | Prisma ORM prevents raw SQL; parameterized queries only |
| CSRF (Cross-site request forgery) | Low | SameSite=Strict cookies, CORS origin whitelist |
| Seed theft via malware | High | **User responsibility** — OS keylogger can read password during unlock. Web app cannot defend. Recommend: desktop app (OS keychain) or hardware wallet for large holdings. |
| Phishing (fake Umbra site) | Medium | Educate users to verify domain, HTTPS, EV cert (if possible) |
| Supply chain attack (npm package) | Medium | `npm audit` in CI, Dependabot alerts, lock file audit |
| Compromised backend | Low impact | Backend never sees private keys — even total server breach cannot steal user funds |
| Man-in-the-middle (MITM) | Low | HTTPS required, HSTS, certificate pinning (future) |
| Brute-force login | Low | Argon2id slows hashing, rate-limit 5 attempts/15min per IP |
| Replay attack (JWT) | Low | Short TTL (15min access, 30d refresh with rotation) |
| Session hijacking | Low | httpOnly cookies, Secure flag, JWT in memory only |
| P2P scam (fake payment proof) | Medium | Users must verify on-chain tx hash + fiat receipt manually; dispute system for edge cases |

---

## Audit status

| Component | Last audit | Findings | Status |
|-----------|-----------|----------|--------|
| Frontend (React) | Not audited | — | Self-review only |
| Backend (NestJS) | Not audited | — | Self-review only |
| Crypto lib (@scure, @noble) | Audited by Trail of Bits (Nov 2022) | 0 critical | ✅ Secure |
| Argon2id (hash-wasm) | N/A | — | Industry standard |
| AES-256-GCM | N/A | — | NIST approved |

**Recommended next step:** When user base >1,000 MAU or >$1M in monthly transaction volume, commission a security audit (cost: $15-40k for web3 wallet auditor like Trail of Bits, CertiK, OpenZeppelin).

---

## Legal model: non-custodial aggregator

### What Umbra does
- Links existing wallets (WalletConnect) and bank accounts (Open Banking)
- Displays aggregated balances
- Facilitates P2P matchmaking (publishes offers, coordinates order proofs)
- Provides UI for exchange rate quotes (no execution — user signs tx themselves)

### What Umbra does NOT do
- Store private keys, seed phrases, or PAN (card numbers)
- Hold or custody user funds at any point
- Execute trades on behalf of users (no escrow, no pooled funds)
- Provide investment advice or financial planning
- Guarantee P2P deal outcomes (users bear counterparty risk)

### Legal classification (best estimate — not legal advice)

| Jurisdiction | Likely classification | Licensing required? |
|--------------|----------------------|---------------------|
| **USA** | Software provider (not MSB if truly non-custodial) | No federal FinCEN registration if no custody; state-by-state money transmitter review recommended |
| **EU** | Not a VASP under MiCA if no custody; but P2P facilitation may require payment institution license | Consult EU fintech lawyer before public launch |
| **Ukraine** | Not a financial institution if no custody; P2P coordination may require registration | Law "On Virtual Assets" (2022) — consult local lawyer |
| **UK** | FCA: not an MSB if non-custodial; but "crypto asset business" registration may apply | FCA registration required if promoting to UK residents |

**Key principle:** If Umbra never touches the money (neither crypto nor fiat), it is not a custodian and does not fall under most VASP/MSB definitions. But:
- P2P facilitation can trigger "payment facilitator" rules in some countries
- Swap rate aggregation without custody is generally safe (like 1inch, CoinGecko)
- This is a legal grey area — **consult a fintech lawyer** in your primary jurisdiction before launch

### Disclaimers (must include in UI)

**On welcome screen (legal footer):**
"By continuing, you agree to the Terms of Service, User Agreement, and Privacy Policy. Umbra Wallet is a non-custodial software tool. We do not control your funds. You are responsible for securing your seed phrase. Crypto assets are volatile and may lose value."

**On P2P page:**
"P2P trades are peer-to-peer. Umbra does not hold funds or guarantee outcomes. Verify all payment proofs before releasing crypto. Use the dispute system if needed."

**On send screen:**
"This transaction is irreversible. Double-check the recipient address. Network fees are paid to miners, not Umbra."

---

## GDPR compliance

| Requirement | Implementation |
|------------|----------------|
| Right to access | GET /users/me returns all user data |
| Right to delete | DELETE /users/me → soft delete + anonymize |
| Data portability | GET /users/export (future) → JSON dump |
| Consent for processing | Terms acceptance on signup |
| No unnecessary data | We never ask for real name, phone, SSN, address (unless KYC required for P2P limits) |
| Data minimization | KYC documents stored by Sumsub/Veriff (third party), not by us |
| DPO (if >250 users in EU) | Appoint Data Protection Officer |

**Data retention:**
- Active accounts: indefinite
- Deleted accounts: soft delete, anonymize email immediately, hard delete after 90 days (legal hold period for disputes)
- Logs: 30 days retention, then purge

---

## Incident response plan

### Scenario 1: Backend server breach

**Impact:** Attacker gains access to PostgreSQL and Redis.

**What attacker can get:**
- Hashed passwords (Argon2id — brute-force impractical)
- Hashed refresh tokens (cannot be reversed)
- Public wallet addresses (already public on blockchain)
- Encrypted bank tokens (need BANK_TOKEN_ENCRYPTION_KEY from env to decrypt)
- P2P order proofs (already public on-chain: tx hashes)

**What attacker CANNOT get:**
- Seed phrases (never on server)
- Private keys (never on server)
- Unhashed refresh tokens (only hashes in DB)
- Plaintext passwords (hashed with Argon2id)

**Response:**
1. Shut down backend immediately (deny all traffic)
2. Rotate all secrets (JWT_ACCESS_SECRET, JWT_REFRESH_SECRET, DB passwords)
3. Invalidate all refresh tokens (mass revoke)
4. Force all users to re-login
5. Notify users: "Our server was compromised. Your funds are safe (we never had access). Change your Umbra account password as a precaution."
6. Restore from clean backup, patch vulnerability
7. Commission security audit before relaunch

### Scenario 2: User loses seed phrase

**Impact:** User cannot recover their wallet.

**Response:**
"We cannot recover your seed phrase. This is by design — non-custodial means only you control your funds. Check if you wrote it down or saved it in a password manager. If lost, the wallet is unrecoverable."

(Harsh, but honest. No false promises.)

### Scenario 3: P2P fraud (buyer paid, seller never sent crypto)

**Response:**
1. User opens dispute via PATCH /p2p/orders/:id/dispute
2. Support (manual) reviews:
   - Fiat payment proof (screenshot, bank statement)
   - Crypto tx hash (if seller claims they sent)
   - Both parties' history (rating, past disputes)
3. Mediation: encourage voluntary resolution
4. Escalation: if seller has pattern of fraud, ban account (flag in DB)
5. No refund from platform (we don't hold funds) — user must pursue legal recourse against counterparty

**Prevention:**
- Reputation system (in roadmap)
- Require KYC for P2P sellers above threshold
- Escrow smart contract (future — makes Umbra a custodian temporarily, adds legal complexity)

---

## Bug bounty program (planned)

When app reaches 1,000+ MAU:

| Severity | Reward | Examples |
|----------|--------|----------|
| Critical | $500-1,000 | RCE, private key leak, fund theft |
| High | $200-500 | XSS leading to session hijack, SQL injection |
| Medium | $50-200 | CSRF, open redirect, info disclosure |
| Low | $0-50 | Minor logic bugs, UI issues |

Scope: frontend + backend code. Out of scope: third-party libs (report to upstream), social engineering, physical access attacks.

---

## Compliance roadmap

| Milestone | Action | ETA |
|-----------|--------|-----|
| 100 users | Legal review (terms/privacy) | Launch |
| 500 users | Consult fintech lawyer (jurisdiction-specific) | Month 2 |
| 1,000 users | Commission security audit | Month 4 |
| 5,000 users | GDPR DPO appointment (if EU users >250) | Month 6 |
| 10,000 users | AML/KYC policy review, P2P limit enforcement | Month 9 |
| 50,000 users | Apply for payment facilitator license (if required) | Month 12 |

**Note:** These are rough estimates. Consult a lawyer familiar with crypto regulation in your target market(s) ASAP, ideally before public launch.

---

## User education (built into UI)

1. **Seed phrase screen:** Big red warning box: "Never share this. Write it down. No one can recover it if lost, not even us."
2. **Send screen:** Confirmation modal: "This transaction is permanent. Verify recipient address. Proceed?"
3. **P2P trade:** Before creating order: "Umbra does not custody funds. You trade directly with the counterparty. Verify all proofs."
4. **Settings → Privacy:** Toggle explanation: "Privacy mode disables third-party connections. Use Tor Browser for maximum anonymity."
5. **Help page:** Link to "How to use Umbra safely" guide (Notion doc or GitHub wiki)

Make security education boring but unavoidable — users skip warnings at their own risk, but we must show them.
