# 12 — Coins, Chains & Token Support

> Complete reference for every blockchain, coin, and token in Umbrella Wallet.
> Based on the actual code in `ChainCatalog.cs`, `PublicChainClients.cs` and
> `Infrastructure/Network/*.cs`.
>
> **`ChainCatalog` is the source of truth.** This page is prose and can drift; the flags the
> wallet itself obeys — `CanSend`, `CanSyncBalance`, `HasHistory` — live there. Where the two
> disagree, the catalog is right and this page is the bug.

---

## Current status — read from the code

| Chain | Symbol | Send | Receive | Balance | History | Tokens | Maturity |
|-------|--------|:----:|:-------:|:-------:|:-------:|:------:|---------|
| Bitcoin | BTC | ✅ | ✅ | ✅ | ✅ | ❌ | Stable |
| Ethereum | ETH | ✅ | ✅ | ✅ | ✅ | ✅ ERC-20 | Stable |
| Litecoin | LTC | ✅ | ✅ | ✅ | ✅ | ❌ | Stable |
| Dogecoin | DOGE | ✅ | ✅ | ✅ | ❌ | ❌ | Beta |
| Tron | TRX | ✅ | ✅ | ✅ | ✅ | ✅ TRC-20 | Stable |
| Solana | SOL | ✅ | ✅ | ✅ | ✅ | ✅ SPL | Beta |
| TON | TON | ✅ | ✅ | ✅ | ✅ | ❌ Jettons planned | Beta |
| Cardano | ADA | ✅ | ✅ | ✅ | ✅ | ❌ | Beta |
| Monero | XMR | ✅ | ✅ | ✅ | ✅ | ❌ | Beta |
| Bitcoin Cash | BCH | ✅ | ✅ | ✅ | ✅ | ❌ | Beta |
| Zcash | ZEC | ❌ | ✅ | ✅ | ❌ | ❌ | Beta |

### EVM side-chains (same ETH address, balance already fetched)

Already in `PublicChainClients.cs` — `EvmSideChains` array.
These show native balance automatically when ETH address is linked:

| Network | Symbol | Balance fetch | Send | Status |
|---------|--------|:-------------:|:----:|--------|
| BNB Smart Chain | BNB | ✅ live | ❌ needs ChainId | Add ChainId.Bsc |
| Polygon | MATIC | ✅ live | ❌ needs ChainId | Add ChainId.Polygon |
| Avalanche | AVAX | ✅ live | ❌ needs ChainId | Add ChainId.Avax |
| Fantom | FTM | ✅ live | ❌ needs ChainId | Add ChainId.Ftm |
| Cronos | CRO | ✅ live | ❌ needs ChainId | Add ChainId.Cro |
| Arbitrum | ETH | ✅ live | ❌ needs ChainId | Add ChainId.Arbitrum |
| Optimism | ETH | ✅ live | ❌ needs ChainId | Add ChainId.Optimism |
| Base | ETH | ✅ live | ❌ needs ChainId | Add ChainId.Base |

> **Note:** Balance fetch is already implemented. To enable full support
> (send + dedicated address tab) takes ~2 hours per chain — add ChainId enum value,
> ChainInfo entry, and EthTransactionSender routing for that chain ID + RPC.

---

## One seed → all chains

```
BIP39 mnemonic (24 words)
  └── BIP32 master key
        ├── m/84'/0'/0'/0/n     → Bitcoin native SegWit (bc1q...)
        ├── m/44'/0'/0'/0/n     → Bitcoin legacy (1...)
        ├── m/44'/60'/0'/0/n    → Ethereum + ALL EVM (same 0x address)
        │                         BSC, Polygon, Avalanche, Fantom, Cronos,
        │                         Arbitrum, Optimism, Base, zkSync, Linea,
        │                         Scroll, Mantle, Celo, Gnosis, Moonbeam...
        ├── m/44'/195'/0'/0/n   → Tron (T... address) + all TRC-20
        ├── SLIP-0010 /501'     → Solana (base58) + all SPL tokens
        ├── m/44'/145'/0'/0/n   → Bitcoin Cash (bitcoincash:q...)
        ├── m/44'/2'/0'/0/n     → Litecoin (L... / ltc1...)
        ├── m/44'/3'/0'/0/n     → Dogecoin (D...)
        ├── m/44'/133'/0'/0/n   → Zcash transparent (t...)
        ├── m/1852'/1815'/0'/0  → Cardano Shelley (addr1...)
        ├── TON-specific path   → TON wallet v4R2 (UQ...)
        ├── m/44'/144'/0'/0/n   → XRP (r...) — planned
        ├── m/44'/148'/0'       → Stellar (G...) — planned
        ├── m/44'/118'/0'/0/n   → Cosmos (cosmos1...) — planned
        └── Monero 25-word seed → XMR (4...) — separate vault entry
```

**Key insight for users:** Your ETH address `0xAbCd...` is simultaneously your
BSC, Polygon, Arbitrum, Optimism, Base, Avalanche, Fantom address. One address,
all EVM chains. Just add the network.

---

## Layer 1 chains — detailed

### Bitcoin (BTC)
```
Derivation:   m/84'/0'/0'/0/n (SegWit, default)
              m/44'/0'/0'/0/n (legacy P2PKH)
Address:      bc1q... (SegWit) | 1... (legacy) | 3... (P2SH)
Library:      NBitcoin
Balance:      Blockstream Esplora (primary, via Tor)
              Blockbook (fallback)
              HaskoinUtxoExplorer (fallback)
Broadcast:    Blockstream + mempool.space
Fee:          sat/vbyte — Low / Medium / High selector
Privacy:      🟡 Medium — public UTXO ledger
              Tip: use fresh address per receive + Tor
Maturity:     Stable ✅
```

### Ethereum (ETH) + all ERC-20
```
Derivation:   m/44'/60'/0'/0/n
Address:      0x... (EIP-55 checksum)
Library:      Nethereum (signing, encoding)
RPC:          cloudflare-eth.com / rpc.ankr.com/eth / eth.drpc.org
              (tried in order, all via Tor)
Tokens:       Fetched via Blockscout public API (keyless)
              USDT, USDC, DAI, WETH, WBTC, LINK, UNI, AAVE...
              Any ERC-20 by contract address
Fee:          EIP-1559 — baseFee + priority tip (in GWEI)
Privacy:      🔴 Low — fully public
Maturity:     Stable ✅
```

### BNB Smart Chain (BSC)
```
Address:      0x... (same as ETH — EVM compatible)
Derivation:   m/44'/60'/0'/0/n (identical to ETH)
RPC:          bsc-dataseed.binance.org (primary)
              bsc-dataseed1.defibit.io (fallback)
              rpc.ankr.com/bsc (fallback)
Native coin:  BNB
Tokens:       All BEP-20 — USDT, USDC, BUSD, CAKE, WBNB...
Fee:          $0.01–0.10
Balance:      ✅ Already fetched in GetEvmSideBalancesAsync
Send:         🟡 Add ChainId.Bsc + routing in EthTransactionSender
Maturity:     Ready to enable (~2h work)
```

### Polygon (MATIC / POL)
```
Address:      0x... (same as ETH)
Derivation:   m/44'/60'/0'/0/n
RPC:          polygon-rpc.com (primary)
              rpc.ankr.com/polygon (fallback)
Native coin:  MATIC / POL
Tokens:       All ERC-20 on Polygon — USDT, USDC, WETH, WBTC...
Fee:          $0.001–0.05 (very cheap)
Balance:      ✅ Already fetched in GetEvmSideBalancesAsync
Send:         🟡 Add ChainId.Polygon
Maturity:     Ready to enable (~2h work)
```

### Tron (TRX) + TRC-20 tokens
```
Derivation:   m/44'/195'/0'/0/n (secp256k1)
Address:      T... (34 chars, base58check with 0x41 prefix)
Library:      Custom Tron signer (BouncyCastle secp256k1)
Balance:      apilist.tronscanapi.com (via Tor)
Tokens:       ALL TRC-20 fetched via GetTronTokensAsync (up to 40)
              USDT, USDC, WTRX, JST, SUN, WIN, BTT, TUSD...
Fee:          Energy + Bandwidth model (see USDT section)
Broadcast:    TronGrid API
Privacy:      🔴 Low — public ledger
Maturity:     Stable ✅
```

### Solana (SOL) + SPL tokens
```
Derivation:   SLIP-0010 m/44'/501'/0'/{index}' (ed25519)
Address:      base58 (32-byte pubkey, ~44 chars)
Library:      Custom ed25519 + Solana JSON-RPC
Balance:      api.mainnet-beta.solana.com (via Tor)
              getBalance → lamports / 1e9
Tokens:       SPL tokens — USDT, USDC, RAY, BONK, WIF, JTO...
Fee:          ~0.000005 SOL ($0.0001) — cheapest of all
Broadcast:    sendTransaction via Solana RPC
Privacy:      🔴 Low
Maturity:     Beta ⚠️ (rate-limited public RPC)
```

### Cardano (ADA)
```
Derivation:   m/1852'/1815'/0'/0/0 (CIP-1852 Icarus, BIP32-Ed25519)
Address:      addr1... (Shelley bech32)
Library:      Custom BouncyCastle ed25519 + Blake2b-224
Balance:      api.koios.rest (via Tor) — POST address_info
              Returns lovelace / 1e6
Broadcast:    Koios submit_transaction
Fee:          ~0.17–0.5 ADA (min UTXO rules apply)
Privacy:      🔴 Low
Maturity:     Beta ⚠️
```

### Monero (XMR) — full privacy
```
Seed:         25-word Monero mnemonic (own format, NOT BIP39)
              Stored as separate vault entry, encrypted same way
Keys:         SpendKey (encrypted in vault)
              ViewKey (derived, used for balance scanning)
Engine:       monero-wallet-rpc (official binary, bundled in installer)
Node:         Configurable — local monerod or remote
              Recommended: Tor hidden service Monero nodes
Balance:      monero-wallet-rpc scan using ViewKey
Fee:          Dynamic ~$0.01–0.50 (ring size 16 = default)
Privacy:      🟢 Maximum
              Ring signatures — sender hidden (16 decoys)
              Stealth addresses — receiver hidden
              RingCT — amount hidden
              Subaddresses — new address per receive
Maturity:     Beta ⚠️ (requires rpc service running)
```

### TON (Toncoin) + Jettons
```
Derivation:   TON-specific (SLIP-0010 ed25519, wallet v4R2)
Address:      UQ... (user-friendly bounceable)
              EQ... (non-bounceable)
Library:      Custom TonCell builder + CRC32c hash
Balance:      toncenter.com API v2 (via Tor)
Broadcast:    toncenter sendBoc
Fee:          0.003–0.05 TON
Tokens:       Jettons — USDT (EQCxE6mu...), NOT, STON, etc.
              ⚠️ Jetton balance display in roadmap
Import:       24-word TON mnemonic supported (Tonkeeper compatible)
Privacy:      🔴 Low (often linked to Telegram accounts)
Maturity:     Beta ⚠️
```

### Litecoin (LTC)
```
Derivation:   m/84'/2'/0'/0/n (SegWit, recommended)
Address:      ltc1... (SegWit) / L... (legacy)
Library:      NBitcoin.Altcoins
Balance:      litecoinspace.org Esplora API (via Tor)
Fee:          ~$0.001 (very cheap)
Privacy:      🟡 Medium (UTXO like Bitcoin)
Maturity:     Stable ✅
```

### Bitcoin Cash (BCH)
```
Derivation:   m/44'/145'/0'/0/n (P2PKH)
Address:      bitcoincash:q... (CashAddr) / 1... (legacy)
Library:      NBitcoin.Altcoins (SIGHASH_FORKID signing)
Balance:      api.haskoin.com (primary, via Tor)
              Blockchair (fallback, rate-limited)
Fee:          ~$0.001
Privacy:      🟡 Medium (UTXO model)
Maturity:     Beta ⚠️ (verify with small amount first)
```

### Dogecoin (DOGE)
```
Derivation:   m/44'/3'/0'/0/n
Address:      D...
Library:      NBitcoin.Altcoins
Balance:      api.blockcypher.com (via Tor)
Fee:          1 DOGE minimum (~$0.10)
Send:         ✅ Real UTXO spend (BlockCypher UTXOs + broadcast)
History:      ❌ Not yet implemented
Maturity:     Beta ⚠️
```

### Zcash (ZEC) — transparent only
```
Derivation:   m/44'/133'/0'/0/n (P2PKH)
Address:      t1... (transparent P2PKH)
              ⚠️ NOT shielded z-addr (different scheme)
Balance:      Trezor Blockbook: zec1.trezor.io (primary)
              Blockchair (fallback)
Send:         ❌ Not yet — needs UTXO + signing implementation
History:      ❌ Not yet
Privacy:      🔴 Low (transparent addr = public ledger)
Note:         Shielded z-addr support planned separately
Maturity:     Beta ⚠️
```

### Avalanche (AVAX)
```
C-Chain:      EVM — same address as ETH (m/44'/60'/0'/0/n)
RPC:          api.avax.network/ext/bc/C/rpc (primary)
              rpc.ankr.com/avalanche (fallback)
Native:       AVAX
Tokens:       All ERC-20 on C-Chain — USDT, USDC, WETH...
Fee:          $0.01–0.30
Balance:      ✅ Already in GetEvmSideBalancesAsync
Send:         🟡 Add ChainId.Avax
```

### Fantom (FTM)
```
Address:      0x... (EVM)
RPC:          rpc.ftm.tools / rpc.ankr.com/fantom
Fee:          ~$0.001
Balance:      ✅ Already fetched
Send:         🟡 Add ChainId.Ftm
```

### Cronos (CRO)
```
Address:      0x... (EVM)
RPC:          evm.cronos.org / cronos-evm-rpc.publicnode.com
Native:       CRO
Fee:          ~$0.01
Balance:      ✅ Already fetched
Send:         🟡 Add ChainId.Cro
```

### Arbitrum One
```
Address:      0x... (EVM — same as ETH)
RPC:          arb1.arbitrum.io/rpc / rpc.ankr.com/arbitrum
Native:       ETH
Tokens:       USDT, USDC, ARB, WBTC + all ERC-20
Fee:          $0.01–0.20
Balance:      ✅ Already fetched
Send:         🟡 Add ChainId.Arbitrum
```

### Optimism
```
Address:      0x... (EVM)
RPC:          mainnet.optimism.io / rpc.ankr.com/optimism
Native:       ETH
Tokens:       USDT, USDC, OP + all ERC-20
Fee:          $0.01–0.10
Balance:      ✅ Already fetched
Send:         🟡 Add ChainId.Optimism
```

### Base (Coinbase L2)
```
Address:      0x... (EVM)
RPC:          mainnet.base.org / base.publicnode.com
Native:       ETH
Tokens:       USDT, USDC + all ERC-20
Fee:          $0.001–0.05
Balance:      ✅ Already fetched
Send:         🟡 Add ChainId.Base
```

---

## Planned L2 / new chains (not yet in code)

| Chain | ChainID | RPC | Native | Fee | Priority |
|-------|---------|-----|--------|-----|----------|
| zkSync Era | 324 | mainnet.era.zksync.io | ETH | $0.01–0.20 | 🔴 High |
| Linea | 59144 | rpc.linea.build | ETH | $0.01–0.10 | 🔴 High |
| Scroll | 534352 | rpc.scroll.io | ETH | $0.01–0.10 | 🟡 Medium |
| Mantle | 5000 | rpc.mantle.xyz | MNT | $0.001 | 🟡 Medium |
| Polygon zkEVM | 1101 | zkevm-rpc.com | ETH | $0.01–0.20 | 🟡 Medium |
| Celo | 42220 | forno.celo.org | CELO | $0.001 | 🟡 Medium |
| Gnosis | 100 | rpc.gnosischain.com | xDAI | $0.001 | 🟢 Low |
| Moonbeam | 1284 | rpc.api.moonbeam.network | GLMR | $0.01 | 🟢 Low |
| XRP Ledger | — | xrplcluster.com | XRP | 0.00001 XRP | 🔴 High |
| Stellar | — | horizon.stellar.org | XLM | $0.000001 | 🟡 Medium |
| Cosmos | — | cosmos-rpc.polkachu.com | ATOM | $0.01 | 🟡 Medium |
| Polkadot | — | rpc.polkadot.io | DOT | $0.10 | 🟡 Medium |
| Near | — | rpc.mainnet.near.org | NEAR | $0.001 | 🟡 Medium |
| Aptos | — | fullnode.mainnet.aptoslabs.com | APT | $0.001 | 🟡 Medium |
| Sui | — | fullnode.mainnet.sui.io | SUI | $0.001 | 🟡 Medium |
| Hedera | — | mainnet.hashio.io/api | HBAR | $0.0001 | 🟢 Low |
| Algorand | — | mainnet-api.algonode.cloud | ALGO | $0.001 | 🟢 Low |

---

## USDT on every chain

| Network | Contract / Address | Fee | Best for |
|---------|-------------------|-----|---------|
| **TRC-20 (Tron)** | TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t | $0.50–8* | P2P, exchanges |
| **ERC-20 (Ethereum)** | 0xdAC17F958D2ee523a2206206994597C13D831ec7 | $1–15 | DeFi |
| **BEP-20 (BSC)** | 0x55d398326f99059fF775485246999027B3197955 | $0.01–0.10 | Cheap EVM |
| **Polygon** | 0xc2132D05D31c914a87C6611C10748AEb04B58e8F | $0.001–0.01 | Very cheap |
| **Solana (SPL)** | Es9vMFrzaCERmJfrF4H2FYD4KCoNkY11McCe8BenwNYB | $0.0001 | Cheapest + fastest |
| **Arbitrum** | 0xFd086bC7CD5C481DCC9C85ebE478A1C0b69FCbb9 | $0.01–0.20 | ETH ecosystem |
| **Optimism** | 0x94b008aA00579c1307B0EF2c499aD98a8ce58e58 | $0.01–0.10 | ETH ecosystem |
| **Avalanche** | 0x9702230A8Ea53601f5cD2dc00fDBc13d4dF4A8c7 | $0.01–0.30 | AVAX ecosystem |
| **Base** | 0xfde4C96c8593536E31F229EA8f37b2ADa2699bb2 | $0.001–0.05 | Cheapest EVM |
| **TON (Jetton)** | EQCxE6mUtQJKFnGfaROTKOt1lZbDiiX1kCixRv7Nw2Id_sDs | $0.01–0.05 | Telegram users |

\* *TRC-20 fee: ~$0.50 if staked TRX, ~$8 if fresh wallet with no TRX staked.*

### TRC-20 fee explained (show in UI before send)
```
USDT TRC-20 = smart contract call → needs Energy
Energy from: staking TRX
No staked TRX → Tron burns TRX instead (~27 TRX = ~$7–8)

Fix: stake 10,000+ TRX once → unlimited free USDT transfers
```

---

## USDC on all chains

| Network | Contract | Fee |
|---------|----------|-----|
| Ethereum | 0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48 | $1–15 |
| Solana | EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v | $0.0001 |
| Polygon | 0x2791Bca1f2de4661ED88A30C99A7a9449Aa84174 | $0.001 |
| Arbitrum | 0xaf88d065e77c8cC2239327C5EDb3A432268e5831 | $0.01 |
| Base | 0x833589fCD6eDb6E08f4c7C32D4f71b54bdA02913 | $0.001 |
| Avalanche | 0xB97EF9Ef8734C71904D8002F8b6Bc66Dd9c48a6E | $0.01 |
| TON | EQD0vdSA_NedR9uvbgN9EikRX-suesDxGeFg69XQMavfLqIw | $0.01 |
| BSC | 0x8AC76a51cc950d9822D68b83fE1Ad97B32Cd580d | $0.01 |
| Optimism | 0x0b2C639c533813f4Aa9D7837CAf62653d097Ff85 | $0.01 |

---

## Top tokens by category

### Stablecoins
```
USDT  — 9 networks (see above)
USDC  — 9 networks (see above)
DAI   ETH: 0x6B175474E89094C44Da98b954EedeAC495271d0F
BUSD  BSC: 0xe9e7CEA3DedcA5984780Bafc599bD69ADd087D56
FDUSD ETH: 0xc5f0f7b66764F6ec8C8Dff7BA683102295E16409
TUSD  ETH: 0x0000000000085d4780B73119b644AE5ecd22b376
PYUSD ETH: 0x6c3ea9036406852006290770BEdFcAbA0e23A0e8
USDP  ETH: 0x8E870D67F660D95d5be530380D0eC0bd388289E1
```

### DeFi blue chips (ERC-20)
```
UNI  0x1f9840a85d5aF5bf1D1762F925BDADdC4201F984  (Uniswap)
AAVE 0x7Fc66500c84A76Ad7e9c93437bFc5Ac33E2DDaE9  (Aave)
CRV  0xD533a949740bb3306d119CC777fa900bA034cd52  (Curve)
COMP 0xc00e94Cb662C3520282E6f5717214004A7f26888  (Compound)
MKR  0x9f8F72aA9304c8B593d555F12eF6589cC3A579A2  (MakerDAO)
SNX  0xC011a73ee8576Fb46F5E1c5751cA3B9Fe0af2a6F  (Synthetix)
YFI  0x0bc529c00C6401aEF6D220BE8C6Ea1667F6Ad93e  (Yearn)
1INCH 0x111111111117dC0aa78b770fA6A738034120C302 (1inch)
BAL  0xba100000625a3754423978a60c9317c58a424e3D  (Balancer)
LDO  0x5A98FcBEA516Cf06857215779Fd812CA3beF1B32  (Lido)
RPL  0xD33526068D116cE69F19A9ee46F0bd304F21A51f  (Rocket Pool)
```

### L2 ecosystem tokens
```
ARB  0xB50721BCf8d664c30412Cfbc6cf7a15145234ad1  (Arbitrum)
OP   0x4200000000000000000000000000000000000042  (Optimism, on OP chain)
MATIC/POL 0x7D1AfA7B718fb893dB30A3aBc0Cfc608AaCfeBB0 (Polygon on ETH)
MNT  0x3c3a81e81dc49A522A592e7622A7E711c06bf354  (Mantle)
```

### Exchange tokens
```
BNB   — native BSC coin
CRO   ETH: 0xA0b73E1Ff0B80914AB6fe0444E65848C4C34450b
OKB   ETH: 0x75231F58b43240C9718Dd58B4967c5114342a86c
GT    Gate chain native
```

### Wrapped assets
```
WBTC  ETH: 0x2260FAC5E5542a773Aa44fBCfeDf7C193bc2C599  ($1 = 1 BTC)
WETH  ETH: 0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2
WBNB  BSC: 0xbb4CdB9CBd36B01bD1cBaEBF2De08d9173bc095c
```

### Privacy coins
```
XMR  — Monero (full support, own blockchain)
ZEC  — Zcash (transparent receive only, shielded planned)
DASH — planned (m/44'/5'/0'/0/n, NBitcoin.Altcoins)
SCRT — Secret Network (Cosmos-based, planned)
```

### AI / infrastructure tokens
```
FET   ETH: 0xaea46A60368A7bD060eec7DF8CBa43b7EF41Ad85  (Fetch.ai)
AGIX  ETH: 0x5B7533812759B45C2B44C19e320ba2cD2681b542  (SingularityNET)
OCEAN ETH: 0x967da4048cD07aB37855c090aAF366e4ce1b9F48  (Ocean)
WLD   ETH: 0x163f8C2467924be0ae7B5347228CABF260318753  (Worldcoin)
LINK  ETH: 0x514910771AF9Ca656af840dff83E8264EcF986CA  (Chainlink)
GRT   ETH: 0xc944E90C64B2c07662A292be6244BDf05Cda44a7  (The Graph)
```

### Gaming / NFT
```
AXS   ETH: 0xBB0E17EF65F82Ab018d8EDd776e8DD940327B28b  (Axie Infinity)
SAND  ETH: 0x3845badAde8e6dFF049820680d1F14bD3903a5d0  (Sandbox)
MANA  ETH: 0x0F5D2fB29fb7d3CFeE444a200298f468908cC942  (Decentraland)
ENJ   ETH: 0xF629cBd94d3791C9250152BD8dfBDF380E2a3B9c  (Enjin)
```

### Meme coins
```
SHIB  ETH: 0x95aD61b0a150d79219dCF64E1E6Cc01f0B64C4cE
PEPE  ETH: 0x6982508145454Ce325dDbE47a25d4ec3d2311933
FLOKI ETH: 0xcf0C122c6b73ff809C693DB761e7BaeBe62b6a2E
BONK  SOL: DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263
WIF   SOL: EKpQGSJtjMFqKZ9KQanSqYXRcF8fBopzLHYxdM65zcjm
```

### Solana ecosystem SPL
```
RAY   4k3Dyjzvzp8eMZWUXbBCjEvwSkkk59S5iCNLY3QrkX6R  (Raydium)
JTO   jtojtomepa8beP8AuQc6eXt5FriJwfFMwQx2v2f9mCL   (Jito)
JUP   JUPyiwrYJFskUPiHa7hkeR8VUtAeFoSYbKedZNsDvCN   (Jupiter)
PYTH  HZ1JovNiVvGqjq3MhMNbDt4gMXQfVdujdGMNMENjuDo4  (Pyth Network)
```

---

## Full chain matrix (complete)

| # | Chain | Type | Status | Native | Key Feature |
|---|-------|------|--------|--------|-------------|
| 1 | Bitcoin | L1 | ✅ Stable | BTC | Original, most trusted |
| 2 | Ethereum | L1 | ✅ Stable | ETH | Largest DeFi ecosystem |
| 3 | Tron | L1 | ✅ Stable | TRX | USDT P2P king |
| 4 | Solana | L1 | ✅ Beta | SOL | Fastest, cheapest fees |
| 5 | Litecoin | L1 | ✅ Stable | LTC | Fast + cheap BTC cousin |
| 6 | Cardano | L1 | ✅ Beta | ADA | Academic, formal proofs |
| 7 | TON | L1 | ✅ Beta | TON | Telegram's blockchain |
| 8 | Monero | L1 | ✅ Beta | XMR | 🟢 Best privacy coin |
| 9 | Bitcoin Cash | L1 | ✅ Beta | BCH | Cheap on-chain payments |
| 10 | Dogecoin | L1 | ✅ Beta | DOGE | Meme but real payments |
| 11 | Zcash | L1 | ✅ Beta | ZEC | Privacy (transparent now) |
| 12 | BNB Smart Chain | L1/EVM | 🟡 Ready* | BNB | Largest EVM after ETH |
| 13 | Polygon | L2/EVM | 🟡 Ready* | MATIC | Cheapest USDT on EVM |
| 14 | Avalanche | L1/EVM | 🟡 Ready* | AVAX | Fast EVM finality |
| 15 | Arbitrum | L2/EVM | 🟡 Ready* | ETH | ETH L2 #1 by TVL |
| 16 | Optimism | L2/EVM | 🟡 Ready* | ETH | ETH L2 with OP token |
| 17 | Base | L2/EVM | 🟡 Ready* | ETH | Coinbase L2, growing |
| 18 | Fantom | L1/EVM | 🟡 Ready* | FTM | Fast EVM |
| 19 | Cronos | L1/EVM | 🟡 Ready* | CRO | Crypto.com chain |
| 20 | zkSync Era | L2/EVM | 🟡 Planned | ETH | ZK rollup |
| 21 | Linea | L2/EVM | 🟡 Planned | ETH | ConsenSys ZK L2 |
| 22 | Scroll | L2/EVM | 🟡 Planned | ETH | ZK rollup |
| 23 | XRP Ledger | L1 | 🟡 Planned | XRP | Fastest for payments |
| 24 | Stellar | L1 | 🟡 Planned | XLM | CBDCs + microtransactions |
| 25 | Cosmos | L1 | 🟡 Planned | ATOM | IBC multi-chain hub |
| 26 | Polkadot | L1 | 🟡 Planned | DOT | Parachain ecosystem |
| 27 | Near | L1 | 🟡 Planned | NEAR | Human-readable addresses |
| 28 | Aptos | L1 | 🟡 Planned | APT | Meta blockchain |
| 29 | Sui | L1 | 🟡 Planned | SUI | High throughput |
| 30 | Algorand | L1 | 🟡 Planned | ALGO | Pure PoS, ASA tokens |
| 31 | Hedera | L1 | 🟡 Planned | HBAR | Enterprise DLT |
| 32 | Dash | L1 | 🟡 Planned | DASH | Privacy + instant send |
| 33 | Mantle | L2/EVM | 🟡 Planned | MNT | BitDAO L2 |
| 34 | Celo | L2/EVM | 🟡 Planned | CELO | Mobile-first payments |
| 35 | Gnosis | L1/EVM | 🟡 Planned | xDAI | Stable gas token |

\* *"Ready" = balance already fetched, send needs ~2h of code to enable (ChainId + routing)*

---

## How token discovery works

### Auto-detection on address link
```
1. Link address (WalletConnect, manual, or seed derivation)
2. Fetch native balance (ETH, TRX, SOL, etc.)
3. Query token API:
   - ETH: Blockscout /api?module=account&action=tokenlist
   - TRX: TronScan trc20token_balances (up to 40 tokens)
   - SOL: Solana getTokenAccountsByOwner (all SPL)
4. Filter: show only tokens with balance > 0
5. Price via CoinGecko /simple/price (via Tor, 60s cache)
6. Display: amount + USD value + fiat equivalent
```

### Custom token (any ERC-20 / TRC-20 / SPL)
```
User inputs contract address →
Wallet calls: name() + symbol() + decimals() on contract →
Validates: returns sane values →
Saves to user's custom token list →
Shows in portfolio with live price if CoinGecko knows it
```

---

## Privacy rating per chain

| Chain | Rating | Reason |
|-------|--------|--------|
| Monero (XMR) | 🟢 Maximum | Ring sigs + stealth addresses + RingCT |
| Zcash (z-addr) | 🟢 High | zk-SNARKs shielded transactions |
| Bitcoin (coin control + Tor) | 🟡 Medium | UTXO — careful hygiene helps |
| Litecoin | 🟡 Medium | Same as Bitcoin |
| Bitcoin Cash | 🟡 Medium | Same as Bitcoin |
| Dash (PrivatSend) | 🟡 Medium | CoinJoin mixing |
| Everything else | 🔴 Low | Fully public ledgers |

**Rule:** Show privacy rating on every send screen.
For any 🔴 Low chain, offer: "Want more privacy? Send as Monero."

---

## Which network to use? (in-app guide)

```
Sending USDT to exchange (deposit)?
  Use whatever the exchange lists (usually TRC-20 or ERC-20)
  Check their minimum deposit to cover network fee

Sending USDT person-to-person?
  Solana:  $0.0001  ← cheapest + fastest
  Polygon: $0.01    ← cheap, widely supported
  TRC-20:  free if TRX staked, $8 if not
  BSC:     $0.05    ← cheap, most exchanges support
  ERC-20:  $1–15   ← universal but expensive

Need maximum privacy?
  No USDT variant is private (all public blockchains)
  Convert: USDT → XMR → send → convert back
  Or use Monero directly end-to-end

Receiving regular payments?
  Give TRC-20 address for Asia/exchange senders
  Give Solana address for fast + cheap
  Give ERC-20 for DeFi / serious amounts
```

---

## Roadmap — implementation order

### Phase 1 (already have balance, ~2h each)
1. BSC (BNB) — add ChainId.Bsc + send routing
2. Polygon — add ChainId.Polygon + send routing
3. Arbitrum — add ChainId.Arbitrum + send routing
4. Optimism — add ChainId.Optimism + send routing
5. Base — add ChainId.Base + send routing
6. Avalanche — add ChainId.Avax + send routing

### Phase 2 (new RPC integration, ~1 day each)
7. zkSync Era — EVM, ZK proof verification for withdrawals
8. XRP Ledger — custom xrpl signing
9. Stellar — ed25519, horizon API
10. TON Jettons — balance display (token standard)
11. Dash — NBitcoin.Altcoins + PrivatSend (optional)

### Phase 3 (complex, ~1 week each)
12. Cosmos + IBC chains (ATOM, OSMO, JUNO...)
13. Polkadot + parachains (sr25519 non-standard curve)
14. Near Protocol
15. Aptos / Sui (new VM architectures)
16. Zcash shielded z-addr (sapling/orchard ZK proofs)
