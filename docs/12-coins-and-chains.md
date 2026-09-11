# 12 — Coins, Chains & Token Support

> Complete reference for every blockchain, native coin, and token.
> Covers Top-100 CoinMarketCap assets + all major stablecoins.

---

## One seed → all chains

```
BIP39 mnemonic (24 words)
  └── BIP32 master key
        ├── m/44'/0'/0'/0/n    → Bitcoin (BTC)
        ├── m/84'/0'/0'/0/n    → Bitcoin native SegWit
        ├── m/44'/60'/0'/0/n   → ETH + ALL EVM chains (same address)
        │                        Polygon, BSC, Arbitrum, Optimism,
        │                        Base, Avalanche, Fantom, Cronos,
        │                        zkSync, Linea, Scroll, Mantle...
        ├── m/44'/195'/0'/0/n  → Tron (TRX + all TRC-20)
        ├── SLIP-0010 /501'    → Solana (SOL + all SPL)
        ├── m/44'/145'/0'/0/n  → Bitcoin Cash (BCH)
        ├── m/44'/2'/0'/0/n    → Litecoin (LTC)
        ├── m/44'/3'/0'/0/n    → Dogecoin (DOGE)
        ├── m/44'/133'/0'/0/n  → Zcash (ZEC) transparent
        ├── m/44'/144'/0'/0/n  → Ripple (XRP)
        ├── m/44'/148'/0'      → Stellar (XLM)
        ├── m/44'/1815'/0'/0/n → Cardano (ADA)
        ├── m/44'/60'/0'/0/n   → Avalanche C-Chain (EVM)
        ├── TON-specific path  → TON + Jettons
        └── Monero 25-word     → XMR (own format)
```

---

## Layer 1 — Native coins

### Bitcoin (BTC)
```
Derivation:  m/84'/0'/0'/0/n (SegWit, recommended)
             m/44'/0'/0'/0/n (legacy P2PKH)
Address:     bc1q... (SegWit) / 1... (legacy) / 3... (P2SH)
Library:     NBitcoin
Balance:     Blockstream Esplora, Blockbook, Haskoin (via Tor)
Fee:         sat/vbyte — Low/Medium/High presets
Privacy:     🟡 Medium (public UTXO — use coin control)
Tokens:      BRC-20 (Ordinals) — planned
```

### Ethereum (ETH)
```
Derivation:  m/44'/60'/0'/0/n
Address:     0x... (EIP-55)
Library:     Nethereum
RPC:         Infura / Alchemy / public (via Tor)
Fee:         EIP-1559 baseFee + tip
Privacy:     🔴 Low (fully public)
Tokens:      All ERC-20 (see token section)
```

### Binance Coin / BNB
```
Chain:       BNB Smart Chain (BSC) — EVM compatible
Derivation:  m/44'/60'/0'/0/n (same as ETH)
Address:     0x... (same address as ETH)
RPC:         bsc-dataseed.binance.org (via Tor)
Fee:         ~$0.01–0.10
Tokens:      All BEP-20 (USDT, USDC, BUSD, CAKE, etc.)
```

### Solana (SOL)
```
Derivation:  SLIP-0010 m/44'/501'/0'/0 (ed25519)
Address:     base58 44 chars
Library:     Custom ed25519 + Solana RPC
Fee:         ~0.000005 SOL ($0.0001)
Privacy:     🔴 Low
Tokens:      All SPL tokens (USDT, USDC, RAY, BONK, JTO, WIF...)
```

### XRP (Ripple)
```
Derivation:  m/44'/144'/0'/0/n (secp256k1)
Address:     r... (base58)
Fee:         0.00001 XRP
Reserve:     10 XRP to activate address (protocol requirement)
Privacy:     🔴 Low
Library:     xrpl.js / custom C# port
```

### Cardano (ADA)
```
Derivation:  m/1852'/1815'/0'/0/0 (Shelley)
Address:     addr1... (bech32)
Library:     Custom (BouncyCastle ed25519 + Blake2b)
Balance:     Koios API (via Tor)
Fee:         ~0.17–0.5 ADA
Privacy:     🔴 Low
Tokens:      Native Cardano assets
```

### Avalanche (AVAX)
```
C-Chain:     EVM — same address as ETH (m/44'/60'/0'/0/n)
X-Chain:     m/44'/9000'/0'/0/n (Secp256k1) — planned
P-Chain:     Same as X-Chain — planned
RPC:         api.avax.network/ext/bc/C/rpc (via Tor)
Fee:         $0.01–0.30
Tokens:      All ERC-20 on C-Chain (USDT, USDC, etc.)
```

### Polygon (MATIC / POL)
```
Derivation:  m/44'/60'/0'/0/n (EVM — same as ETH)
Address:     0x... (same as ETH address)
RPC:         polygon-rpc.com / Alchemy (via Tor)
Fee:         $0.001–0.05
Privacy:     🔴 Low
Tokens:      USDT, USDC, WETH, WBTC, LINK + all ERC-20
```

### Tron (TRX)
```
Derivation:  m/44'/195'/0'/0/n
Address:     T... (34 chars, base58)
Fee:         Energy + Bandwidth model
Privacy:     🔴 Low
Tokens:      All TRC-20 (see USDT section)
```

### Polkadot (DOT)
```
Derivation:  m/44'/354'/0'/0/n (sr25519)
Address:     1... (SS58 format)
Library:     Planned (sr25519 is non-standard)
Fee:         ~$0.01–0.10
Tokens:      Parachain assets
Status:      🟡 Planned
```

### Cosmos (ATOM)
```
Derivation:  m/44'/118'/0'/0/n
Address:     cosmos1... (bech32)
Fee:         ~$0.01
Tokens:      IBC tokens (OSMO, JUNO, etc.)
Status:      🟡 Planned
```

### Near Protocol (NEAR)
```
Derivation:  m/44'/397'/0'/0/n (ed25519)
Address:     alice.near (human-readable) or hex
Fee:         ~$0.001
Status:      🟡 Planned
```

### Aptos (APT)
```
Derivation:  m/44'/637'/0'/0/n (ed25519)
Address:     0x... (hex, 32 bytes)
Fee:         ~$0.001
Status:      🟡 Planned
```

### Sui (SUI)
```
Derivation:  m/44'/784'/0'/0/n (ed25519)
Address:     0x... (hex)
Fee:         ~$0.001
Status:      🟡 Planned
```

### TON (Toncoin)
```
Derivation:  TON-specific (wallet v4R2)
Address:     UQ... (user-friendly) / EQ... (bounceable)
Library:     Custom TonCell builder (CRC32c)
Fee:         0.003–0.05 TON
Tokens:      Jettons (USDT, NOT, STON, etc.)
Import:      24-word TON mnemonic supported
Privacy:     🔴 Low (linked to Telegram often)
```

### Litecoin (LTC)
```
Derivation:  m/44'/2'/0'/0/n
Address:     L... (legacy) / ltc1... (SegWit)
Fee:         ~$0.001
Privacy:     🟡 Medium
Library:     NBitcoin.Altcoins
```

### Bitcoin Cash (BCH)
```
Derivation:  m/44'/145'/0'/0/n
Address:     bitcoincash:q... or 1...
Fee:         ~$0.001
Privacy:     🟡 Medium (UTXO like BTC)
Library:     NBitcoin.Altcoins
```

### Dogecoin (DOGE)
```
Derivation:  m/44'/3'/0'/0/n
Address:     D...
Fee:         1 DOGE minimum (~$0.10)
Status:      ⚠️ Receive + balance only (send in roadmap)
Library:     NBitcoin.Altcoins
```

### Monero (XMR) — Privacy coin
```
Seed:        25-word Monero mnemonic (own format)
Keys:        SpendKey (encrypted) + ViewKey
Engine:      monero-wallet-rpc (official, bundled)
Node:        Configurable (Tor hidden service nodes available)
Fee:         Dynamic (~$0.01–0.50)
Privacy:     🟢 Maximum
             Ring signatures — sender hidden
             Stealth addresses — receiver hidden
             RingCT — amount hidden
Ring size:   16 (default, mandatory since v14)
Subaddress:  New address per receive (prevents linking)
```

### Zcash (ZEC) — Privacy coin
```
Derivation:  ZIP-32 (own HD format)
Address:     t... (transparent) / z... (shielded sapling)
             ua... (unified address — planned)
Privacy:     🟢 High (shielded) / 🔴 Low (transparent)
Fee:         ~$0.001 + shielding cost
Status:      🟡 Planned (lightwalletd integration)
```

### Stellar (XLM)
```
Derivation:  SLIP-0010 m/44'/148'/0'
Address:     G... (Stellar public key, 56 chars)
Fee:         0.00001 XLM ($0.000001)
Tokens:      Stellar assets (USDC, yXLM, etc.)
Status:      🟡 Planned
```

### Ripple (XRP)
```
Derivation:  m/44'/144'/0'/0/n
Address:     r... (25–34 chars, base58)
Fee:         0.00001 XRP
Reserve:     10 XRP minimum to activate
Status:      🟡 Planned
```

### Algorand (ALGO)
```
Derivation:  SLIP-0010 ed25519
Address:     58-char base32 + checksum
Fee:         0.001 ALGO
Tokens:      ASA (Algorand Standard Assets)
Status:      🟡 Planned
```

### Hedera (HBAR)
```
Derivation:  m/44'/3030'/0'/0/n
Address:     0.0.XXXX (account ID format)
Fee:         $0.0001
Status:      🟡 Planned
```

---

## Layer 2 / Rollups (all EVM — easy to add)

All L2s use same ETH address and same Nethereum library.
Adding = change RPC URL + chain ID. ~2 hours per chain.

| Chain | Chain ID | RPC | Typical fee |
|-------|----------|-----|-------------|
| **Arbitrum One** | 42161 | arb1.arbitrum.io | $0.01–0.20 |
| **Optimism** | 10 | mainnet.optimism.io | $0.01–0.10 |
| **Base** | 8453 | mainnet.base.org | $0.001–0.05 |
| **zkSync Era** | 324 | mainnet.era.zksync.io | $0.01–0.20 |
| **Linea** | 59144 | rpc.linea.build | $0.01–0.10 |
| **Scroll** | 534352 | rpc.scroll.io | $0.01–0.10 |
| **Mantle** | 5000 | rpc.mantle.xyz | $0.001–0.05 |
| **Polygon zkEVM** | 1101 | zkevm-rpc.com | $0.01–0.20 |
| **Celo** | 42220 | forno.celo.org | $0.001 |
| **Gnosis** | 100 | rpc.gnosischain.com | $0.001 |
| **Fantom** | 250 | rpc.ftm.tools | $0.001 |
| **Cronos** | 25 | evm.cronos.org | $0.01 |
| **Moonbeam** | 1284 | rpc.api.moonbeam.network | $0.01 |

---

## Stablecoins — all networks

### USDT (Tether)

| Network | Contract | Fee | Notes |
|---------|----------|-----|-------|
| **TRC-20 (Tron)** | TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t | $0.50–8 | Most popular P2P |
| **ERC-20 (Ethereum)** | 0xdAC17F958D2ee523a2206206994597C13D831ec7 | $1–15 | DeFi standard |
| **BEP-20 (BSC)** | 0x55d398326f99059fF775485246999027B3197955 | $0.01–0.10 | Cheap |
| **Polygon** | 0xc2132D05D31c914a87C6611C10748AEb04B58e8F | $0.001–0.01 | Very cheap |
| **Solana (SPL)** | Es9vMFrzaCERmJfrF4H2FYD4KCoNkY11McCe8BenwNYB | $0.0001 | Fastest+cheapest |
| **Arbitrum** | 0xFd086bC7CD5C481DCC9C85ebE478A1C0b69FCbb9 | $0.01–0.20 | |
| **Optimism** | 0x94b008aA00579c1307B0EF2c499aD98a8ce58e58 | $0.01–0.10 | |
| **Avalanche** | 0x9702230A8Ea53601f5cD2dc00fDBc13d4dF4A8c7 | $0.01–0.30 | |
| **Base** | 0xfde4C96c8593536E31F229EA8f37b2ADa2699bb2 | $0.001–0.05 | |
| **TON (Jetton)** | EQCxE6mUtQJKFnGfaROTKOt1lZbDiiX1kCixRv7Nw2Id_sDs | $0.01–0.05 | Telegram users |

#### Why TRC-20 fee varies $0.50–8
```
USDT TRC-20 = smart contract call
Requires Energy (obtained by staking TRX)
No staked TRX → Tron burns TRX instead

Fresh wallet + fresh recipient = ~27,000 Energy
At 0 staked TRX → burns ~27 TRX ≈ $7–8

Solution: stake 10,000+ TRX once = unlimited free transfers
Show this tip to user when TRX balance < 30 TRX
```

### USDC (Circle)

| Network | Contract | Fee |
|---------|----------|-----|
| Ethereum | 0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48 | $1–15 |
| Solana | EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v | $0.0001 |
| Polygon | 0x2791Bca1f2de4661ED88A30C99A7a9449Aa84174 | $0.001 |
| Arbitrum | 0xaf88d065e77c8cC2239327C5EDb3A432268e5831 | $0.01 |
| Base | 0x833589fCD6eDb6E08f4c7C32D4f71b54bdA02913 | $0.001 |
| Avalanche | 0xB97EF9Ef8734C71904D8002F8b6Bc66Dd9c48a6E | $0.01 |
| TON | EQD0vdSA_NedR9uvbgN9EikRX-suesDxGeFg69XQMavfLqIw | $0.01 |

### DAI (MakerDAO)
```
Ethereum:  0x6B175474E89094C44Da98b954EedeAC495271d0F
Polygon:   0x8f3Cf7ad23Cd3CaDbD9735AFf958023239c6A063
Arbitrum:  0xDA10009cBd5D07dd0CeCc66161FC93D7c9000da1
BSC:       0x1AF3F329e8BE154074D8769D1FFa4eE058B1DBc3
```

### BUSD / FDUSD / TUSD / PYUSD
```
BUSD:   BSC 0xe9e7CEA3DedcA5984780Bafc599bD69ADd087D56
FDUSD:  Ethereum 0xc5f0f7b66764F6ec8C8Dff7BA683102295E16409
TUSD:   Ethereum 0x0000000000085d4780B73119b644AE5ecd22b376
PYUSD:  Ethereum 0x6c3ea9036406852006290770BEdFcAbA0e23A0e8
```

---

## Top DeFi & ecosystem tokens (ERC-20 unless noted)

### DeFi Blue Chips
```
UNI   (Uniswap)     0x1f9840a85d5aF5bf1D1762F925BDADdC4201F984
AAVE  (Aave)        0x7Fc66500c84A76Ad7e9c93437bFc5Ac33E2DDaE9
CRV   (Curve)       0xD533a949740bb3306d119CC777fa900bA034cd52
COMP  (Compound)    0xc00e94Cb662C3520282E6f5717214004A7f26888
MKR   (MakerDAO)    0x9f8F72aA9304c8B593d555F12eF6589cC3A579A2
SNX   (Synthetix)   0xC011a73ee8576Fb46F5E1c5751cA3B9Fe0af2a6F
YFI   (Yearn)       0x0bc529c00C6401aEF6D220BE8C6Ea1667F6Ad93e
1INCH (1inch)       0x111111111117dC0aa78b770fA6A738034120C302
BAL   (Balancer)    0xba100000625a3754423978a60c9317c58a424e3D
```

### Layer 2 tokens
```
ARB   (Arbitrum)    0xB50721BCf8d664c30412Cfbc6cf7a15145234ad1
OP    (Optimism)    0x4200000000000000000000000000000000000042
MATIC (Polygon)     0x7D1AfA7B718fb893dB30A3aBc0Cfc608AaCfeBB0
MNT   (Mantle)      0x3c3a81e81dc49A522A592e7622A7E711c06bf354
```

### Exchange tokens
```
BNB   — native BSC coin
CRO   (Cronos)      0xA0b73E1Ff0B80914AB6fe0444E65848C4C34450b
OKB   (OKX)         0x75231F58b43240C9718Dd58B4967c5114342a86c
GT    (Gate.io)      on Gate chain
```

### Privacy coins
```
XMR   (Monero)      — own blockchain, full support
ZEC   (Zcash)       — own blockchain, planned
DASH  (Dash)        — own blockchain, planned
SCRT  (Secret)      — Cosmos-based, planned
```

### Meme coins
```
DOGE  — own blockchain (NBitcoin.Altcoins)
SHIB  0x95aD61b0a150d79219dCF64E1E6Cc01f0B64C4cE (ERC-20)
PEPE  0x6982508145454Ce325dDbE47a25d4ec3d2311933 (ERC-20)
FLOKI 0xcf0C122c6b73ff809C693DB761e7BaeBe62b6a2E (ERC-20)
BONK  DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263 (Solana)
WIF   EKpQGSJtjMFqKZ9KQanSqYXRcF8fBopzLHYxdM65zcjm (Solana)
```

### Gaming & Metaverse
```
AXS   (Axie)         0xBB0E17EF65F82Ab018d8EDd776e8DD940327B28b
SAND  (Sandbox)      0x3845badAde8e6dFF049820680d1F14bD3903a5d0
MANA  (Decentraland) 0x0F5D2fB29fb7d3CFeE444a200298f468908cC942
ENJ   (Enjin)        0xF629cBd94d3791C9250152BD8dfBDF380E2a3B9c
GALA  (Gala)         0x15D4c048F83bd7e37d49eA4C83a07267Ec4203dA
```

### AI tokens
```
FET   (Fetch.ai)     0xaea46A60368A7bD060eec7DF8CBa43b7EF41Ad85
AGIX  (SingularityNET) 0x5B7533812759B45C2B44C19e320ba2cD2681b542
OCEAN (Ocean)        0x967da4048cD07aB37855c090aAF366e4ce1b9F48
WLD   (Worldcoin)    0x163f8C2467924be0ae7B5347228CABF260318753
```

### Infrastructure
```
LINK  (Chainlink)    0x514910771AF9Ca656af840dff83E8264EcF986CA
GRT   (The Graph)    0xc944E90C64B2c07662A292be6244BDf05Cda44a7
FIL   (Filecoin)     — own blockchain, planned
AR    (Arweave)      — own blockchain, planned
```

---

## Full chain support matrix

| # | Chain | Status | Coins/Tokens |
|---|-------|--------|-------------|
| 1 | Bitcoin | ✅ | BTC, BRC-20 (planned) |
| 2 | Ethereum | ✅ | ETH + all ERC-20 |
| 3 | BNB Smart Chain | 🟡 Planned | BNB + all BEP-20 |
| 4 | Solana | ✅ | SOL + all SPL |
| 5 | XRP Ledger | 🟡 Planned | XRP |
| 6 | Tron | ✅ | TRX + all TRC-20 |
| 7 | Cardano | ✅ | ADA + native assets |
| 8 | Avalanche | 🟡 Planned | AVAX + ERC-20 |
| 9 | Polygon | 🟡 Planned | MATIC/POL + ERC-20 |
| 10 | Polkadot | 🟡 Planned | DOT + parachains |
| 11 | TON | ✅ | TON + Jettons |
| 12 | Arbitrum | 🟡 Planned | ETH + ERC-20 |
| 13 | Optimism | 🟡 Planned | ETH + ERC-20 |
| 14 | Base | 🟡 Planned | ETH + ERC-20 |
| 15 | Litecoin | ✅ | LTC |
| 16 | Monero | ✅ | XMR (full privacy) |
| 17 | Cosmos | 🟡 Planned | ATOM + IBC tokens |
| 18 | Near | 🟡 Planned | NEAR |
| 19 | Algorand | 🟡 Planned | ALGO + ASA |
| 20 | Stellar | 🟡 Planned | XLM |
| 21 | Bitcoin Cash | ✅ (partial) | BCH |
| 22 | Dogecoin | ✅ (receive) | DOGE |
| 23 | Zcash | 🟡 Planned | ZEC |
| 24 | Aptos | 🟡 Planned | APT |
| 25 | Sui | 🟡 Planned | SUI |
| 26 | zkSync Era | 🟡 Planned | ETH + ERC-20 |
| 27 | Fantom | 🟡 Planned | FTM + ERC-20 |
| 28 | Cronos | 🟡 Planned | CRO + ERC-20 |
| 29 | Linea | 🟡 Planned | ETH + ERC-20 |
| 30 | Hedera | 🟡 Planned | HBAR |

---

## Token detection logic

```
On address add:
  1. Fetch native balance
  2. Query top-50 tokens on that chain
  3. Show tokens with balance > 0
  4. Cache results 60 seconds

On custom token add:
  User enters contract address →
  Wallet calls: name(), symbol(), decimals() →
  Validates it's a real token contract →
  Adds to watchlist

Price feed:
  CoinGecko /simple/price (via Tor, 60s cache)
  Fallback: last known price
  Display: USD + user fiat (UAH/EUR/etc.)
```

---

## Which USDT to use — guide for UI

```
Sending to exchange?
  → Use the network the exchange lists
    (TRC-20 or ERC-20 most common)
  → Check minimum deposit amount

Sending person-to-person?
  → Solana SPL:  $0.0001  ← cheapest + fastest
  → Polygon:     $0.01    ← cheap, widely supported
  → TRC-20:      free if TRX staked, $8 if not
  → ERC-20:      $1–15    ← most universal, expensive

Want privacy?
  → No USDT is private (all public blockchains)
  → Convert: USDT → XMR → send → convert back
  → Or use Monero directly
```

---

## Privacy rating

| Chain | Rating | Reason |
|-------|--------|--------|
| Monero (XMR) | 🟢 Maximum | Ring sigs + stealth addr + RingCT |
| Zcash shielded | 🟢 High | zk-SNARKs |
| Bitcoin + coin control | 🟡 Medium | UTXO — manageable with care |
| Litecoin | 🟡 Medium | Same as BTC |
| Everything else | 🔴 Low | Fully public ledger |

Show this rating on every send screen.
