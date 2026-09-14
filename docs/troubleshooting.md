# Troubleshooting

For users first, then the things only a developer will hit.

---

## Balances

**"My balance is slow to appear."**
Check whether Tor is on (Security Center). Tor adds latency by design — every request takes a longer
path, and that is the trade you are making for the explorer not seeing your IP.

If Tor is off and it is still slow, it is usually one provider being slow rather than the wallet: the
wallet queries prices and balances in parallel and paints cached balances immediately, so you should
see your last-known numbers instantly and live ones shortly after.

**"Monero is stuck at Scanning…"**
That is expected and it is not stuck. Monero hides amounts on-chain, so no explorer can tell you your
balance — the wallet runs a real Monero wallet daemon locally and syncs it. That takes minutes on
first run, not seconds. Everything else in the wallet works meanwhile.

**"My balance went down!"**
It should not, by design: a partial or failed network scan keeps the previous balance rather than
showing a lower one. If you genuinely see a drop, check Activity for a transaction you do not
recognise, and treat the machine as suspect before the wallet.

**"A coin I hold is not showing."**
- Is that coin enabled for this wallet? Settings → Wallets → coins.
- Is it a token on a chain the wallet reads? Every ERC-20 and TRC-20 at your own address appears
  automatically; tokens on other chains may not.
- Is it folded away as a suspected spam airdrop? The notice above Holdings shows the count and opens
  them.

---

## Suspected spam tokens

**"An unknown token appeared, named like an advert."**
That is an unsolicited airdrop. Anyone can send a token to any address; it arriving means nothing
about you. Their name is the attack — it lures you to a site that asks for your seed phrase.

Umbrella folds them away and tells you how many. **Never open a site named in a token, and never enter
your 24 words anywhere.**

They are hidden, not deleted — one click shows them. A token with a real market price is never
flagged, so this cannot hide something valuable.

---

## Sending

**"Send fails with 'not synced'."**
A partial scan refuses to spend rather than risk building a transaction from an incomplete picture.
Refresh, wait for a full scan, try again. This is the wallet protecting you from a stuck or invalid
transaction.

**"My transaction is not confirming."**
Check the fee level you chose. Economy is genuinely slower. Every level is clamped so it cannot fall
below the network's relay minimum, so it will confirm eventually — but under congestion "eventually"
can be hours.

**"USDT send failed."**
USDT (TRC-20) is a contract call and its fee is paid in **TRX**. Keep a small amount of TRX on the
address or the transfer fails for lack of energy.

**"I sent to the wrong network."**
Nothing can be done. This is why every row prints the network under the coin name, why the wallet
warns when an address does not match the selected chain, and why you should always send a small test
amount to a new address first.

---

## Tor

**"Tor will not start."**
It takes ~20 seconds to bootstrap. If it never does:
- Something else may hold port 9250.
- A corporate firewall or ISP may block Tor.
- The bundled `tor.exe` may have been quarantined by antivirus.

**"Is my Tor Browser affected?"**
No. The wallet uses port 9250 specifically so a Tor Browser on the standard 9050 is untouched.

**"I want to be certain nothing goes direct."**
Turn on the **Tor-only kill-switch**. A request that cannot go through Tor then does not happen at
all, instead of quietly falling back to a direct connection.

---

## The app itself

**"The app icon is blank in Search or on the desktop."**
Usually a shortcut whose icon path points at a file that does not exist, or a stale Windows icon
cache. Point the shortcut's icon at `Umbrella.exe` itself and refresh the cache:

```powershell
ie4uinit.exe -show
```

**"It says an old version after I updated."**
If you updated by copying files, you may have replaced the DLLs but left the old `Umbrella.exe`. Use
the installer, or re-copy the whole publish folder.

**"I forgot my vault password."**
Your 24 words restore everything in any BIP39 wallet, including this one — create a wallet, choose
import, enter the words, set a new password. If you have lost **both** the password and the words, the
funds are gone. Nobody can recover them; that is the same property that stops anyone freezing them.

**"The wallet locked itself."**
Auto-lock, on idle or on minimise (Settings → Security). `Ctrl+L` locks instantly.

---

## For developers

**`MSB3021: cannot copy … used by another process`**
The app or a previous publish is still running and holding the DLLs. Close it. Note that a *published*
app can hold files even after its window closes if a child process (Tor, `monero-wallet-rpc`) is
still alive.

**`monero-wallet-rpc` blocked during build**
A well-known antivirus false positive on Monero tooling. The fetch script verifies the official
SHA-256 *before* staging, so the file is authentic; the block happens afterwards. Allowing it is your
call — without it the build works and Monero balance/send do not.

**Localization parity test fails**
You added a key to one language only. The failure lists exactly which keys and which language.

**Theme contrast test fails**
A palette's text dropped below WCAG AA. The message gives the measured ratio and the required one. See
[theming.md](theming.md).

**Hardcoded-string test fails**
A literal in a view or in the send/backup/status paths. Move it to `Localization.cs`. If it is
genuinely untranslatable (a chain name, a standard like `ERC-20`), add it to `AllowedLiterals` with a
reason.

**XAML binding error at build time**
Working as intended — `x:CompileBindings` is on, so a binding typo fails the build instead of
producing a blank label you find weeks later.

**Tests pass locally, fail in CI**
Usually a culture assumption. CI runs under a different default locale; use `Fx.Culture` or an
explicit culture in assertions rather than relying on the machine's.

---

## Still stuck

- [Open an issue](https://github.com/kiurakku/UmbrellaWallet/issues)
- [Telegram](https://t.me/UmbrellaWallet)
- **Security problems: [report privately](https://github.com/kiurakku/UmbrellaWallet/security/advisories/new)**, not in a public issue.

Never post your seed phrase, a private key, or a screenshot containing either. No one legitimate will
ever ask you for them — not us, not support, not anyone.

---

📖 Back to [Documentation Index](INDEX.md)

