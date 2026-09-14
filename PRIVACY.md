# Privacy

There is no privacy policy in the usual sense, because there is nothing to have a policy about: no
server, no account, no database, no analytics. This document says what that means concretely, and
where the wallet's privacy stops.

---

## What never leaves your machine

- Your 24-word recovery phrase
- Every private key, including Monero's spend key
- Your vault password
- Your private transaction notes
- Your address book (encrypted at rest since 4.7)
- Your settings, themes, profile images
- Your activity log

There is no telemetry, no crash reporting, no analytics SDK and no account. **This is enforced by the
build**, not promised: the test suite fails if any of nineteen analytics or crash-reporting packages is
referenced, if their API shapes appear in the source, or if anything writes an application log.

There is no application log at all. A log is the easiest place for an address or an amount to end up
sitting in the clear, so the wallet does not keep one.

---

## What does leave your machine, and to whom

A wallet cannot read a blockchain by itself. It has to ask a server — and on a public chain, asking
means saying your address.

| What is sent | To whom | Why |
|---|---|---|
| Your wallet addresses | Block explorers and RPC nodes | To read balances and history |
| A signed transaction | The same | To broadcast it |
| Coin symbols | CoinGecko, Binance | To price what you hold |
| Nothing identifying | GitHub | Only when you check for an update |

**Tor hides your IP from these servers. It does not un-send the address.** That sentence is the whole
honest summary of wallet privacy on a transparent chain.

The full list — every server, who runs it, why it is contacted, and what it learns — is in
**Settings → Privacy**, sorted so the ones handed your actual addresses come first. It cannot fall
behind the code: the build fails if a host appears in the source without appearing in the list.

---

## What is never sent

- Your IP or location **as data**. It is visible to whoever answers a request, like any internet
  connection — which is what Tor is for. It is never *transmitted as a field*.
- Your device model, OS version, hardware identifiers, screen size or locale
- Your email, name or any identifier — none is ever asked for
- Your balances, as a figure. A server sees the address and can look the balance up itself; the wallet
  never reports one.
- Timestamps of anything, anywhere

---

## What you can change

- **Tor**, bundled and one switch. The kill-switch is fail-closed: with Tor-only on and Tor down, a
  request that would go over clearnet does not go at all. Over Tor, requests are split across
  **separate circuits by purpose** — the exit relay that saw your address is not the one that receives
  your transaction.
- **A SOCKS5 proxy** of your own instead.
- **Which server answers for each chain** — Bitcoin, Litecoin, Bitcoin Cash, Dogecoin, Ethereum,
  Solana, TON, Tron, Cardano, Monero. Pick a different company, or point the wallet at a node you run.
  A `.onion` node is accepted and will only ever be used with Tor on.
- **A fresh receive address per payment** on the UTXO chains.
- **Coin control**, so a spend need not join coins from different parts of your life.

A server you chose is never silently replaced by the default. If it stops answering, the wallet says
"unknown" and lets you decide — rerouting your addresses to the default is exactly what choosing was
meant to prevent.

---

## Where this stops

Being direct about this is the point of the document.

- **A transparent chain is public forever.** The amount and both addresses are on it. Nothing the
  wallet does changes that; Monero is the only coin here where it is not true.
- **Whoever you pay knows you paid them.** No network privacy touches this.
- **A spend links its inputs.** Two addresses joined by one transaction cannot be un-joined.
- **Malware on your machine defeats all of it.** See [THREAT_MODEL.md](THREAT_MODEL.md), Vector 1.
- **There is no CoinJoin, no PayJoin, no Dandelion++ and no Taproot yet.** Real gaps, on the roadmap,
  not implied away.

---

## Data on disk

Everything the wallet stores lives in one directory — `data/` beside the executable, or
`%APPDATA%/UmbrellaWallet` if that is read-only.

| Encrypted | In the clear |
|---|---|
| The seed vault (Argon2id → AES-256-GCM) | Settings: theme, language, currency |
| Exchange API keys | Watch-only addresses you added |
| Private transaction notes | The activity log |
| The address book | Cached balances and prices |

The items in the right-hand column are there because they are useful before the wallet is unlocked, or
because they are not secret. If that trade is wrong for you, **Settings → Danger zone** removes all of
it, and the build fails if the wallet writes a file the wipe does not handle.

### Data retention

- **We retain nothing on our servers** — there is no user database. See also the store-facing
  [PRIVACY_POLICY.md](PRIVACY_POLICY.md).
- **On your device**, data remains until you wipe it in-app or delete the application data directory:
  - Windows (installed): `%APPDATA%\UmbrellaWallet`
  - Linux: `~/.config/UmbrellaWallet` or `data/` next to a portable build
- **Encrypted backup files** you export remain until **you** delete them.

Full-disk encryption is the right answer to a stolen laptop, and it is the operating system's job
rather than this program's.

---

## Questions this document exists to answer directly

**Does the app send my IP, location or device model?** No, none is transmitted as data. Your IP is
visible to any server you connect to, as with any internet connection; Tor replaces it with an exit
node.

**Is there Google Analytics, Firebase or Sentry?** No, and the build fails if anyone adds one.

**Are my transactions, balances or contacts logged?** There is no application log. The activity list
is local and is erased by the wipe.

**Do you use third-party public RPC nodes?** Yes — twenty-one of them, all listed by name in Settings
with what each learns, and all replaceable.

**Can I use my own node or proxy?** Yes: your own server per chain, your own Monero node, your own
SOCKS5 proxy, or the bundled Tor.

---

📖 Back to [Documentation Index](docs/INDEX.md)

