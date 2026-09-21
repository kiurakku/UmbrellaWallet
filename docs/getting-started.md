# Getting started

Install Umbrella Wallet, verify the download, create or import a wallet, and make a first small send
safely.

<!-- TOC -->
- [1. Download](#1-download)
- [2. Verify the file](#2-verify-the-file)
- [3. Install or run portable](#3-install-or-run-portable)
- [4. First launch](#4-first-launch)
- [5. Create or import a wallet](#5-create-or-import-a-wallet)
- [6. Turn on Tor (recommended)](#6-turn-on-tor-recommended)
- [7. Receive and send](#7-receive-and-send)
- [8. Backup](#8-backup)
- [9. Next reading](#9-next-reading)
<!-- /TOC -->

---

## 1. Download

Get the latest build from
**[GitHub Releases](https://github.com/thefear078/UmbrellaWallet/releases/latest)**.

Typical assets:

| Platform | File |
|---|---|
| Windows installer | `UmbrellaWallet-Setup-<version>.exe` |
| Windows portable | `UmbrellaWallet-<version>-win-x64-portable.exe` (or `.zip`) |
| Linux | `UmbrellaWallet-<version>-linux-x64.tar.gz` |

Also download **`SHA256SUMS-<version>.txt`** from the same release.

## 2. Verify the file

Do not run an unsigned copy you cannot check.

```powershell
# Windows PowerShell — compare to the matching line in SHA256SUMS
Get-FileHash .\UmbrellaWallet-Setup-4.8.0.exe -Algorithm SHA256
```

```bash
# Linux / macOS
sha256sum -c SHA256SUMS-4.8.0.txt
```

Details: [SECURITY.md — Verifying what you run](../SECURITY.md#verifying-what-you-run).

## 3. Install or run portable

- **Installer:** choose a folder; wallet data lives under `%APPDATA%\UmbrellaWallet` (Windows).
- **Portable:** run next to its folder; data typically stays in `data/` beside the binary — useful when
  you do not want an installed footprint.

Linux: unpack the tarball and run `./Umbrella.Wallet.App` (or the published apphost name listed in
the release notes).

## 4. First launch

You will set a **vault password** (unlocks the encrypted vault on this device). This is **not** your
24-word recovery phrase.

Read and accept the terms when prompted (see [TERMS_OF_SERVICE.md](../TERMS_OF_SERVICE.md)). You must
be **18+**.

## 5. Create or import a wallet

**Create:** the app generates a BIP39 phrase with the OS CSPRNG. Write all words on **paper**. Confirm
the random word checks. Anyone with those words has the funds; if you lose them and the device, nobody
can recover them — including us.

**Import:** paste a BIP39 phrase from another wallet. Addresses will match the original derivation
paths Umbrella supports. Telegram/Tonkeeper non-BIP39 formats are a separate case — see release notes
and import UI messages.

## 6. Turn on Tor (recommended)

Before you unlock (or immediately after), open **Settings → Privacy** and enable Tor. Optional:
**Tor-only kill-switch** so clearnet is refused if Tor is down.

Tor hides your **IP** from explorers. It does **not** make Bitcoin/Ethereum private. See
[PRIVACY.md](../PRIVACY.md).

## 7. Receive and send

1. **Receive:** pick the coin, confirm the **network** under the name, show QR / address. On UTXO
   chains, prefer a fresh address when the wallet offers one.
2. **Send a tiny test amount first.** Blockchain transfers are final.
3. On the review screen, check destination, amount, fee, and any privacy warnings.

Coins that are **receive-only** (for example zkSync Era until send is proven) are labelled honestly —
do not treat them as “Ready” to spend.

## 8. Backup

Export an **encrypted** backup from Settings and store it offline. Prefer verifying the backup before
you rely on it. The paper seed remains the ultimate recovery path.

## 9. Next reading

| Topic | Document |
|---|---|
| Philosophy | [MANIFESTO.md](../MANIFESTO.md) |
| Threats | [THREAT_MODEL.md](../THREAT_MODEL.md) |
| Problems | [troubleshooting.md](troubleshooting.md) |
| Full doc map | [INDEX.md](INDEX.md) |

---

📖 Back to [Documentation Index](INDEX.md)
