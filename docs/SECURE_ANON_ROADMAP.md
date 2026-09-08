# Umbrella — Secure & Anonymous Wallet Roadmap

Goal: an **ideal non-custodial, private, self-verifiable** desktop wallet. No custody, no accounts,
no KYC, no telemetry; keys and traffic never leak; the user can prove to themselves that this is true.

Legend: ✅ done · 🟡 partial · ⏳ next · ❌ missing. Status as of 2026-09-03 (v4.5.0).

---

## Pillar 1 — Key security (the seed is everything)

| # | Item | Status |
|---|---|---|
| 1.1 | Seed generated from an OS CSPRNG (never `Random`/time) | ✅ hardened (explicit `RandomNumberGenerator`) |
| 1.2 | BIP39/32/44/84 + SLIP-0010 derivation, pinned to spec vectors | ✅ |
| 1.3 | Vault encrypted at rest (Argon2id KDF + AES); seed never written/logged in plaintext | ✅ |
| 1.4 | Private keys zeroed from memory right after signing | ✅ |
| 1.5 | Screenshot/scrape guard while the seed is on screen | ✅ |
| 1.6 | Signing key is re-checked against the shown address before every sign | ✅ |
| 1.7 | Encrypted seed export for recovery (no plaintext on disk) | ✅ verified (copies the sealed vault; verify never surfaces the seed) |

## Pillar 2 — Network privacy (nobody sees who you are)

| # | Item | Status |
|---|---|---|
| 2.1 | Bundled Tor; every balance/price/history/swap/broadcast rides it | ✅ |
| 2.2 | Tor-only kill-switch, **fail-closed** — clearnet is refused, never a silent fallback | ✅ |
| 2.3 | Monero node connection also fail-closed under Tor-only | ✅ |
| 2.4 | No DNS leak (proxy resolves hostnames) + a one-tap self-test | ✅ SOCKS5h (names resolved at the proxy); kill-switch refuses at connect *before* any DNS lookup; "Verify Tor" self-test + a hard transport-level proof (`KillSwitchTests`) |
| 2.5 | No telemetry / crash reports / analytics; opt-in market data off by default | ✅ |
| 2.6 | "Verify Tor" — prove the exit is really a Tor node | ✅ |
| 2.7 | User-supplied SOCKS5 proxy (own VPN/Tor) as an alternative | ✅ |

## Pillar 3 — On-chain privacy (the ledger can't link you)

| # | Item | Status |
|---|---|---|
| 3.1 | Change returns to a **fresh internal** address, never a reused one | ✅ |
| 3.2 | Full HD scan (every issued address) so rotation never loses funds | ✅ |
| 3.3 | Fresh receive address per counterparty (fail-closed HD rotation) + **reuse warning** | ✅ Receive now checks the shown address on-chain and warns before it is handed out twice (`AddressReuseInspector`; an unreachable explorer reports "unknown", never "fresh") |
| 3.4 | **Coin control** — pick which UTXOs fund a spend (avoid linking identities) | ✅ opt-in on BTC/LTC/DOGE; the planner is handed only the ticked coins and can't reach outside them (proven offline: `Coin_control_spends_only_the_selected_coins`, `…fails_closed_when_the_selection_cannot_cover`) |
| 3.5 | Monero private-by-default (hidden amounts, stealth addresses) | ✅ |
| 3.6 | Per-chain privacy notes shown to the user | ✅ |

## Pillar 4 — App & operational security

| # | Item | Status |
|---|---|---|
| 4.1 | Idle auto-lock + lock-on-minimize | ✅ |
| 4.2 | Clipboard auto-wipe after copy | ✅ |
| 4.3 | Password strength meter + vault re-encrypt on change | ✅ |
| 4.4 | **Plausible-deniability hidden wallet** (BIP39 passphrase) | 🟡 code exists, unlock UI removed |
| 4.5 | **Panic / duress wipe** of the local vault | 🟡 DataWiper exists, no duress trigger |
| 4.6 | Explicit two-step Review → Confirm on every send | ✅ |

## Pillar 5 — Trust & verifiability (don't trust, verify)

| # | Item | Status |
|---|---|---|
| 5.1 | Keyless public endpoints only; no API keys, no account | ✅ |
| 5.2 | Published SHA-256 of every release artifact | ✅ (`SHA256SUMS-4.5.0.txt`) |
| 5.3 | Reproducible-build instructions so anyone can match the hash | 🟡 documented ([`BUILD_VERIFY.md`](BUILD_VERIFY.md)); managed DLLs deterministic + no build-path leak verified, installer not bit-identical (.NET single-file limitation) |
| 5.4 | Code signing (OV/EV cert) | ❌ blocked (no cert) |
| 5.5 | Open source / auditable | ✅ |

---

## Execution order (by security impact, safest-first)

1. **1.1 Audit seed entropy** — confirm new seeds come from a CSPRNG. *(Critical; if wrong, everything is.)*
2. **2.4 DNS-leak self-test** — ✅ SOCKS5h resolves names at the proxy; with the kill-switch on and no
   proxy the connect callback refuses *before* resolving, so nothing leaks to a local resolver.
   Provable on the real transport (`KillSwitchTests` via `PublicHttp.CreateProbeClient`) — not just prose.
3. **3.3 Address-reuse warning** — ✅ the Receive screen asks the explorer whether the address on
   screen already has history and, if it does, says so and offers a fresh one. The verdict is
   evidence-only: the local scan floor can prove an index is untouched offline, and anything
   else (network error, no state) is reported as unknown rather than silently "fresh"
   (`AddressReuseTests`, 9 offline tests).
4. **3.4 Coin control** — ✅ optional manual UTXO selection on the send screen (BTC/LTC/DOGE). Off by
   default (send path unchanged); on, only the ticked coins fund the spend and it fails closed if they
   can't cover it. Offline guarantee proven; rides the already-verified HD send/broadcast path.
5. **4.4 / 4.5 Deniability & panic** — decide with the user: re-expose hidden-wallet unlock and add a duress wipe.
6. **1.7 / 5.2 Recovery + hashes** — verify encrypted export; keep SHA-256 sums current per release.
7. **5.3 Reproducible build** — ✅ documented in `BUILD_VERIFY.md`: verify a release hash, build from
   the `v4.5.0` tag, and compare the deterministic managed DLLs (the installer itself isn't bit-identical,
   an inherent .NET single-file limitation — the guide is honest about this).

Each fund- or key-touching change ships with an offline test; live send/broadcast paths are verified
with a small real amount before being trusted.
