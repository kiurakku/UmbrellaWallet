# Hardware wallets & malware residual risk

**Audience:** users holding more than “coffee money”, and contributors working on roadmap **H.2**.

## The hard truth (desktop)

No desktop wallet — including Umbrella — can fully defeat **malware already running as your user**
(infostealer, keylogger, RAT, memory scraper). If the OS and the wallet process are compromised, a
seed unlocked in RAM can be stolen. That residual risk is **HIGH**. See
[THREAT_MODEL.md](../THREAT_MODEL.md) Vector 1.

| Layer | What Umbrella does today | What it does **not** do |
|-------|--------------------------|-------------------------|
| Vault | Argon2id → AES-256-GCM, deniable/duress slot | Stop a keylogger while you type the password |
| Session | Auto-lock, clipboard wipe, Win screenshot guard | Stop a RAT watching the screen or scraping memory |
| Coercion | Duress / decoy password | Defend against malware (different threat) |
| Spend path | Keys can leave RAM for signing on this PC | Keep keys off a compromised PC |

## What to do with serious amounts

1. **Prefer a hardware wallet** (Ledger, Trezor, Coldcard, BitBox, …) so the seed never decrypts on
   the general-purpose PC for spends.
2. **Multisig** (2-of-3+) across devices/people when stakes are high (roadmap **H.3**).
3. Keep Umbrella for **small balances**, experiments, multi-chain convenience — with Tor + kill-switch on.

## What works in Umbrella **today** (H.1 PSBT)

Umbrella can **export an unsigned Bitcoin PSBT** from Send review, and **import / review / sign /
broadcast** PSBTs. That is the bridge to hardware and air-gapped workflows:

### Recommended flow (Bitcoin)

1. Build the payment in Umbrella (or another wallet) and **Export PSBT**.
2. Move the PSBT to a hardware-wallet companion (Sparrow, Specter, vendor app) **or** an air-gapped
   signer via QR/SD card.
3. Sign on the device. Verify amounts and addresses **on the hardware screen**.
4. Bring the signed PSBT back to Umbrella (or the companion) to broadcast — Umbrella only signs coins
   its **own scan** found, at chain values, never at values a hostile PSBT invents.

Details: Security Center → “Sign a PSBT”, and Send → Export PSBT when a BTC quote is ready.

### Limits today

- **USB Ledger / Trezor inside Umbrella** (seed never on PC) = roadmap **H.2** — not shipped yet.
- PSBT path is **Bitcoin**; other chains still sign on this PC until device support exists.
- Signing in Umbrella still needs an unlocked seed for hot-wallet mode — that is exactly the malware window.

## Roadmap

| ID | Goal | Status |
|----|------|--------|
| H.1 | PSBT export / import / review / sign | ✅ |
| H.2 | Ledger / Trezor (sign on device; seedless watch-only) | 🏆 P0 next |
| H.3 | Multisig 2-of-3 | 📅 |

## For contributors (H.2 design notes)

1. **Seedless watch-only account** — import xpub / descriptor; balances and PSBT build without mnemonic.
2. **Device transport** — USB HID (Ledger) / WebUSB-or-bridge (Trezor); never ask the device to export seed.
3. **Display verification** — user must confirm address/amount on device; Umbrella shows the same.
4. **Tests** — offline vectors + hardware-in-the-loop CI where possible; refuse silent clearnet for device bridges if Tor-only is on.

Track status in [ROADMAP.md](ROADMAP.md). Day-to-day process: [WORKFLOW.md](WORKFLOW.md).

---

📖 Back to [Documentation Index](INDEX.md)
