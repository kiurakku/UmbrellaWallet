# 12 — Coins, Chains & Tokens — Complete User Guide

> This document covers everything about coins, networks and tokens in Umbrella Wallet.
> Written for users of all levels — from first-time crypto users to experienced traders.

---

## Quick answer: what Umbrella supports right now

### Coins you can send and receive today

| Coin | Network | Send | Receive | Tokens | Notes |
|------|---------|:----:|:-------:|:------:|-------|
| **Bitcoin** | Bitcoin | ✅ | ✅ | — | Most trusted, moderate fees |
| **Ethereum** | Ethereum | ✅ | ✅ | All ERC-20 | Gateway to DeFi |
| **USDT** | Tron (TRC-20) | ✅ | ✅ | — | Send works. Fee paid in TRX |
| **USDT** | Ethereum, BSC, Solana, Polygon, Arbitrum, Base, Optimism, Avalanche, TON | — | ✅ | — | **Receive and balance only.** Sending a token on these chains is not built yet |
| **USDC** | Ethereum, BSC, Solana, Polygon + more | — | ✅ | — | **Receive and balance only**, same reason |
| **BNB** | BNB Smart Chain | ✅ | ✅ | All BEP-20 | Binance native |
| **SOL** | Solana | ✅ | ✅ | All SPL | Ultra-fast |
| **TRX** | Tron | ✅ | ✅ | All TRC-20 | Powers USDT TRC-20 |
| **TON** | TON | ✅ | ✅ | Jettons (balance) | Telegram's coin |
| **ADA** | Cardano | ✅ | ✅ | Native assets | — |
| **XMR** | Monero | ✅ | ✅ | — | 🟢 Maximum privacy |
| **LTC** | Litecoin | ✅ | ✅ | — | Fast + cheap BTC |
| **BCH** | Bitcoin Cash | ✅ | ✅ | — | Cheap on-chain |
| **DOGE** | Dogecoin | ✅ | ✅ | — | Meme but real |
| **AVAX** | Avalanche | ✅ | ✅ | All ERC-20 | Fast EVM |
| **MATIC** | Polygon | ✅ | ✅ | All ERC-20 | Cheap EVM |
| **FTM** | Fantom | ✅ | ✅ | All ERC-20 | Fast EVM |
| **CRO** | Cronos | ✅ | ✅ | All ERC-20 | Crypto.com chain |
| **ETH** | Arbitrum, Base, Optimism, Linea | ✅ | ✅ | All ERC-20 | Layer 2 — same 0x address, far cheaper |
| **ETH** | zkSync Era | — | ✅ | — | **Balance and receive only.** Its fee model is not Ethereum's, so sending is not enabled yet |
| **ZEC** | Zcash | — | ✅ | — | Receive only for now |

> Any ERC-20 token (LINK, UNI, AAVE, SHIB, PEPE, etc.) is automatically detected and its
> balance shown when you link an Ethereum address. Same for TRC-20 on Tron and SPL on Solana.
>
> **Detected is not the same as sendable.** Token balances appear for every chain above;
> sending a token is implemented for USDT on Tron only. Everything else in the Send column
> above is a NATIVE coin.

---

## The most important thing to understand

### One seed phrase → all your addresses

When you create or import a wallet, one 24-word seed phrase generates ALL your addresses:

```
Your seed phrase
  ├── Bitcoin address:    bc1q...
  ├── Ethereum address:   0xAbCd...  ← also your BSC, Polygon, Arbitrum address
  ├── Tron address:       TXyz...
  ├── Solana address:     7abc...
  ├── TON address:        UQab...
  ├── Cardano address:    addr1...
  └── Monero address:     4xyz...    ← separate 25-word mnemonic
```

**What this means for you:**
- Same ETH address works on BSC, Polygon, Arbitrum, Base, Optimism, Avalanche, Fantom, Cronos
- You don't need separate wallets for each network
- Import your MetaMask seed → all your ETH balances appear automatically
- Monero is the only exception — it uses its own 25-word format

---

## USDT — which network to use

USDT is the same dollar value everywhere. The difference is the fee.

### Fee comparison

| Network | Fee | Speed | When to use |
|---------|-----|-------|-------------|
| **Solana** | **$0.0001** | 0.4 sec | Person-to-person, small amounts |
| **Base** | **$0.001–0.05** | 2 sec | Person-to-person, Coinbase users |
| **Polygon** | **$0.001–0.01** | 2 sec | Person-to-person, widely supported |
| **BSC** | $0.01–0.10 | 3 sec | Binance exchange withdrawals |
| **Arbitrum** | $0.01–0.20 | 1 sec | ETH ecosystem users |
| **Optimism** | $0.01–0.10 | 2 sec | ETH ecosystem users |
| **TRC-20 (Tron)** | $0.50–8* | 3 sec | P2P, Asian exchanges, OTC |
| **Avalanche** | $0.01–0.30 | 2 sec | AVAX ecosystem |
| **Ethereum (ERC-20)** | **$1–15** | 15 sec | DeFi, large amounts, maximum trust |
| **TON** | $0.01–0.05 | 5 sec | Telegram users |

\* *TRC-20 explanation below — it can be free if you stake TRX*

### Why TRC-20 fee is sometimes $8 — explained simply

Tron uses an energy system:
- USDT transfer = running a smart contract = costs Energy
- Energy comes from staking TRX (locking it up)
- **Fresh wallet with no staked TRX → Tron burns ~27 TRX ≈ $7-8 as fee**
- **Wallet with 10,000+ TRX staked → USDT transfers are FREE**

Most experienced traders who use USDT-TRC20 regularly will stake TRX once and then transfer USDT for free. If you're just starting — use Solana or Polygon for cheap transfers.

### Simple decision tree

```
Are you withdrawing from an exchange?
  → Use whatever network the exchange offers
  → Most exchanges support TRC-20 and ERC-20
  → Check the exchange's minimum withdrawal amount

Are you sending to another person?
  → Both have Solana: use Solana (cheapest)
  → Both have Polygon: use Polygon (cheap, reliable)
  → Unsure: use TRC-20 (most exchanges accept it)
  → Large amount ($1000+): use ERC-20 (most trusted)

Do you want maximum privacy?
  → USDT has NO privacy on any network (all public)
  → Use Monero (XMR) for truly private transfers
```

---

## Understanding tokens vs. coins

### Coins (native)
Every blockchain has one native coin used to pay fees:
- Bitcoin → BTC (fees in BTC)
- Ethereum → ETH (fees in ETH — called "gas")
- Tron → TRX (fees from TRX energy)
- Solana → SOL (fees in SOL)
- BNB Smart Chain → BNB (fees in BNB)

### Tokens (ride on top)
Tokens use an existing blockchain and pay fees in the native coin:
- USDT on Tron → TRC-20 token, fees paid in TRX
- USDT on Ethereum → ERC-20 token, fees paid in ETH
- USDT on BSC → BEP-20 token, fees paid in BNB
- USDT on Solana → SPL token, fees paid in SOL

**Common mistake:** "I received USDT on Tron but can't send it" → You have USDT but no TRX to pay the fee. You need a small amount of TRX (or staked TRX) in the same address.

---

## Receiving crypto — step by step

### 1. Open Receive tab
Select the coin you want to receive.

### 2. Make sure you're on the right network
⚠️ **Critical:** USDT on TRC-20 and USDT on ERC-20 are different addresses.
- Your TRX address (T...) — for USDT TRC-20
- Your ETH address (0x...) — for USDT ERC-20, also BSC, Polygon, Arbitrum
- Your SOL address — for USDT SPL

### 3. Copy address or show QR
- Click "Copy" — address goes to clipboard (auto-cleared after 45 seconds)
- Show QR — sender scans with their phone or app

### 4. Confirm amount (optional)
For Bitcoin and Monero, you can include an exact amount in the QR code — the sender's wallet will auto-fill it.

### 5. Wait for confirmation
| Network | Typical confirmation time |
|---------|--------------------------|
| Solana | ~1 second |
| BSC | ~3 seconds |
| Tron | ~3 seconds |
| Polygon | ~2 seconds |
| Ethereum | 15–30 seconds |
| Bitcoin | 10–60 minutes |
| Monero | ~2 minutes |
| Cardano | ~1 minute |
| TON | ~5 seconds |

---

## Sending crypto — step by step

### 1. Open Send tab
Select the coin you want to send.

### 2. Enter the recipient address
- Paste from clipboard
- Or scan their QR code
- The wallet validates the address format and warns if it looks wrong

⚠️ **Double-check the address.** Crypto transactions are irreversible. A wrong address means lost funds — permanently.

### 3. Enter the amount
- You can enter in the coin (e.g., 0.001 BTC)
- Or in USD equivalent (e.g., $50)
- "Send all" button available (accounts for fee automatically)

### 4. Review the fee
The wallet shows:
- Network fee (paid to miners/validators, not to Umbrella)
- Total cost (amount + fee)
- Estimated confirmation time

**Fee levels available for Bitcoin and Litecoin:**
- Economy (~1h) — lowest fee, slower
- Standard (~30min) — recommended
- Priority (~10min) — highest fee, fastest

### 5. Confirm and send
- Review screen shows all details
- Your seed phrase is decrypted temporarily to sign the transaction
- Only the signed transaction bytes go to the network — your private key never leaves your device
- After broadcast, the screen shows the transaction hash

### 6. Track your transaction
Click the transaction hash to open the block explorer and see confirmation status.

---

## Automatic token detection

When you link a wallet address (ETH, TRX, SOL, or TON), the wallet automatically finds all your tokens:

**Ethereum address:**
- Checks Blockscout for all ERC-20 tokens
- Shows: USDT, USDC, DAI, SHIB, LINK, UNI, AAVE, and any other token you hold

**Tron address:**
- Checks TronScan for all TRC-20 tokens (up to 40)
- Shows: USDT, USDC, WTRX, JST, SUN, BTT, and all others

**Solana address:**
- Checks all SPL token accounts
- Shows: USDT, USDC, RAY, BONK, WIF, JTO, JUP, and all others

**TON address:**
- Checks toncenter's index for all Jettons (up to 40)
- Shows: USD₮ (Tether on TON), NOT, DOGS, and all others
- Balance only for now — Jettons are read, not sent

**Adding a custom token manually:**
1. Go to Settings → Add Token
2. Paste the contract address
3. Wallet reads the name, symbol, and decimals automatically
4. Token appears in your portfolio

---

## Privacy — honest explanation per coin

### 🟢 Monero (XMR) — maximum privacy
Every transaction hides:
- Who sent it (ring signatures — 15 decoy senders)
- Who received it (stealth addresses)
- How much was sent (RingCT — encrypted amounts)

No one, not even blockchain analysis companies, can see your balance or transactions without your ViewKey. This is why Monero is used by privacy-conscious individuals worldwide.

### 🟡 Bitcoin with Tor — medium privacy
Bitcoin is a public ledger — every transaction is visible. However:
- Umbrella routes Bitcoin queries through Tor → your IP is not linked to your address
- Fresh address per receive → harder to link transactions
- Still: anyone with your address can see your balance and full history

### 🔴 Ethereum, Tron, Solana, BSC, etc. — low privacy
Fully public blockchains. Anyone can:
- See your complete transaction history
- See your current balance
- Track movements between addresses

This is not specific to Umbrella — it's how these blockchains work. For private transfers, use Monero.

---

## Common questions

### "I sent ETH to my BSC address by mistake"
Good news: ETH and BSC use the same 0x address. Your funds are on BSC, not Ethereum.
Switch the network in your wallet to BSC to see and access them.

### "My USDT shows $0 even though I know I have it"
Check which network you're on:
- USDT on Tron → need to be viewing Tron address (T...)
- USDT on Ethereum → need to be viewing ETH address (0x...)
- The same address on different networks holds different USDT

### "Why does sending USDT on Tron cost $8?"
Your wallet has no staked TRX. See the TRC-20 fee section above.
Quick fix: stake some TRX, or use Solana USDT instead.

### "The transaction was sent but shows as pending for hours"
Bitcoin: normal during network congestion. Economy-fee transactions may wait 1-24h.
ETH: usually confirms in minutes. If stuck: the fee may have been too low during a congestion spike.
Solana: if not confirmed in 30 seconds, it was likely dropped — retry.

### "Can Umbrella see my balance or transactions?"
No. Balances are fetched directly from public blockchain nodes and explorers — no Umbrella server is involved. When Tor is enabled, even the blockchain nodes don't see your real IP.

### "What happens to my coins if Umbrella shuts down?"
Your coins are safe. They live on the blockchain, not in any app. Import your 24-word seed phrase into any BIP39-compatible wallet (MetaMask, Trust Wallet, Ledger, etc.) and you have full access. Umbrella is just an interface.

### "My Monero balance is 0 but I know I received XMR"
Monero requires scanning the blockchain with your ViewKey to find incoming transactions. Make sure the bundled monero-wallet-rpc is running (you'll see a status indicator in the app). Initial sync can take 10-60 minutes depending on how many blocks need scanning.

---

## Fees — complete reference

### How fees work

| Chain type | Fee paid in | Who sets the fee | Can you control it? |
|-----------|-------------|-----------------|---------------------|
| Bitcoin | BTC | Market (miners) | ✅ Economy / Standard / Priority |
| Ethereum | ETH | Market (validators) | ✅ Gas price selector |
| Tron TRX/TRC-20 | TRX (or Energy) | Protocol fixed | ⚠️ Stake TRX to make it free |
| Solana | SOL | Protocol fixed | ❌ Always ~$0.0001 |
| BSC | BNB | Market | ✅ Gas price |
| Polygon | MATIC | Market | ✅ Gas price |
| TON | TON | Protocol | ❌ Fixed |
| Cardano | ADA | Protocol | ❌ Calculated automatically |
| Monero | XMR | Dynamic | ✅ Priority selector |
| L2 (Arbitrum/Base/OP) | ETH | L2 sequencer | Limited |

### Fee tips

**Bitcoin:** Use Economy fee for non-urgent transfers. Save 50-70% vs Priority.

**Ethereum:** Check gas price before sending. Monday-Thursday morning UTC is usually cheapest. Avoid NFT mint rushes.

**USDT TRC-20:** Stake TRX once, send USDT free forever. Or switch to Solana.

**Monero:** Standard priority is fine for most uses. Only use high priority if urgency matters.

---

## Security — how your coins are protected

### Your private keys never leave your device

The wallet works like this:
1. Your seed phrase is encrypted with Argon2id (designed to be very slow to crack) + AES-256-GCM
2. When you unlock with your password, the seed is decrypted in memory only
3. When you send, the private key is derived temporarily, used to sign, then cleared from memory
4. Only the signed transaction (bytes) is sent to the network
5. The network cannot reverse-engineer your private key from a signed transaction

### What protects you from hackers

| Threat | Protection |
|--------|-----------|
| Someone screenshots your seed phrase | Window goes BLACK — screenshot tools see nothing |
| Malware captures your screen | Same as above — OS-level protection |
| Man-in-the-middle network attack | All connections via Tor (optional), HTTPS |
| Someone brute-forces your password | Argon2id KDF — costs $1000+ per guess on GPU |
| Exchange gets hacked | Your coins aren't on an exchange — they're in your wallet |
| Umbrella's servers get hacked | Servers never had your keys — nothing to steal |

### What you must protect yourself

| Risk | What to do |
|------|-----------|
| Losing your seed phrase | Write 24 words on paper, store securely offline |
| Weak wallet password | Use 12+ characters, mix of types |
| Sending to wrong address | Always verify the first and last 4 characters |
| Phishing (fake Umbrella site) | Always download from official GitHub only |
| Keylogger on your PC | Keep system clean, use antivirus |

---

## Coming soon — roadmap

### Near term (months 1-2)

| Feature | What it means for you |
|---------|-----------------------|
| Send any ERC-20 token | Currently send ETH native; soon send USDT, USDC, LINK, etc. on ETH |
| Send any TRC-20 token | Send any token on Tron, not just USDT |
| Send any SPL token | Send BONK, WIF, RAY etc. on Solana |
| Send on zkSync Era | Its balance already shows; sending needs the gas limit to come from the chain |

### Medium term (months 3-6)

| Feature | What it means |
|---------|--------------|
| XRP support | Fast international transfers |
| Stellar (XLM) | Ultra-cheap microtransactions |
| Cosmos (ATOM) | IBC multi-chain ecosystem |
| Coin control (BTC) | Choose exactly which UTXOs to spend — better privacy |
| Zcash shielded | Send with zero-knowledge proofs |
| Duress password | Open decoy wallet if forced to unlock |

### Long term

| Feature | What it means |
|---------|--------------|
| Ledger hardware wallet | Sign transactions with physical device |
| Android app | Mobile wallet (same seed, full features) |
| Silent Payments (BTC) | Bitcoin privacy without address reuse |
| Multisig | 2-of-3 signatures for shared control |

---

## Full supported chains list

| # | Chain | Symbol | Status | What works |
|---|-------|--------|--------|-----------|
| 1 | Bitcoin | BTC | ✅ Stable | Send, receive, balance, history |
| 2 | Ethereum | ETH | ✅ Stable | Send, receive, balance, history, ERC-20 |
| 3 | BNB Smart Chain | BNB | ✅ Working | Send, receive, balance, BEP-20 |
| 4 | Polygon | MATIC | ✅ Working | Send, receive, balance, ERC-20 |
| 5 | Tron | TRX | ✅ Stable | Send, receive, balance, history, TRC-20 |
| 6 | Solana | SOL | ✅ Working | Send, receive, balance, history, SPL |
| 7 | TON | TON | ✅ Working | Send, receive, balance, history, Jetton balances |
| 8 | Avalanche | AVAX | ✅ Working | Send, receive, balance, ERC-20 |
| 9 | Arbitrum | ETH | ✅ Working | Send, receive, balance, ERC-20 |
| 10 | Base | ETH | ✅ Working | Send, receive, balance, ERC-20 |
| 11 | Optimism | ETH | ✅ Working | Send, receive, balance, ERC-20 |
| 12 | Fantom | FTM | ✅ Working | Send, receive, balance, ERC-20 |
| 13 | Cronos | CRO | ✅ Working | Send, receive, balance, ERC-20 |
| 14 | Cardano | ADA | ✅ Working | Send, receive, balance, history |
| 15 | Monero | XMR | ✅ Working | Send, receive, balance, history, private |
| 16 | Litecoin | LTC | ✅ Stable | Send, receive, balance, history |
| 17 | Bitcoin Cash | BCH | ✅ Working | Send, receive, balance, history |
| 18 | Dogecoin | DOGE | ✅ Working | Send, receive, balance |
| 19 | Zcash | ZEC | ⚠️ Partial | Receive + balance only |
| 20 | Linea | ETH | ✅ Working | Send, receive, balance, ERC-20 |
| 21 | zkSync Era | ETH | ⚠️ Partial | Receive + balance only |
| 22 | XRP Ledger | XRP | 📅 Planned | — |
| 23 | Stellar | XLM | 📅 Planned | — |
| 24 | Cosmos | ATOM | 📅 Planned | — |
| 25 | Near | NEAR | 📅 Planned | — |
| 26 | Polkadot | DOT | 📅 Planned | — |
