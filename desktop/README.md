# Umbrella Wallet — desktop

Native, local-first, non-custodial crypto wallet built with **.NET 8 + Avalonia**. The recovery
phrase is generated on the machine and encrypted at rest; it is never sent anywhere. This is the
only shipped Umbrella product — there is no web app or backend server (the old web stack was
removed). Ships for **Windows and Linux**; Android is planned, not shipped.

Version and release history: see [`../VERSION`](../VERSION) and [`../CHANGELOG.md`](../CHANGELOG.md).
Product direction and the current work plan live in
[`../docs/CLAUDE_IMPLEMENTATION_ROADMAP_UK.md`](../docs/CLAUDE_IMPLEMENTATION_ROADMAP_UK.md).

## What it does

- Generate or import a **BIP39** recovery phrase; deterministic **BIP44/84** derivation.
- Encrypt the phrase locally with **Argon2id** (64 MiB, 4 iterations) + **AES-256-GCM**, in a
  versioned vault with authenticated metadata and atomic writes. Lock/unlock lifecycle.
- **Tor built in** — one toggle routes wallet traffic through a bundled Tor client; a custom SOCKS5
  proxy is also supported. Public explorer/RPC calls inherit it.
- **Send and receive** real transactions, signed locally; only the signed result is broadcast.
- **On-chain history** read from public explorers for the wallet's own addresses.
- In-wallet **swap** via THORChain, and a market view with live prices and OHLC candles.

### Network support (check the in-app badge before you send or receive)

The authoritative, per-asset support state is shown **in the app** next to each asset — that is the
one place a user must read before moving funds, and it is deliberately conservative.

- **Bitcoin / Litecoin** — full HD UTXO wallet: balance, on-chain history and spending are
  aggregated across every derived address (external + internal change). *(A live testnet smoke of
  the multi-address send is still pending before the "fresh receive address" button returns — see
  the roadmap §3.4/§3.5.)*
- **Ethereum + EVM side-chains, TRON (+ TRC-20 USDT), Solana** — send and receive.
- **Monero** — runs a bundled `monero-wallet-rpc` locally for real balance and sending.
- **Others** — some chains are receive-only or address-derivation-only. Do not infer "fully
  supported" from "an address can be shown"; trust the in-app badge.

## Security model

Non-custodial: your keys never leave the device, and there is no server that could hold them.

**Protects against:** theft of the vault file without the password; vault tampering (AES-GCM fails
closed); accidental plaintext persistence of the seed.

**Does not protect against:** malware/keyloggers or an already-compromised OS; a weak vault
password; physical access while unlocked. The on-screen screenshot guard is **Windows-only** today.

For meaningful cold storage: use an offline machine, verify release hashes (see the repo README's
"Verify your download"), keep an offline paper/metal backup, and never type the seed into a website
or chat.

## Build and run

Requires the .NET 8 SDK. On first build, stage the bundled Tor + Monero binaries (they are pinned
and hash-verified — see [`../THIRD_PARTY_NOTICES.md`](../THIRD_PARTY_NOTICES.md)):

```powershell
pwsh desktop/scripts/fetch-tor.ps1
pwsh desktop/scripts/fetch-monero.ps1

dotnet restore desktop/Umbrella.Wallet.sln
dotnet test    desktop/Umbrella.Wallet.sln
dotnet run --project desktop/src/Umbrella.Wallet.App
```

On low-disk-space systems, point the package cache off the system drive with
`$env:NUGET_PACKAGES = "D:\nuget-packages"` before restoring.

## Honest limitations

This wallet is **not independently audited** and has **no hardware-wallet / PSBT / air-gapped
signing** yet. Before trusting it with significant funds, the roadmap calls for: an external
cryptography and supply-chain audit; reproducible, code-signed builds with update-signature
verification; hardware-wallet integration; and a manual QA pass per supported network. The vault is
kept separate from the network/signing code so no network adapter can reach seed material directly.
