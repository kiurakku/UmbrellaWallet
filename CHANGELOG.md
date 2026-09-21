# Changelog

All notable releases of **Umbrella Wallet**.

Format follows [Keep a Changelog](https://keepachangelog.com/). Versioning follows [SemVer](https://semver.org/).

## [4.8.0] — 2026-09-21 — the gold design, Taproot and PayJoin, and balances you can trust

A new look, and a lot underneath it. The wallet is gold light on black, its logos follow whichever
theme you pick, and its charts are lines of light. Underneath: Taproot wallets are found and spent,
PayJoin and PSBT arrive for Bitcoin, any ERC-20, TRC-20 or jetton you hold can be sent, and XRP,
Stellar, Cosmos, NEAR and Polkadot can receive and show a balance. And the reason so many balances
said "server did not respond" — a timeout treated as a cancel, and a refresh that asked too much —
is fixed.

The fund-safety and honesty work from the roadmap's P0 queue is below it: a balance the wallet could
not read never reads as zero, and a send never leaves by a route you did not choose.

### A balance the wallet could not read no longer reads as zero

- An explorer that rate-limits, a node that is down, a request the kill-switch refused: each used to
  leave the row showing **0.000000** at **$0.00**, with exactly the confidence of a genuinely empty
  address. That is the wallet telling somebody their money is gone. Unread balances now show **—**
  with the reason underneath.
- A number from the last successful read is kept and labelled as such rather than blanked or passed
  off as current. A real zero from the chain is still a real zero — an empty address is an answer.
- The portfolio total leaves out what it could not read **and says how many assets that was**, instead
  of quietly summing the rest into a wrong number that looks like a right one.

### A send whose route is not the one you chose is refused

- Tor on in Settings but not connected, Tor connected but the traffic routed elsewhere, a proxy typed
  and never applied, or the Tor-only kill-switch armed with nothing to route through: each now blocks
  the send, at Review **and** again at Confirm, because Tor can drop in between. A broadcast is the one
  request that ties an IP to specific coins permanently.
- Asking for nothing still sends over the open internet — that is your choice, and the review already
  says what it costs.

### First run says the things that cannot be said later

- Before a seed exists: non-custodial software by **the fear**, not a financial institution; no
  guarantee of anonymity on a transparent chain; and nobody — including the author — who can restore
  a lost recovery phrase. Plus 18+ and the Terms and Privacy Policy, in all six languages.
- It gates create, import and unlock. Acceptance is recorded against a version of the wording, so
  changed wording asks again.

### Honest wording

- The wallet called itself an "anonymous crypto wallet" and offered "Tor · anonymous traffic". Tor
  hides an IP; it does not make a public ledger private. It now says non-custodial, no account, and
  "hides your IP" — in every language, with a test that refuses absolute privacy claims in any of them.

### The gold design

- **Umbrella**, the theme every new wallet starts on, is now gold light on black: near-neutral blacks,
  cards a step up from the page with hairline borders, white type, and long arcs of gold light behind
  the window. The previous look is still there as **Navy · the classic blue** (Settings → Appearance).
- **The sidebar has names.** The sections are listed with their labels; the page you are on sits on a
  warm wash of the accent. The panel carries the Umbrella mark, the open wallet, and at its foot where
  requests are going, the version and the lock.
- **The action keys are flat tiles** — dark, with the accent in the icon.
- **Charts are lines of light.** The market chart, its sparklines and the movers use a smooth line in
  the accent with a soft glow and a wash under it, and a lit dot where it ends. The curve passes through
  every price and never overshoots a high or low the data did not reach. Up or down is still said in
  green or red, by the figures.
- **Every logo follows the theme** — the mark, the wordmark, the umbrella and the canopy, gold on the
  default theme, blue on Navy, and so on. Only the desktop icon keeps fixed colours, and it is new.
- The Market lists show each coin's mark, price and change, and the table no longer runs the 24h change
  into the status column on a narrower window.

### Fixed — balances that read "server did not respond"

- A request that **timed out** was treated as if you had cancelled it: one slow explorer aborted the
  whole Bitcoin scan, the backup servers were never asked, and the other chains' refresh stopped too.
  Bitcoin, Litecoin and Dogecoin showed as unreadable while a server that would have answered sat unused.
- Busy explorers (429) are now waited out and asked again, a few requests at a time per server; a
  definite answer — refused, unknown host, a 4xx — is returned at once.
- A refresh no longer re-walks twenty addresses on every branch: a full walk runs on unlock and every
  30 minutes (Dogecoin every 2 hours), and in between the wallet re-reads every address it has issued or
  seen used plus three past it. **Limit:** money sent to an address far beyond those, by another wallet
  on the same phrase, appears at the next full walk rather than the next refresh.
- Locking now forgets the previous wallet's scans, so the next wallet — or the hidden one — can never
  have a send planned from someone else's coins.
- **Cardano** read as unavailable on every new wallet: Koios answers an empty list for an address that
  has never been used, and that was taken for "no answer". It is a real zero, and now reads as one.
- **Zcash** asked Blockchair on every sixty-second refresh — about 1,400 requests a day — until
  Blockchair blacklisted the IP. An answer is now reused for ten minutes. Trezor's server, which now
  refuses everything but Trezor Suite, is no longer asked.
- **Monero** with its service off said the server did not respond. It now says the service is off.

### Fixed — Activity overstated Bitcoin you sent

- A payment that returned change to the wallet showed the change as money sent: paying 0.001 BTC from
  an address holding 0.01 appeared as **0.00999 BTC sent**. A later payment funded only by that change
  did not appear at all.
- Each transaction is now judged against every address the wallet has — receiving and change, SegWit
  and Taproot — so "sent" is exactly what left the wallet, and the change addresses' own history is
  read. Bitcoin, Litecoin and Bitcoin Cash.
- **Previous addresses** on the Receive screen now say what each one holds, from the last complete
  scan — or "not checked yet", rather than a number the wallet has not confirmed.

### Solana tokens are shown

- The wallet now reads the SPL tokens at your Solana address — USDC, USDT, PayPal USD, JUP, BONK and
  the rest — under both of Solana's token programs, with the decimals each token's mint reports.
- A token account does not say what the token is called, and names on Solana are free to invent, so
  the wallet names only mints it knows (each checked on-chain before it went on the list). Any other
  token is shown by its mint address as **unverified** and folded away with suspected spam — airdropped
  lure tokens are common there.
- If the read fails, the tokens you already saw stay on screen rather than vanishing.
- **Not yet:** sending SPL tokens.

### Fixed — new chains: prices, and a balance server that is down

- Stellar, Cosmos and NEAR shipped without a price source, so their fiat value read **$0.00** —
  right only while the balance was also zero. They are priced now, and a test fails if any chain the
  wallet shows has nowhere to get a price from.
- Public servers go down: during testing Polkadot Asset Hub's default server stopped answering minutes
  after it had worked, and the wallet (correctly) said "could not read". Now, if you have not chosen a
  server, the XRP, Stellar, Cosmos, NEAR and Polkadot balances try each listed server in turn before
  giving up. If you **have** chosen one, only that one is asked — your addresses never go to a server
  you did not pick.

### Polkadot: receive and balance

- A Polkadot (DOT) account that is **the same one Polkadot.js, Talisman, SubWallet and Nova show** for
  your phrase. Polkadot does not use the BIP-44 scheme every other coin here uses: it derives an
  sr25519 key from the phrase's entropy. The wallet does the same, and each step is checked against
  published output — the curve arithmetic against RFC 9496's own values, the full phrase-to-address
  path against Parity's `subkey` tool, the address format against Polkadot.js.
- The balance is read directly from chain storage, with no indexer and no API key, at the finalized
  block of **both Polkadot Asset Hub and the relay chain**, and the two are added. Since the 2025
  migration most DOT lives on Asset Hub — a wallet reading only the relay chain would show most people
  close to nothing. If either read fails, the balance shows as "could not read", never as half of it.
- DOT locked for staking or governance is included in the number; not all of it may be spendable.
  Both chains' servers can be changed in *Settings → Privacy*.
- **Not yet:** sending DOT.

### NEAR: receive and balance

- A NEAR account from the same phrase, at `m/44'/397'/0'` — the path NEAR's own seed-phrase library
  uses, checked against that library's test. It is an *implicit* account: the 64-character id is your
  public key, so it can receive without registering anything.
- The balance is read at final finality from a server you can change (the NEAR Foundation's RPC by
  default, or FastNEAR's). An account nobody has funded yet is a real zero. Large balances read
  correctly — NEAR counts in 10⁻²⁴ units, more digits than the wallet's number type holds directly.
- Named accounts (`you.near`) and NEAR staked with a pool are not shown.
- **Not yet:** sending NEAR.
- The four new balance readers (XRP, Stellar, Cosmos, NEAR) now treat a malformed server answer as
  "could not read" in every shape the tests throw at them, instead of relying on an outer catch.

### Cosmos Hub: receive and balance

- A Cosmos Hub (ATOM) address at `m/44'/118'/0'/0/0` — what Keplr, Leap and Ledger derive — checked
  against cosmjs's own wallet test, key and address.
- The balance is your **available** ATOM, read from a server you can change (PublicNode by default, or
  Keplr's). ATOM you have staked with a validator is not part of that number, and the wallet says so
  rather than let the figure look like everything you own.
- A pasted `cosmos1…` address is recognised and its checksum verified; an address for another Cosmos
  chain (`osmo1…` and the like) is not taken for a Hub one.
- **Not yet:** sending ATOM, staking, IBC.

### Fixed — a mistyped Cardano address could have been paid

- The Cardano send path decoded the destination without checking its bech32 checksum. One wrong
  character produced a different but structurally valid address — key hashes nobody holds — and a
  payment to it would have been accepted by the network and lost. The checksum is now verified before
  anything is built, and every single-character typo of a real address is refused in the tests.
- The address inspector now calls a mistyped `addr1…` address **invalid** instead of "unverified", and a
  testnet (`addr_test`) address is refused on mainnet.
- One shared Bech32 codec, checked against BIP-173's own test strings, replaces the two private copies
  the Cardano code carried.

### Stellar: receive and balance

- A Stellar (XLM) address from the same phrase, derived by SEP-0005 — the standard LOBSTR, Solar and
  Ledger follow — and checked against the three test vectors the SEP itself publishes.
- The balance comes from Horizon, on a server you can change (the Stellar Development Foundation's by
  default, or LOBSTR's). An address nobody has funded yet is a real zero: on Stellar an address only
  becomes an account once it receives the minimum balance. Anything else is "could not read".
- A pasted `G…` address is now recognised as Stellar, and its checksum catches a mistyped character.
- **Not yet:** sending XLM.

### XRP: receive and balance

- The wallet now has an XRP Ledger address, at `m/44'/144'/0'/0/0` — the path Xaman, Ledger and
  Trust Wallet use, so the same phrase finds these funds there. The key is checked against xrpl.js's
  own test and the address encoding against the example in XRPL's documentation, not against this
  wallet's output.
- The balance is read at the last **validated** ledger, never an open one that can still change. An
  address the ledger has never seen is shown as a real zero — on the XRP Ledger an address only
  becomes an account once it receives the network's reserve, which then stays locked. Any other
  failure shows as "could not read", not as zero.
- The server is yours to choose in *Settings → Privacy*: the XRPL Labs cluster by default, or
  Ripple's own, or any rippled or Clio server you run. It learns your XRP address, like every
  balance server learns what it is asked about; the network screen says so.
- **Not yet:** sending XRP. It stays off until a signed payment is checked byte for byte against the
  reference library, as every other coin's was.

### PSBT: check a payment somewhere else, or sign someone else's

- **Export.** On the Bitcoin review screen, *Export as PSBT* gives the exact unsigned transaction — as
  text to copy or a `.psbt` file — for reading in Sparrow or Electrum before you confirm, or for
  signing elsewhere. It names this wallet's key fingerprint and each coin's path (Taproot coins
  included, under the fields BIP-371 defines), and it reserves the change address as a real send
  would, so the change is found whichever wallet signs it.
- **Sign.** *Settings → Security → Sign a PSBT* reads base64, hex or a `.psbt` file and shows every
  input and output, which are yours, the fee when it can be known, and exactly what the transaction
  takes out of this wallet. Other people's inputs are allowed — PayJoin, CoinJoin and multi-party
  payments look like that — and are pointed out.
- The wallet signs **only coins its own scan found on-chain, at the value it read there**. A PSBT that
  misstates what one of your coins holds is refused; so is one that spends from your address a coin
  the wallet cannot see. That closes the known SegWit trick of lying about an input's amount to make a
  signer overpay the fee. The price: signing needs a synced wallet — this is not an air-gapped signer.
- A fully signed PSBT can be broadcast from the same card, through the same Tor/route check as a send.
- **Verify screen:** Bitcoin now exports two watch-only keys, SegWit and Taproot. The balance counts
  both, so a scanner given only the first would have reported less than the wallet shows.
- **Not yet:** a watch-only wallet with no seed on this machine. It is the same groundwork hardware
  wallets need, and will come with them.

### PayJoin: a payment that does not look like it came only from you

- Paste a `bitcoin:` payment link into Send and the address and amount fill themselves in. If the
  link offers PayJoin (BIP-78), the receiver is asked to add a coin of its own to the payment — so
  "every input belongs to the payer", the assumption most chain analysis starts from, is wrong about
  this transaction.
- The review says it before you confirm, with the most the receiver may take from your change toward
  the fee. Nothing beyond that is possible: the proposal is signed only after the full BIP-78 sender
  checklist passes, plus a separate check that your total cost did not rise past that number. The
  receiver can never redirect the payment — output substitution is always refused.
- If the receiver does not answer, or its answer fails any check, the payment is sent exactly as you
  reviewed it, and the result says which one happened.
- If even that fails to broadcast, the wallet says so **without** offering a retry: the receiver
  already holds a signed copy and may still broadcast it, and paying again could pay twice.
- Only for endpoints on HTTPS or a `.onion`, over its own Tor circuit when Tor is on. A link with a
  plaintext endpoint still pays; it just says PayJoin will not be used.
- **Limits:** sending only — the wallet cannot receive a PayJoin. Tested against a simulated receiver
  with real keys and full signature checks, not yet against a live one; make the first one small.

### A seed from a Taproot wallet is no longer shown as empty

- Bitcoin coins on `m/86'` (BIP-86, `bc1p…`) were invisible: the scan walked only the SegWit account,
  so a phrase restored from a Taproot wallet read as **0 BTC** while the coins sat on another branch
  of the same seed. BTC now walks both, with the same gap-limit and "an error is unknown, not empty"
  rules on each.
- Those coins can be spent — alone, or in one transaction with SegWit inputs, each signed from the
  path it was found on and checked against consensus rules before anything is broadcast. Fees are
  estimated per input type, so a Taproot input is not charged as a SegWit one.
- Change goes back to the branch the inputs came from, on that branch's own index counter. Sending
  a Taproot spend's change to a SegWit address would mark the recipient's output as yours to anyone
  running the usual heuristic.
- Restored Taproot history now shows in Activity next to the balance it explains.
- **What did not change:** receive addresses are still native SegWit. The wallet does not hand out
  `bc1p…` yet. **What it costs:** an empty Bitcoin wallet now asks the explorer about 80 addresses per
  scan instead of 40. Other chains are unchanged.

### Tokens can be sent, not just watched — ERC-20, TRC-20 and jettons

- The Send picker now lists every ERC-20 this wallet actually holds. Until now it could send native
  coins and exactly one token — USDT on Tron, hardcoded — so every other token was money you could
  see and not move.
- It routes on the **contract**, never the ticker: two contracts can call themselves USDC, and only
  one of them is the one you hold. The amount is scaled by the decimals that contract reports, and a
  token whose decimals were never read is **refused** rather than assumed to be 18 — a wrong guess
  there is off by a factor of a trillion.
- The token balance is read from the contract itself at quote time, not from a row that might be a
  minute old, and the review says plainly that the fee comes out of ETH rather than out of the token.
- Unsolicited airdrop tokens stay out of the picker. They remain visible in Holdings behind the spam
  fold; what they do not get is a promotion into the screen that moves money.
- **Tron tokens too.** USDT on Tron stops being a special case and becomes the TRC-20 whose
  contract the wallet already knew; every other TRC-20 you hold appears in the picker beside it,
  with the fee paid in TRX. That path also gained the exact-scaling refusal it never had — it
  used to multiply through `Math.Pow` and truncate, silently dropping the remainder of an
  over-precise amount.
- **Jettons on TON too.** A jetton moves by a message to your OWN jetton wallet — a separate
  contract that then credits the recipient's — so the destination of the message and the
  destination of the money are different addresses, and swapping them sends the tokens
  nowhere recoverable. The message body is pinned cell-hash-for-cell-hash against the
  reference `@ton/core` library, the way the wallet's TON transfers already were. About
  0.05 TON is attached for gas and partly returned; a jetton whose wallet contract the
  wallet does not know still says **Receive only** instead of offering a send it cannot make.
- SPL tokens on Solana are **not** included: their balances are not read yet, so there is
  nothing to send. Saying so beats a picker entry that does nothing.
- **Verify a first send with a small amount** — the encoding and the refusals are covered by tests,
  but no token transfer from this build has yet been confirmed on-chain with real funds.

### Releases are signed, without a signing key

- Every artifact **and** the checksum manifest now carry a build attestation: GitHub signs, keylessly
  through OIDC, a statement that these exact bytes came out of this repository's release workflow at
  a named commit, recorded in a public transparency log.

  ```bash
  gh attestation verify UmbrellaWallet-Setup-<version>.exe --repo thefear078/UmbrellaWallet
  ```

- A checksum only ever proved that a download matches the manifest on the release page — and whoever
  can replace the artifacts can replace the manifest beside them. This is the half that was missing.
  There is no signing key involved, which means there is no signing key for anybody to steal.
- Releases published before this have checksums only. An attestation cannot be added to a build after
  the fact, and a page claiming otherwise would be worth nothing.

### Check this wallet against something that is not this wallet

- **Settings → Security** exports a **watch-only key** for each UTXO chain. Paste it into an explorer
  you chose and it derives the same addresses and reports the same balance — or it does not, and that
  answer is worth more than our reassurance. It cannot spend a coin; it does reveal every address on
  that account, past and future, and the screen says so before it shows you anything.
- **[VERIFY_YOUR_WALLET.md](docs/VERIFY_YOUR_WALLET.md)** was a stub. It is now the actual guide: the
  balance from a third party, where the traffic really goes, the download, the build, who the wallet
  talks to — and a closing section on what none of it proves.

### Coin logos

- Bitcoin, Ethereum, Litecoin, Dogecoin, Bitcoin Cash, Monero, Solana, TRON, Polygon and BNB use a
  new, consistent icon set, and **Zcash finally has one** — it shipped as a supported chain drawing a
  letter glyph beside eleven real brand marks. A test now pins the logo registry to the files on
  disk, in both directions: a registration with no file renders an empty square, which is worse than
  the letter it replaced.

### The hidden wallet has a way in

- A BIP39 passphrase opens a **separate** wallet from the same recovery phrase, with its own
  addresses. The derivation, the balance scan and the spender all honoured one already — the unlock
  screen simply had no field to type it into, so the feature existed and nobody could reach it. It is
  there now, folded behind **Advanced** so the unlock screen does not advertise to somebody watching
  that hidden wallets are a thing this wallet does.

### Activity says which coins it is not reading

- The feed shows what this wallet did plus whatever history an explorer will give us. For a coin with
  no history reader — Dogecoin and transparent Zcash today — a transfer made anywhere else, or before
  this wallet existed, was simply absent, and an empty feed read as "nothing happened" when it meant
  "nobody asked". Those coins are now named above the list, computed from the capability catalog so
  the note cannot claim coverage the code does not have — or keep warning after one is wired up.
- The empty-state hint used to list the covered chains by name and had gone stale (BCH, SOL, TON and
  ADA all gained history since it was written). It now points at that computed note instead.

### Coin control on Bitcoin Cash

- There were three lists of "the UTXO chains" and they had drifted: the balance scan walked
  BTC/LTC/BCH/DOGE, the fee selector offered all four, and coin control — with the private-send
  checklist that reads it — quietly left Bitcoin Cash out. So on BCH the panel that lets you avoid
  linking your own addresses was missing, and the privacy checklist never mentioned linkage, on a
  chain where it is exactly as real as on Bitcoin. One list now.

### Where your requests are going, without asking

- A connection chip in the sidebar (and in the top/bottom nav): **TOR**, **PROXY**, **DIRECT** or
  **BLOCKED**, read from the live route rather than from a setting. Click it and you are in the
  screen that can change it.
- It turns amber for the state that looks like the good one: Tor switched on in Settings, Tor not
  actually carrying the traffic, every request going out in the clear. It reads the same state the
  send gate does, so the chip and a refused send can never tell different stories.

### The project's GitHub account is now `thefear078`

- Every link in the app, the docs, the installer and the release scripts follows it. GitHub redirects
  the old ones, which is exactly why a stale link survives unnoticed — it works, and it is wrong. A
  test now fails if one comes back.

### A password that opens a different wallet

- **Duress password** (Settings → Security). If you are ever made to unlock this wallet in front of
  somebody, the password you hand over works — a real wallet opens, with its own addresses and its own
  history — and it is not the one holding your money.
- The vault file is now the same two-slot shape for **every** wallet, whether a duress password is set
  or not: same size, unused slot full of random bytes, real slot in a random position. A file that
  only became two-slot when its owner asked for a decoy would announce, by its own size, that they had
  asked. Existing vaults are rewritten into that shape the first time their password opens them —
  verified to give back the identical seed before anything is replaced.
- The wallet will never tell you whether a duress password is set. That is the entire point, so
  Settings offers to set one and to remove one and reports neither state — and removing one leaves a
  file indistinguishable from one that never had it.
- What it does not do is printed beside it: it does not hide that other wallets exist on this
  computer, does nothing about somebody watching you type, and cannot help against a copy of the file
  taken before and after. Changing your vault password removes the decoy.
- The decoy is a fresh wallet with its own recovery phrase, shown once — a decoy with nothing in it is
  not convincing, and anything you put there is real money.

### What a send actually cost, said afterwards

- A short report under the send result: whether the node you broadcast to saw your IP, whether the
  kill-switch was armed, that the ledger keeps the amount and both addresses forever — and, when the
  spend drew on more than one of your addresses, **how many**, because "several" is exactly the
  vagueness that lets somebody assume it was two when it was nine.
- It credits what went right too (a single input, change to a fresh address, coins you picked
  yourself), and ends with the one nobody can fix: the person you paid knows you paid them.

### Privacy Radar states its limits

- Every signal behind the grade now carries what it does **not** do, directly underneath. "Tor is on"
  reads to most people as "I am anonymous"; what it means is that the servers being asked see an exit
  node instead of your address, and nothing at all about the addresses they were handed.

### Supply chain: the pins are checked against what upstream signed

- A SHA-256 written into this repository only ever agreed with itself. Both fetch scripts now verify
  the project's own **signed** sums file — Tor's detached signature, Monero's clearsigned hashes —
  against a pinned key fingerprint before trusting the hash, and release builds refuse to proceed
  when that check cannot run.
- **The Tor pin was already broken:** the pinned version had been pruned from the mirror, so a fresh
  clone got a 404 while the hash beside it belonged to a version that still exists. A repo check now
  keeps the scripts and THIRD_PARTY_NOTICES describing the same download.
- A published release can be re-checked against its own manifest at any time
  (`scripts/verify-published-release.sh`) — the workflow verifies before publishing; this verifies
  what the page serves afterwards.
- The README's coin table and the roadmap's matrix are now checked against the same source of truth
  the Send picker reads, like the coins document already was.

### Who published this build

- Settings → Guide gains **About this build**: version, publisher and copyright read from the
  assembly, the licence in one line, and links to the only official repository, releases and channel.
  The .exe's own properties and the installer now name **the fear** too, from one source.

### Proof

- The restore scenario runs end to end offline for BTC, LTC, BCH and DOGE: twenty addresses issued,
  payment on #15, local state deleted, then found **and spent** from the phrase alone — and the
  past-the-gap-limit case is pinned as the limit it is, rather than hidden.
- A CI job proves the kill-switch opens no socket at all, with the kill-switch off as the
  counter-proof, plus a scan for any HTTP client built outside the one place that wires it.
- 837 offline tests.

## [4.7.0] — you choose which server sees your addresses

Your keys never leave your device. That is true, and every wallet says it.

What almost none of them say is that a wallet still has to **ask somebody** what is on the chain — and
on a public chain, asking means handing over the address. Whoever answers can tie together every
address you ask about in one session. Tor hides your IP; it does not un-send an address.

This release is about that, and about three bugs found while looking into it.

### Who answers for your money

- **Every chain's server is now yours to choose** — Bitcoin, Litecoin, Bitcoin Cash, Dogecoin,
  Ethereum, Solana, TON, Tron, Cardano and Monero. Pick a different company, or point the wallet at a
  node you run (Settings → Privacy → Where each chain is read from).
- **Monero node selection.** It had one node compiled in — the same for every user, named nowhere in
  the interface, unchangeable. It now names the node, offers alternatives, and takes your own,
  including a `.onion`.
- **Two refusals, enforced rather than warned about.** A `.onion` node is never used without Tor and
  is **never silently swapped for a clearnet one**; a plain `http://` endpoint is refused outright,
  because choosing your own server *for privacy* and then sending addresses in clear would be worse
  than not choosing. Credentials in a URL are refused too — every hop along the way logs the URL.
- **Once you pick a server, there is no falling back to ours.** Rerouting your addresses to the
  default is exactly what choosing was meant to prevent. The wallet reports "unknown" and lets you
  decide.
- Removed `node.community.rino.io` from the shipped Monero nodes — it no longer resolves at all.

### Who this wallet talks to

- **A full list** in Settings → Privacy: every server, who runs it, why it is contacted, and what it
  learns — sorted so the ones handed your actual addresses come first. Four contact modes, because
  they are genuinely different exposures: automatic, on-demand, opt-in, and never-contacted (a link
  your browser follows, not the wallet).
- **It cannot go stale.** The build fails if a server appears in the code without appearing on that
  list, and fails the other way if the list names a host the code no longer uses.

### Private send, as one switch

- Tor, the kill-switch, waiting for bootstrap, narrowing inputs, a fresh change address — one switch
  instead of a checklist nobody remembers.
- Beside it, **what no switch can change**. Monero shows an empty to-do list and its limits all the
  same, because "nothing to turn on" must never read as "nothing to know".

### Fixed — money that was going missing from the display

- **Bitcoin Cash and Dogecoin were read one address deep.** Change from a send lands on an internal
  address by design, so after sending either coin the displayed balance dropped to whatever was left
  on the first address. The money was never at risk; the number was wrong, in the direction that makes
  people think they have lost funds. Both are now scanned across every address, like BTC and LTC.
- **A fresh receive address per payment** now works on BCH and DOGE too, which follows from the
  above: the wallet only offers an address it can also find and spend.
- **Bitcoin balances no longer fail when one public explorer rate-limits.** Esplora had a single base
  URL; Blockstream returning 429 left the wallet with no Bitcoin balance at all. It now tries another
  instance and stays there for the rest of the scan.
- **A failed Send preparation now says so on the Send screen.** It reported into the title bar, so
  pressing Review appeared to do nothing at all.
- **The balance cache no longer collapses the L2s.** ETH on mainnet, Arbitrum, Base, Optimism and
  Linea are five balances sharing one symbol at the same address; the cache keyed on symbol + address
  and threw on the unlock path.

### Coins

- **Jetton balances on TON**, including USD‑tether — how most people hold dollars on Telegram's chain,
  and something this wallet simply did not show. Tether on TON calls itself `USD₮`, which no price
  feed recognises, so a real dollar balance rendered as $0.
- **Linea** — send and receive. **zkSync Era** — balance only: a plain transfer there does not cost a
  flat 21,000 gas, so sending would strand the transaction, and the wallet says so instead.
- A balance the wallet can read but not spend now says **Receive only** rather than "Ready".
- USDT now exists on two chains under one symbol, so the Send screen pins each token's balance to the
  one chain it can actually be spent on.

### Fixed — "delete everything" did not

- **Your address book survived a wallet delete**, and it is stored in **plain text**. So did your
  private transaction notes, the count of addresses ever issued, and the price cache. A person
  deleting their wallet believes it is gone; what stayed behind was precisely the part naming who they
  were dealing with. All four are wiped now.
- The wiper is no longer maintained by hand: the build fails if the app writes a file under its data
  directory that the wiper does not handle, or that is not named as a deliberate exception (the
  bundled Tor client, which is a program rather than user data).
- The Danger Zone now states exactly what goes and what stays, in all six languages.

### Added — the rules, written down

- **[MANIFESTO.md](MANIFESTO.md)** — the twelve rules this wallet is held to, each one there because
  it was broken at least once, and an honest note on what following them costs.
- Three new sections in the in-app guide (English and Ukrainian): who answers for your money, who this
  wallet talks to, and what deleting everything actually deletes.

### Tests

- **731 offline tests**, up from 617. The suite no longer touches the public internet at all: it was
  quietly querying real explorers on every wallet it created and only looked fast because those calls
  were failing.
- Each run gets a throwaway data directory, and the classes that share process-wide state no longer
  run in parallel with each other — two tests had begun failing against unchanged code.

## [4.6.0] — history, privacy tools, safer backups & a friendlier Send

Rolls up the Bitcoin Cash / Zcash / L2 / swap work below, plus a wave of wallet, privacy and safety
features. Every path is covered by offline tests (**453 green**); newly-enabled live send paths should
still be checked with a small amount first.

### Wallet & Send

- **Bitcoin Cash transaction history** in the Activity feed (Haskoin), so BCH is now fully complete
  (receive + balance + send + swap + history).
- **Network-fee speed selector** on UTXO sends — Economy / Standard / Priority. Standard is byte-for-byte
  the old rate; every level stays inside the chain's safe fee band.
- **Enter send amounts in USD** — type a fiat amount and the coin amount fills in; the coin field stays
  the value that is actually signed.
- **Quick amount presets** — 25% / 50% beside the fee-aware Max.
- **Export transaction history to CSV** — local, read-only, RFC-4180 with a CSV-injection guard.
- **Sort the Holdings list** — by value, 24h change or name.

### Privacy & safety

- **Privacy Radar (per-send)** — a local, offline read of a spend's address-linkage and network privacy.
- **Privacy Radar (wallet-wide score)** at the top of the Security Center — a Tor-weighted grade with the
  single biggest privacy win named.
- **Address checker** (Settings → Privacy & Tor) — paste any address to see its network and whether the
  checksum is valid; fully local.
- **Sign & verify message** (Settings → Security) — prove control of your Ethereum address (EIP-191);
  a signature can never move funds.
- **Encrypted transaction notes** — private per-tx bookkeeping, encrypted at rest with a seed-derived key.

### Backups

- **Recovery-phrase backup confirmation** — after showing the 24 words, the wallet asks for three of them
  back (at random positions) before entering the workspace.
- **Tap-to-copy the recovery phrase** on both the create screen and Settings → Reveal phrase (clipboard
  auto-clears; the note still says paper is safest).

### Reliability

- **CI Tor bundle fix** — the pinned Tor was pruned upstream; bumped so the Windows release job builds.

## [Unreleased] — Bitcoin Cash (full), Zcash receive, Ethereum L2 sends, more swap pairs

New coins are added one at a time, receive + balance first; a coin's **send** is enabled only once its
signing path is proven by tests (the fund-safety rule). Every path here is covered by offline tests
(≈385 green); newly-enabled live send paths should still be checked with a small amount first.

### New coins

- **Bitcoin Cash (BCH) — send + receive + balance.** Real BIP44 CashAddr address (`m/44'/145'`), pinned
  to the standard test vector. **Sending is live**: a real UTXO spend over Haskoin (UTXOs, fee, broadcast)
  signed by the same proven spender as BTC/LTC/DOGE with NBitcoin's **SIGHASH_FORKID** — the FORKID
  signature and change path are pinned offline. Balance/UTXOs go through Haskoin (Blockchair's keyless
  tier IP-blacklists a busy caller). Verify a first BCH send with a small amount.
- **Zcash (ZEC) — transparent receive + balance.** Real transparent `t1…` address (`m/44'/133'`,
  Zcash's `0x1CB8` prefix). Honestly labelled the **public/transparent** side of Zcash — this is **not**
  a shielded z-address, and the privacy note says so. Pinned to a vector plus a non-circular proof that
  the t-addr encodes the same key-hash NBitcoin computes for that key. Balance is best-effort (Trezor
  Blockbook, Blockchair fallback). Send stays off (transparent spend path not yet wired).

### Sending

- **Ethereum L2 sends: Arbitrum, Base, Optimism.** Native **ETH** on the three major rollups is now
  sendable from the same 0x address as mainnet — same EIP-155 signing, only the chain id differs
  (pinned by tests, including that each chain id yields a distinct signature so a tx can't be replayed
  across chains). Balances on these networks already displayed; now they can be spent too.

### Swaps

- **More non-custodial swap targets (THORChain).** You can now receive **BCH, AVAX, BNB, and the
  stablecoins USDC/USDT** as swap outputs (delivered to your own address), on top of BTC/ETH/LTC/DOGE —
  paying from BTC/LTC/DOGE/ETH. Every asset id and address format was checked against the live THORChain
  API first (e.g. BCH must be the CashAddr *body*, and USDC/USDT arrive as ERC-20 on your Ethereum
  address). Still fully keyless and non-custodial; parsers pinned to real captured quotes.

## [4.5.0] — 2026-09-03 — DOGE send, more swaps, Security Center, asset pages

Real new sending and swapping, on-chain history for three more chains, privacy hardening, and three
new screens (Security Center, asset details, market overview). Every send/sign path is covered by offline tests (332 green); the live send and
swap paths on newly-enabled chains should still be checked with a small amount first.

### Sending & swaps

- **Dogecoin send.** DOGE is now spendable (it was receive-only). Real UTXO spend over BlockCypher —
  UTXO discovery, fee, and broadcast — signed by the same proven spender as BTC/LTC, then wired into
  the send flow. Offline signing is pinned in tests.
- **More cross-chain swaps.** The THORChain swap now works **from BTC, LTC, DOGE and ETH** (was
  BTC/LTC only) to any of BTC, ETH, LTC, DOGE — **12 pairs**. UTXO chains carry the swap memo as an
  OP_RETURN; **ETH** carries it as calldata of a `router.depositWithExpiry` call (the encoding is
  pinned to the router selector offline). Coins with no THORChain pool (SOL/TON/ADA/TRX/XMR) are
  honestly not offered — use an external venue under Discover.

### On-chain history

- **Transaction history for TON, ADA and SOL** (previously balance-only). TON via toncenter, ADA via
  Koios, SOL via the public Solana RPC (best-effort — the free RPC rate-limits). All read-only through
  the Tor-aware client; each parser is unit-tested against captured payloads.

### Scam control

- **Address-poisoning defence.** Before a send, the destination is checked against the addresses this
  wallet already knows — its own receive addresses, the address book and everyone it has paid. A
  destination that looks almost identical to one of them (same first and last characters, different
  middle) raises a red warning: that is the fingerprint of an address-poisoning scam, where the
  attacker seeds your history with a lookalike hoping you copy the wrong one. Sending to one of your
  own addresses is flagged too. It only ever warns — it never blocks a send and never touches the
  network. (`AddressSafetyInspector` in Core, 15 offline tests.)
- **Ethereum address checksum (EIP-55).** A mixed-case `0x…` address whose casing doesn't match its
  checksum has almost certainly been mistyped or swapped — a single altered character breaks it. Send now
  warns before it can be signed. All-lowercase / all-uppercase addresses carry no checksum and are accepted.
  (`EvmAddress` in Core, pinned to the standard's vectors, 14 offline tests.)
- **First-time recipient note.** Sending to an address you have never used before shows a quiet reminder to
  double-check it — only when the address is well-formed and you actually have contacts/history to be “new”
  against, so a brand-new wallet isn't nagged on every send.
- **Anonymity reminder on send.** If Tor is off (and the kill-switch isn't forcing it), the review step notes
  that the node you broadcast to would see your IP — with a nudge to turn Tor on.
- **Fix:** the auto-lock interval now shows in your language (“5 хв”) instead of English “5 minutes”.
- **Trusted-contact badge.** When the destination is a saved contact or an address you've paid before, Send
  shows a green confirmation (naming the contact) — so a trusted address reads as safe and the warnings stand
  out by contrast. Together the send screen now covers wrong-network, EIP-55, poisoning, own-address,
  first-time and trusted destinations.

### Fixed — amount fields (important)

- **A comma decimal could be read as ten times the amount.** Typing `0,5` in Send or Swap — normal in
  most of the languages this wallet ships in — was parsed as **5**, because .NET reads the comma as a
  group separator and never checks group sizes. The two-step review still showed the real figure
  before anything was signed, so nothing could be sent without it being on screen, but the field was
  wrong. All amount input now goes through one parser (`AmountInput`) whose rules are pinned by tests:
  a lone `.` or `,` is always the decimal point (so `1,234` is 1.234, never 1234 — ambiguity always
  errs towards the smaller amount), grouping has to actually look like grouping, spaces (including the
  non-breaking ones locale formatting uses) are ignored, and anything it cannot read confidently is
  refused instead of guessed at.
- **The wrong-network warning is now tested.** The shape check that warns when a destination does not
  look like an address on the selected chain moved into Core with a full matrix — every chain accepts
  its own address forms, rejects foreign ones, and stays silent where there is no rule.

### Privacy & security

- **Monero is now fail-closed like the clearnet kill-switch.** When Tor-only mode is on but Tor isn’t
  connected, the Monero node connection is refused rather than falling back to a direct connection that
  would expose your IP to the node.
- **Receive warns before you reuse an address.** When the address on screen already has on-chain
  history, Receive says so and offers a fresh one — handing the same address to two people lets anyone
  reading the ledger tie them together. The verdict is evidence-only: the wallet’s own scan state can
  prove an unused index offline, and if the explorer cannot be reached the screen says nothing rather
  than calling a used address “fresh”.
- **Activity feed is real events only.** UI-preference changes (theme, currency, layout…) are no longer
  logged, and legacy “Theme changed” rows are purged; repeated “Vault unlocked” rows collapse into one.

### Under the hood

- **A journey smoke suite** drives the wallet end to end through the same commands the buttons use:
  create → back up → lock → unlock → receive a real address → refuse a bad send → verify a backup.
  It is the safety net for the ongoing refactor below.
- **MainViewModel is being split up.** Send (with coin control), Swap, Activity/history, the market
  chart, the market overview, the asset page, the Security Center and the command palette now live in
  their own partial-class files — 6 100 lines down to 4 500 in the main file, with no behaviour change
  (it is still one type; only the files moved). All 332 offline tests stay green across the move.

### Interface — Kraken-style refresh

- **Dashboard widgets, tidied.** The four look-alike stat tiles became two distinct, useful modules — a live
  Top-movers list and an allocation bar — beside the balance card. Scrollbars are hidden everywhere (wheel still
  scrolls), the selected asset row is properly rounded, and the no-animation balance card is a clean gradient
  instead of a photo with bright edges.
- **Curated themes.** Trimmed 24 palettes to 14 visibly distinct ones (dropped near-duplicate blues, reds, golds
  and violets); kept Umbrella, the fear noir, signal red, OLED black, sunset, Uniswap, ocean, Binance gold,
  Telegram, WhiteBit lime, Bitcoin orange, Kraken, Nord and Dracula.
- **Settings, brought current.** Removed the avatar and banner pickers (nothing displayed them) and the static
  security bullet list (the live Security Center replaces it, reachable now from a shortcut in Settings); back
  buttons are plain text; the balance-card video hint no longer mentions a photo.
- **News** items are single clean cards; **market range** switches (1H…) refresh only the open coin, instantly.

- **Branded, scannable receive QR.** The QR now renders with rounded modules, styled finder eyes and the
  Umbrella mark in the centre (error-correction H, so the mark never breaks a scan — verified by decoding
  the rendered image). The developer “Advanced” path toggle was removed from the receive popup.
- **Fuller portfolio.** Live stat tiles (assets, 24h change, top mover, networks) sit beside the balance
  card so the dashboard reads full and balanced instead of a lone card over a list.
- **Uniswap-style Market chart.** The coin chart now opens as a clean line over a soft gradient area by
  default (candlesticks are one tap away), with 24h High/Low/Volume tiles beneath.
- **DEX-style Swap.** A stacked “You pay → You receive” widget with a circular flip button, replacing the
  two plain dropdowns.
- **Crisper coin icons** (real round logos, no redundant colour disc, no cropping) and **icons on the
  Discover hub cards**.
- **The “the fear” wordmark is now gold**, matching the gold-ghost mark; the app defaults to **English**
  on a fresh install (it used to follow the OS locale).

- **New the fear maker's mark:** the gold ghost, background removed, now sits beside “the fear” on the welcome screen.

- **Quick actions are now round, labelled keys.** Receive / Send / Swap / Buy / Market sit as accent
  discs with a word under each (they were unlabelled squares), so the primary things you do read at a
  glance and light up on hover.
- **Holdings are clean rows, not a spreadsheet.** The column-header table is gone; each coin is a card
  row — mark, name and ticker with a tinted 24h pill on the left, fiat value over the coin amount on
  the right — and the whole row opens that coin’s asset page.
- **Balance typography tightened** so the currency symbol, figure and cents sit on one clean baseline.
- **Every icon-only control has a spoken name** (roadmap §8.2): the sidebar rail, the lock button and
  the quick actions now carry an accessibility name, not just a tooltip — screen readers can address
  them, and so can UI tests.

### Interface

- **Icon-only sidebar.** A slim rail of grouped section icons (labels on hover), the launch logo as the
  top button, and a lock icon at the foot — no more text nav or footer blurb. The active section, the
  balance card, and the action icons all follow the **active theme’s** colour now (they used to be a
  fixed blue/violet that clashed with other themes).
- **Balance as a credit-card-sized card** (not stretched full width), with animated rain behind it by
  default and a toggle in Settings to swap in a still photo. The % change sits in a tinted pill.
- **Compact, icon-only quick actions**, a condensed scrollable assets list, and **click a coin** (the
  `›`) to open its chart.
- **Themes trimmed** to a tighter curated set (the noisy/duplicate ones were removed); the sidebar
  background is now just the theme colour, not a photo.
- **Security Center.** A new section that answers “what is actually protecting this wallet right
  now?” — Tor routing, the Tor-only kill-switch, your proxy, idle auto-lock, lock-on-minimize,
  clipboard auto-wipe, screen-capture blocking, address rotation, telemetry, backup and release
  verification. Every line is read from live settings, a protection that is off says so and offers the
  fix on the spot, and the “X of Y protections active” score counts only things you can switch on —
  it is never padded with facts.
- **Asset details page.** Click a coin in your holdings to get one screen for it: what you hold and
  what it is worth, the live price and 24h move, the address on this device, what this build can
  really do with that coin (read from the chain catalogue, so it cannot over-promise), the chain’s
  privacy note, and the movements that touched it. Receive / Send / Swap / chart are one click away,
  and an action only appears where there is a real derived address behind it.
- **Market overview and watchlist.** Top gainers, top losers and your starred coins sit above the
  market list. Both are computed from prices the wallet has already fetched — no new endpoint and no
  extra requests — and a coin with no live price is left out rather than shown as a flat 0%. The
  watchlist is a local list of tickers that never leaves the device.
- **The command palette is keyboard-complete.** Ctrl+K, then ↑/↓ to walk the results (they wrap) and
  Enter to run the highlighted one; the highlight follows the list as it scrolls.
- **The send and backup screens speak your language.** Every error and status message in the money
  flow — “unlock first”, “balance isn’t fully synced”, coin-control refusals, “prepare the transfer
  first”, and every backup verdict — is now translated in all six languages instead of being English
  only. A test scans the source so a hardcoded sentence cannot creep back into a money screen.
- The unlock screen’s “Advanced” passphrase field was removed (hidden-wallet passphrase entry is gone
  from unlock); the app/taskbar icon and in-app logos are unchanged.

## [4.4.0] — 2026-08-18 — premium redesign, hidden wallets, Tor kill-switch

A new look and two headline privacy features, on top of everything in 4.3.0 (which never shipped
publicly — its notes are kept below).

### Premium redesign

- **A whole new visual system.** Deep navy/graphite glass instead of flat black, big rounded cards
  (26px), the cyan→blue→violet Umbrella signature gradient on the primary actions, taller inputs,
  and a soft-glowing capsule for the active sidebar item. The default theme is now **Umbrella ·
  premium**; all 27 palettes inherit the new rounded-glass structure.
- **Animated intro.** A small centred splash window — the umbrella mark scaling and fading in over a
  breathing blue glow — plays first, then the wallet opens.
- **Dashboard.** The balance card carries the signature sweep; the quick actions get brand-coloured
  icons (receive violet · send blue · swap cyan · market green).
- The app/taskbar icon and the in-app logos are unchanged.

### Hidden wallets (BIP39 passphrase)

- **A passphrase field on unlock (behind “Advanced”).** Empty opens your normal wallet; any value
  opens a wholly separate **hidden wallet** from the same recovery phrase + vault password — for
  plausible deniability. The passphrase is never stored (that *is* the deniability), and a different
  passphrase simply opens a different wallet, so there's no “wrong” one.
- Built on an ambient-passphrase deriver shared by scanning and signing, so the shown, scanned and
  spent addresses can never disagree. Cardano (whose Icarus scheme can't honour a passphrase) is
  hidden in a passphrase wallet rather than leaking the base address. Pinned by tests including the
  canonical BIP39 “TREZOR” seed vector and a shown-address == signing-key check.

### Privacy

- **Tor-only kill-switch.** Settings → Privacy: fail closed — if Tor is off, still connecting or
  drops, the wallet refuses to touch clearnet instead of leaking your IP. Applies to every request.
- **Verify Tor.** A one-click check (via check.torproject.org, same route as balances) that proves
  your traffic really exits through Tor — or, with the kill-switch on and Tor off, that clearnet is
  blocked.

### Also

- Every remaining screen is now translated across all six languages; fiat amounts and dates follow
  the interface language's locale. Keyboard focus ring + Enter-to-submit; the Portfolio recent list
  shows real on-chain transactions.

## [4.3.0] — 2026-08-15 — full HD wallet, verifiable releases, honest support

The big one: Bitcoin & Litecoin are now a **real HD wallet**, releases are
**verifiable end-to-end**, and the app stops claiming what it can't do.

### Wallet core (BTC/LTC)

- **Multi-address HD wallet.** Balance and history are aggregated across *every*
  derived address (external + internal change), discovered by a gap-limit scan
  (20) — not just receive #0. A transient explorer error now shows "not fully
  synced" instead of a wrong, lower balance.
- **Spends across all addresses.** A transfer selects UTXOs from any owned
  address and signs each input with its own key; change returns to a **fresh
  internal address**, never a reused public one. The old key-#0-only signing
  path is gone.
- **"Generate new address" is back — safely.** Receiving on a rotated address is
  now sound because the wallet finds and spends it. Indices are persisted before
  an address is shown (fail-closed) so a crash can never lose a published one.
- Proven by an offline receive → sum → spend → change → restore integration
  test, plus a live smoke against the real explorer API.

### Trust & verification

- **Verify a backup (§6.5).** Settings → Backup → "Verify backup" decrypts a
  backup with your password and confirms it holds a valid recovery phrase — so
  you know it's restorable *before* you need it — without ever revealing the seed.
- **Verifiable releases.** CI asserts the release has exactly the expected
  artifacts and self-checks `SHA256SUMS.txt`; a version-consistency gate keeps
  VERSION, the installer, README and this changelog in lockstep.
- **Pinned, hash-verified Tor & Monero.** The bundled binaries are pinned to a
  version and verified against the projects' official signed hashes, failing the
  build on any mismatch. Recorded in `THIRD_PARTY_NOTICES.md`.

### Honesty

- **No coin is shown "Ready" unless it can actually send.** Dogecoin derives an
  address and syncs a balance but has no send path, so it is now shown as
  "Receive only" rather than as spendable.
- **Docs tell the truth.** The stale web-product docs (React/NestJS/Prisma) are
  archived; the desktop README no longer claims "version 1.7.0", a backend, or
  macOS.
- **Send offers only what it can send.** ADA and the EVM side-chains (BNB, MATIC,
  AVAX, FTM, CRO) were offered in the picker but rejected before signing; TON
  worked but was hidden. One capability set now drives both the picker and the
  guard, pinned by a test. A full per-network capability matrix (receive /
  balance / send / history / swap / tokens / maturity) is the single source of
  truth (`ChainCatalog`).

### Look & feel

- **New app icon** and **real round coin logos** for 21 assets (with a coloured
  glyph fallback).
- **Five-item navigation** — Wallet · Activity · Swap · Discover · Settings.
  Buy / P2P / Market / News moved into a **Discover** hub (external services
  labelled honestly); Receive/Send live under Wallet.
- **Connection status is always visible** in the sidebar (Tor / Direct /
  proxy), with a note that the blockchain is public either way.
- Quieter default motion, aligned button heights, a much fainter rain layer, and
  more of the UI localized across all six languages.

### Send — financial transparency (§4)

- **The review shows the full destination**, never truncated — you can verify
  every character, including long Monero addresses that were previously shortened
  and impossible to check.
- **Amount carries a live fiat estimate** (from the latest fetched prices), shown
  as you type and again in the review.
- **Total debit reads as its own line**, kept separate from the network fee, so
  what actually leaves your wallet is unambiguous.
- **Network-check-on-paste** — an advisory warning if a destination doesn't look
  like the selected network, before any funds move.
- **Local address book** — save, label and reuse destinations per asset. Stored
  on this device only; public addresses only, never keys.

### Receive — less technical noise (§5)

- **Previous addresses** stay listed and copyable, so funds sent to an earlier
  address are never orphaned.
- **Requested amount** folds into a standard BIP21 payment URI (BTC/LTC/DOGE) so
  the sender's wallet pre-fills it.
- **Explicit token-network warning** on USDT/USDC: only the shown network is safe.
- The derivation path moved behind an **Advanced** toggle.

### Activity — one merged feed (§6)

- **Transactions and Activity are now one screen.** Real on-chain history for your
  own addresses is merged with local events, deduped by explorer link.
- **Confirmation status per movement** — Confirmed / Pending / Failed. A broadcast
  send shows *Pending* until it settles; a failed broadcast shows *Failed* with a
  **Retry** that re-opens a pre-filled Send (never auto-broadcasts, so nothing can
  be sent twice).
- **Filters** by type, asset, status and date range, plus a **last-synced** stamp
  and an on-demand **Refresh**.

### Privacy & anonymity

- **Tor-only kill-switch (block clearnet).** A new Settings → Privacy toggle makes
  the wallet **fail closed**: with it on, any request that would go to clearnet is
  refused at the transport layer, so a Tor that is off, still connecting or dropped
  can never silently de-anonymise you. It covers every request the wallet makes
  (balances, prices, history, swap quotes, broadcasts) and is applied at startup
  before the first call. Turning it on also switches Tor on.
- **Clipboard auto-clear** already wipes a copied address after a delay you choose.
- Reminder of what was already true and stays true: bundled Tor, native Monero,
  screenshot-capture protection, read-only exchange links, and **zero telemetry**.

### Full localization & polish

- **Every screen is now translated** across all six languages (en/uk/ru/zh/es/de) —
  Discover/Buy, P2P & DEX, News, Market, NFT, Staking, the Portfolio overview and
  the onboarding/Swap/Watch/Exchange flows that were still partly English.
- **Fiat amounts and dates follow the interface language's locale** (e.g. `1 234,56`
  for uk/ru/de); crypto amounts stay in the universal `.` form.
- Keyboard focus ring + Enter-to-submit on unlock; screen-reader labels on icon-only
  buttons; the Swap review now matches Send's structured layout; the Portfolio
  "recent activity" rail includes real on-chain transactions.

> A small real-amount BTC/LTC send is still recommended as a smoke test before
> relying on rotated addresses. Authenticode code-signing awaits a certificate.

## [4.2.1] — 2026-08-15 (test build) — honesty fixes

- **Pulled the "Generate new address" button (#1).** It derived the next HD receive index, but the send
  path still signs only with key #0 — so any coins received on a rotated address would have been
  **unspendable**. Rather than ship a privacy feature that can strand funds, the button is removed until
  the wallet can scan and spend across every issued index (balance scan, per-address UTXO signing,
  internal change addresses, gap-limit restore). The derivation building blocks (`HdAddressDeriver`
  index support, `AddressIndexStore`) stay in the tree, tested, for that work.
- **Release checksums are now produced by the pipeline (#2).** `.github/workflows/release.yml` gained a
  `checksums` job that runs after the Windows + Linux builds, pulls every attached artifact and writes
  one `SHA256SUMS.txt` back onto the release — so verification isn't a manual afterthought. README now
  documents how to check it. (Authenticode code-signing still needs a certificate you provide.)

## [4.2.0] — 2026-08-15 (test build, superseded by 4.2.1)

- Address-privacy and release-checksum work that 4.2.1 corrects — see above. This build's
  "Generate new address" button could strand funds and its checksum file was attached by hand, not by
  the pipeline; do not use it.

## [4.1.0] — 2026-08-15 (test build) — honest self-custody cut

- **Removed Staking (#4)** and **NFT (#3)** from navigation — an APR list with no real staking action,
  and a preview-less NFT list, were misleading placeholders. Gone until they are genuinely useful.
- **Removed the desktop "Mobile mode" toggle (#2)** — it was a phone-shaped desktop, not a real mobile
  platform; a real mobile-first build comes before any Android claim. Previously-saved state is forced off.
- **Simplified the assets table (#1)** — dropped the Price column (it lives on the coins Market page)
  and made name/value more legible: Name · Amount · Value · 24h.
- 33/33 VM/registry/flow tests pass; full crypto suite green in chunks.

## [4.0.4] — 2026-08-15 (test build)

- **Selected-coin / single-coin wallets (#7)** — Settings → Wallets: choose which coins a wallet
  shows (tap coin chips; none = all). A wallet limited to one coin derives/shows only that coin.
- 141/141 tests pass across chunks (0 failures).

## [4.0.3] — 2026-08-15 (test build)

- **Staking is now dynamic (#3)** — personalised to what you hold: coins you own show first with an
  estimated yearly reward from their live value; coin badges added. Rebuilt on every balance refresh.
- 147/147 tests.

## [4.0.2] — 2026-08-15 (test build)

- **Connect removed from the menu (#5)** — the quick-action tile is now Swap; watch-address markup
  stays in code but is no longer in navigation.
- **More Ukrainian/RU/ZH/ES/DE coverage (#15/#2)** — Receive / Send / Buy / NFT / Staking descriptions
  now follow your language instead of staying English.
- **Toasts in more places (#8)** — opening an external venue/link now shows a top-centre toast.
- 147/147 tests.

## [4.0.1] — 2026-08-15 (test build)

- **Coin logos** — brand-coloured coin badges now show on **Market** and **Receive** rows too (matching
  Holdings), so every coin is recognisable at a glance. No external icon assets bundled.
- **Developer fee → your dedicated TRON/USDT wallet** (`TNvxWSh…`). Note: TRON send-path fee routing
  isn't enabled yet, so nothing is deducted on TRON until that's implemented and tested on-chain —
  the address is in place for when it is.
- 147/147 tests.

## [4.0.0] — 2026-08-15

### On-chain transaction history (new)
- The Transactions section now shows **real on-chain history for your own addresses** — including
  transactions made **before** you ever opened the wallet. Covers **TRX (native + USDT/TRC-20),
  Bitcoin, Ethereum and Litecoin**, via keyless public explorers, routed through Tor / your proxy
  like everything else. Merged with local activity and de-duplicated.

### Richer token pages
- Opening a coin shows a **24h High / Low / Volume** row (from the same Binance feed).
- **Optional market-data connector (CoinGecko)** — **off by default** so the privacy-first wallet
  contacts no third party unless you enable it (Settings → Privacy). When on, token pages also show
  **Market Cap, FDV and Volume**.

### Wallets
- **Colour-tag your wallets** — a colour ring on each wallet's badge + a swatch picker
  (Settings → Wallets), persisted.

### Stickers (yours) — all toggle with the other animations
- Welcome: greeting sticker beside the umbrella, **GhostPepe** (Anonymous), **encryption** (Your keys).
- **67** in NFT, **Recieve** on Receive, **Sending** on the send animation, **up/down** on the market chart.

### Security & privacy
- **Lock on minimize**, **hide balances by default**, custom SOCKS5 proxy, IPv4/IPv6 control,
  clipboard auto-clear (Settings → Security / Privacy). Re-verified vault (Argon2id + AES-256-GCM),
  screenshot blackout, KDF-parameter guard.

### Fixes
- Nav bar reappears on Top/Bottom layouts (regression) · green chart no longer draws a red line ·
  unlock screen vertically centred · quick-action tiles no longer clip · Activity events are separate
  cards · shorter top-centre toasts, now shown across all sections · new Telegram news logo ·
  removed the umbrella-logo glow.

### Still ahead (honest — each its own piece of work)
Full in-page swap widget + swap between **all** pairs (needs an aggregator), per-wallet single-coin
support, Telegram-gift NFTs (TON NFT API), SOL/DOGE history, real per-coin logos, and the
developer-fee routing decision. Being done deliberately, with your testing, not a blind mass rewrite.

### Verified
146/146 tests pass; smoke-launches clean (desktop + mobile). Updates never touch your data.

## [3.5.1] — 2026-08-15

### Market / token detail (toward the Uniswap token page)
- Opening a coin now shows a **24h stats row — High · Low · Volume** — beneath the chart, pulled
  from the same Binance feed the prices use (no new data source, no extra tracking, same Tor/proxy
  route). Converted to your display currency; volume shown compact (e.g. 4.6B).

> Market cap / FDV / TVL / 52-week range and an in-page swap widget need an external market-data
> service (e.g. CoinGecko/DeFiLlama) — that will land as an **optional, off-by-default** connector so
> the privacy-first default never calls a third party you didn't enable.

### Verified
139/139 tests pass; smoke-launch clean.

## [3.5.0] — 2026-08-15

### Mobile layout (toward a Uniswap-style phone UI)
- **Floating pill bottom nav** with five fixed slots (Portfolio · Receive · Send · Market · More) —
  no more horizontal scrolling. Overflow sections open in a **"More" bottom sheet**.
- The 330px side rail is **hidden on the phone layout**, so the content is a clean single column
  instead of a cramped, crooked split.

### UI fixes (from screenshots)
- **the fear logo** now sits beside the umbrella on the welcome screen (replacing the sticker).
- **Quick-action tiles no longer clip** long labels — icon + a bounded, ellipsised text column.
- **Activity events are separate rounded cards** with gaps, not rows crammed into one field.
- **Toasts are shorter** (≈2.2s notices / 3.5s errors), still top-centre.

### Security
- **Hide balances by default** (Settings → Security): every unlock starts with amounts hidden.

### P2P & DEX
- Added **SushiSwap** and **Raydium** (Solana) — both non-custodial. (We deliberately don't list
  sites we can't verify as safe/non-custodial.)

### Coin support (audit)
- Sending is wired for BTC, ETH (+BNB/Polygon/Avalanche/Fantom/Cronos & L2s), LTC, SOL, XMR, TRON,
  USDT-TRC20 and ADA, plus ERC-20/TRC-20 tokens; TON sending for imported TON wallets. DOGE and a
  few others are receive/balance-only for now.

### Verified
139/139 tests pass; smoke-launches clean in desktop and mobile layouts.

## [3.4.2] — 2026-08-15

### Desktop
- **Real phone-style mobile layout.** Mobile mode now shows a proper bottom **icon tab bar**
  (icons + labels, horizontally scrollable so every section stays reachable) instead of a narrowed
  desktop menu, and the quick actions wrap to 2×2. A soft themed **glow** now sits behind the
  the-fear logo on the welcome screen.
- **Clearer backup.** The recovery phrase and the (optional) Monero keys are now one card with a
  plain-language intro explaining that the 24-word phrase is the real backup and the Monero keys are
  an advanced extra most people never need.
- **Fixed the Activity spam.** The feed no longer records a "Sync · Public RPC · OK" row every 60
  seconds; the live status line already shows the last-updated time.

### Security & privacy
- **Lock on minimize** (Settings → Privacy): the vault locks the instant the window is minimized.
- Builds on 3.3's custom SOCKS5 proxy, IPv4/IPv6 control and clipboard auto-clear.

### P2P & DEX
- **More vetted non-custodial venues:** CoW Swap (MEV-protected), Matcha, Curve, Osmosis, plus
  Haveno (Monero P2P), Vexl (no-KYC BTC P2P) and LocalCoinSwap.

### Localization
- The welcome screen (buttons, trust cards, blurb) and several section headers now follow your
  language in all six locales. (Translation coverage continues to expand each release.)

### Verified
139/139 tests pass; app smoke-launches clean in both desktop and mobile layouts.

## [3.4.0] — 2026-08-14

### Desktop
- **Mobile layout on your PC.** Settings → Appearance → *Mobile layout*: the wallet renders as a
  phone-style app — a narrow centred column, a bottom tab bar, and a phone-sized window (430×900) —
  and switches straight back to the wide desktop layout when you turn it off. Your saved menu
  position is preserved across the switch (mobile mode force-docks the nav to the bottom without
  overwriting your preference).
- **Responsive quick actions.** The dashboard's action tiles now wrap to a 2×2 grid in the mobile
  layout instead of clipping four across, and "Prices & charts" is now localized in all six languages.

### Verified
139/139 tests pass; app smoke-launches clean. Anonymity/security model unchanged; updates preserve data.

## [3.3.0] — 2026-08-14

### Security & privacy
- **Security review + vault hardening.** A full pass over the crypto/storage layer. `UnlockAsync`
  now rejects out-of-range Argon2 parameters (memory 8 MiB–1 GiB, t 1–64, p 1–16) up front, so a
  tampered or foreign vault file can no longer stall/OOM the app on unlock. Re-verified: on-device
  Argon2id + AES-256-GCM vault, sign-then-`ZeroMemory` key handling, `SetWindowDisplayAffinity`
  screenshot/screen-share blackout while a seed or Monero key is on screen, and the `https://`-only
  external-link guard.
- **Custom SOCKS5 proxy.** Settings → Privacy routes every request through your own proxy (VPN,
  SSH tunnel, another Tor) instead of the bundled Tor. `host:port` or `socks5://host:port`; Tor and
  the custom proxy are mutually exclusive.
- **IPv4 / IPv6 control.** Force outbound (direct) connections onto one family, or leave it automatic.
- **Clipboard auto-clear.** Copied addresses are wiped from the clipboard after a chosen delay
  (off / 30s / 45s / 1m / 2m), and only if the clipboard still holds what the app put there.

### Localization
- Onboarding (create / import / unlock / restore / back-up), section titles and the holdings/market
  column headers are now localized across all six languages instead of hardcoded English.

### Installer
- Setup shows the licence, carries publisher/version metadata and support/update URLs, and on
  uninstall tells the user where their encrypted data is kept (and that it's deliberately preserved).

### Verified
139/139 tests pass (added a KDF-parameter-rejection test). Anonymity/security model unchanged;
updates still preserve all wallet data.

## [3.2.0] — 2026-08-09

### Desktop
- **Lock-screen background is yours.** Settings → Appearance → Lock screen: pick your own image for the
  unlock screen, revert to the bundled default, or turn it off entirely for a flat, plain lock screen.
- **Market opens instantly.** Prices are cached on this device and shown the moment the Market opens,
  instead of populating dash-by-dash over a few seconds; the live fetch then updates them in the
  background. (Same device-only cache approach as the portfolio totals.)

### Verified
138/138 tests pass. Anonymity/security model unchanged; updates still preserve all wallet data.

## [3.1.0] — 2026-08-09

### Desktop
- **Developer fee → your Solana address.** The 0.5% send fee (disclosed in the review, capped at 2%)
  now routes to `ABX24FdKZb6nyW6eiQ3bE5TdZUPdypG9P23AZeutRXL5` on SOL. Recipient addresses for
  TRON/USDT, Ethereum and TON are stored, but their send paths don't route a fee **yet** — no fee is
  taken on those chains until per-chain routing ships (a fund-critical change, coming next).
- **Fresh installs speak your language.** After a delete + re-download (or first run), the app now
  defaults to your **OS language** if it's translated (UK/RU/ZH/ES/DE), instead of always English.
- **the fear logo — with its blue background** again (as requested).
- **+13 display currencies** — CAD, AUD, CHF, BRL, KRW, MXN, ZAR, SEK, NOK, AED, SGD, HKD, KZT
  (23 total), all live via open.er-api.com.

### Verified
138/138 tests pass (incl. the updated developer-fee vector). Anonymity/security unchanged.

## [3.0.4] — 2026-08-09

### Desktop
- **Update-safety hardened.** The installer now explicitly excludes any `data` folder, so a new version
  can never overwrite your wallets, theme, linked addresses or history. (They already lived in a
  separate data folder — `%APPDATA%\UmbrellaWallet` for installs, `data/` beside the exe for portable —
  untouched by updates; this is a belt-and-braces guarantee.)
- **Brand logos finalized** from the supplied artwork in `docs/assets` (the fear mark + Telegram-channel
  icon), blue background removed.
- README refreshed: new logo, corrected theme count (27), ambient-motion options, and an explicit
  "updates keep your data" note.

## [3.0.3] — 2026-08-09

### Desktop
- **Much faster balances.** Account balances are now fetched **concurrently** instead of one after
  another, and the portfolio total shows right after the (fast) native pass — no more long wait before
  the sum appears on unlock or wallet-switch.
- **Instant totals (no $0 flash).** Balances are cached per wallet on this device, so switching or
  unlocking shows your last-known total immediately while the live refresh updates it in the background.
- **Centered notifications.** Errors and important notices now pop as a **toast at the top-center**
  (auto-hides), instead of only landing in the status bar.
- **Real brand logos.** The **the fear** mark and the **Telegram channel** icon now use the supplied
  artwork with the blue background removed (transparent).

### Verified
Anonymity/security unchanged (no accounts, no telemetry, keys local + encrypted, Tor optional). 138/138
tests pass.

## [3.0.2] — 2026-08-09

### Desktop
- **Settings fully translated.** The **Wallets** tab and the **Maintenance / Danger zone** cards were
  hardcoded English regardless of the chosen language — now every label, hint and button is localized
  (Ukrainian + others).
- **Clearer, more honest wording.** "Delete vault" → **"Erase from this PC"**, and the danger-zone text
  now spells out that this does **not** delete your on-chain wallet (a wallet lives on its recovery
  phrase and can't be deleted) — it only wipes this device's local copy, which your phrase restores.
- **Wallet switching from anywhere.** When the nav sits at the top or bottom, a **wallet switcher chip**
  (shows the active wallet, one tap to switch) now lives in the bar — no more digging into Settings.
- **Distinct theme names** — no two themes share a name/colour label anymore (27 themes).
- **New "Aurora glow" ambient animation** (opt-in) — two soft, slowly drifting glows behind the content,
  toggled independently of the rain and stickers in Settings → Appearance.

### Verified
Anonymity/security model unchanged: no accounts, no telemetry, keys encrypted locally (Argon2id +
AES-256-GCM), Tor optional, app password held only in memory. 138/138 tests pass.

## [3.0.1] — 2026-08-09

### Project
- **Desktop-only from here.** The web frontend and NestJS backend were removed from the repository —
  development is focused entirely on the native desktop apps (Windows & Linux now, **Android planned**).
  Your wallet was always self-custody and local-first, so nothing about it changes.
- **Official Telegram channel.** Added an in-app News card and link to **t.me/UmbrellaWallet** — the one
  official place for news, releases and contact. A News notice explains the web pause.
- README, docs index and CI updated for the desktop-only layout; added a GitHub Actions release workflow
  that builds the Windows installer/portable and the Linux tarball on their native runners.

## [3.0.0] — 2026-08-09

### Desktop
- **Import your Telegram / TON wallet.** Umbrella now understands the TON-native 24-word standard used
  by **Telegram Wallet, Tonkeeper and TON Space** — paste that phrase and it imports as a Toncoin wallet,
  showing the exact same TON address (and balance) those wallets display. The whole derivation
  (mnemonic → ed25519 seed → wallet v4R2 address) is pinned byte-for-byte against `@ton/crypto` +
  `@ton/ton`. You can receive and send TON from it.
- **Full Ukrainian (and RU/ZH) navigation.** Every menu item is now translated — Buy, Swap, P2P & DEX,
  NFTs, Staking and Transactions no longer sit in English next to the translated ones.
- **Individual animation toggles.** Turn the ambient rain and the animated stickers on or off
  independently in Settings → Appearance (under the master motion switch).
- **Six more themes** — Solana, Ethereum, Monero, Kraken, Nord and Dracula — bringing the total to 27.

### Verified
138/138 tests pass, including the TON reference vector, all BIP39/TON validation, every chain's address
derivation and signing, developer fees, and the multi-wallet / password flows.

## [2.8.8] — 2026-08-08

### Desktop
- **Fixed: couldn't create or import *any* wallet.** "Add wallet" locked the current wallet (wiping the
  app password) and then the create/import screen cleared the pre-filled password — leaving the password
  fields hidden with a "enter a vault password" error and no way forward, which blocked creating or
  importing anything. The app password is now retained across the add-lock and the create/import flow
  reuses it directly, so it can't be cleared out from under you. If the fields ever have no password to
  reuse, they simply show again.
- **Self-healing on startup.** If an add-wallet was interrupted before its seed was written, the app now
  switches to a wallet that actually has a vault (so you're never stranded on onboarding) and clears the
  leftover empty wallet.
- **Change password (Settings → Wallets).** Set a new app password; every wallet on your current password
  is re-encrypted so one password keeps unlocking them all.
- **Forgot your password?** The unlock screen now has a recovery path: enter the wallet's recovery phrase
  and a new password to restore access — the same phrase restores the same addresses and funds.
- Note: a truly password-less vault isn't offered, because it would leave your seed effectively
  unencrypted on disk. Use a simple password you write down, plus the new recovery path, instead.

## [2.8.7] — 2026-08-08

### Desktop
- **Import is far more forgiving, and tells you what's wrong.** A phrase from any BIP39 wallet — Kraken
  Wallet, MetaMask, Trust, Ledger, Exodus, Coinbase Wallet, etc. — now imports even if it was pasted
  with numbering ("1. word 2. word"), commas or line breaks (the words are extracted cleanly). If a
  single word is mistyped, the error names that exact word instead of a blanket "invalid". A short note
  on the import screen lists which wallets are compatible.
- **Clearer non-BIP39 message.** A valid-looking phrase that fails the checksum now explains it's likely
  from a non-BIP39 wallet (Telegram Wallet / Tonkeeper on TON), which can't be imported here — your
  funds stay safe in that wallet. (Native TON-wallet import is still planned separately.)

## [2.8.6] — 2026-08-08

### Desktop
- **Rain animation, properly fixed.** The previous pass still pulsed: it used a shared start-delay plus
  repeating durations, so the drops re-synchronised on a cycle and showed a recurring "wave" the longer
  it ran. Every drop now has a unique, non-repeating fall duration and a unique start height with no
  delay, so they scatter immediately and never line back up — steady, subtle rain at any runtime.

## [2.8.5] — 2026-08-08

### Desktop
- **One login password for every wallet.** Adding a wallet no longer asks for a separate password —
  it reuses your app password, and switching between wallets unlocks instantly instead of re-prompting.
  (If a wallet happens to use a different password, it still asks.) The password is held only in memory
  while unlocked and wiped on lock, same as the seed.
- **Clearer "invalid phrase" for TON/Telegram wallets.** Importing a recovery phrase from Telegram
  Wallet / Tonkeeper (TON) now explains *why* it's rejected: those wallets use the same wordlist but the
  TON mnemonic standard, which isn't BIP39 — so it can't be imported here, and your funds stay safe in
  that wallet. (Full TON-wallet import is planned as a separate, test-pinned feature.)
- **Fixed the rain animation.** The ambient streaks used to fall in three synchronised rows (they all
  shared one animation phase). They now have varied speeds and per-drop delays, so it reads as real rain,
  and travels the full height of a maximised window. Kept deliberately subtle.
- **Settings search.** A search box at the top of Settings finds any option by name or keyword and jumps
  straight to its pane.

## [2.8.4] — 2026-08-08

### Desktop
- **Multiple wallets (Binance-style).** Keep several independent wallets on this device and switch
  between them from **Settings → Wallets** (or the new **⇄ Wallets** button in the sidebar). Each wallet
  is its own encrypted seed with its own password; switching locks the current one and asks for the
  other's password. Add a new wallet (create or import), rename the active one, or remove another —
  the active wallet and the original "Main" seed file are protected from deletion.
  - Safety: the pre-existing wallet is always preserved and auto-registered as "Main"; a corrupt index
    can never lock you out (it falls back to the Main vault); a full data-wipe now also clears the
    wallet index, every additional vault and the activity log.
  - This ships **Stage 1** (independent wallets). Sub-accounts *within* one seed (one phrase, Account
    1/2/3 like MetaMask) are the next stage.

## [2.8.3] — 2026-08-08

### Desktop
- **Fixed: sidebar overflow.** On a shorter window the nav list ran into the footer, so *Settings*
  overlapped "Keys encrypted on this PC" and the Lock button. The nav list now scrolls inside its own
  area, and the status footer + Lock vault button stay pinned at the bottom at any window height.
- **New Buy section (fiat on-ramps).** Top up with a card or bank transfer via regulated on-ramps that
  deliver straight to your own address — **Onramper** (aggregator), MoonPay, Ramp, Transak, Banxa,
  Mercuryo and Guardarian. A 3-step "how it works" and one-tap copy of your receive address; Umbrella
  holds nothing and takes no fee.
- **NFTs — clearer provenance.** Added a "Where these come from" note (read-only from the Ethereum
  chain via a public explorer against your own 0x address; more chains on the roadmap).

## [2.8.1] — 2026-08-08

### Desktop
- **Responsive chart — nothing gets clipped.** The Market detail chart now scales to fit whatever
  width the window gives it (wrapped in a Viewbox), so on a narrow or restored-down window it shrinks
  to fit instead of having its price axis and right edge cut off, and it grows cleanly on a maximised
  one. The hover crosshair stays pixel-accurate at any scale.

## [2.8.0] — 2026-08-08

### Desktop
- **Pro-grade charts.** The Market detail chart now has an interactive **hover crosshair** with a
  floating price + time readout, a **Line ⇄ Candles** toggle, a soft **gradient area fill** under the
  line (fading in the up/down colour, Kraken/TradingView-style) and a **change-over-window badge**
  (first→last, e.g. `▲ 4.21% · 7D`).
- **New P2P & DEX section.** A curated directory of **non-custodial** ways to trade — the in-wallet
  THORChain swap up top, then on-chain DEXes (Uniswap, THORSwap, Jupiter, 1inch, PancakeSwap) and
  peer-to-peer escrow venues (Bisq, Hodl Hodl, RoboSats, Peach). Each shows its custody model and opens
  in your own browser; no custodial exchanges are listed, and Umbrella takes no fee and holds nothing.
- **Market search.** A live filter box matches by ticker or name, plus a one-tap refresh — the coin
  list stays fully live while you type.

## [2.7.0] — 2026-08-01

### Desktop
- **Watch-only linking now auto-detects the network** from the address you paste (a `T…` address is
  TRON, `bc1…` is Bitcoin, `0x…` is EVM, and so on), so a linked address is always tracked on the
  right chain instead of whatever the dropdown happened to show. (Connect tracks an *external* address
  read-only; it never changes your own receive addresses.)
- **Activity & transaction history now persist** across restarts (stored on this device only, never a
  server) with real timestamps — they no longer vanish when you close the wallet, and the Transactions
  section keeps your sends/swaps.
- **Danger zone expanded** beyond delete-wallet: *Clear history* (wipe the local activity/transaction
  log) and *Disconnect all* (remove every linked watch address and exchange) — both keep the vault and
  funds intact.
- **Eight branded themes**, each with real brand colours: Uniswap (exact `#FF007A` pink), Binance
  (gold), Bybit (amber), OKX (mono black/white), Telegram (blue), TON · Gram (blue), TRON (red),
  WhiteBit (green) and Bitcoin (orange) — 21 themes total.
- LICENSE and README updated: independent/experimental self-custody framing, a trademark &
  non-affiliation clause (the branded themes and coin names imply no endorsement), and a
  not-a-regulated-service / no-advice clause. Author: **the fear**.

## [2.6.0] — 2026-08-01

### Desktop
- **Display currency.** Settings → Appearance lets you show balances and prices in USD, EUR, UAH, RUB,
  GBP, CNY, JPY, PLN, TRY or INR — the total, holdings, breakdown and market all convert (USD→currency
  rate via a keyless API, routed through Tor when on). Coins are unchanged; only how their value reads.
- **Transactions section + Activity filters.** A dedicated Transactions view lists money movements
  (sends, receives, swaps) with a copy-explorer-link on each; the Activity feed gains a filter
  (All / Transactions / Connections / Settings / System).
- **Market fixed + fuller.** Prices now come from Binance first (CoinGecko's free tier was rate-limiting
  and blanking every row), with CoinGecko filling only what Binance lacks — so BNB/MATIC/AVAX/FTM/LINK/
  UNI/XRP/DOT/BCH/USDC all price and chart. Market-only coins (XRP/DOT/BCH) now say so honestly instead
  of claiming a wallet address.
- **Two more themes** — Uniswap (magenta-pink) and Ocean (teal) — bringing the palette to 13.

## [2.5.0] — 2026-08-01

### Coins — sending on every EVM chain
- **Send native BNB, MATIC, AVAX, FTM and CRO**, not just Ethereum. The wallet already showed these
  balances (same 0x address); now it signs and broadcasts their transfers too, reusing the exact
  EIP-155 signer that is pinned byte-for-byte to the official test vector — only the chain id, RPC and
  explorer differ. Nonce, gas price and the balance check come from each chain's public RPCs (with
  fallbacks), and everything routes through the bundled Tor when it is on. So a MetaMask-imported
  wallet can now spend across Ethereum, BSC, Polygon, Avalanche, Fantom and Cronos from one place.
- The full 24h portfolio change now shows in the overview ring; Recent Activity, Market (12 more
  popular coins), News and the Guide were all filled in.

## [2.4.0] — 2026-08-01

### Desktop
- **All tokens now show, not just USDT.** Every TRC-20 token on TRON and every ERC-20 token on
  Ethereum (via Koios-style public APIs / Blockscout, no keys) appears in Holdings — reward tokens,
  other stablecoins, any token — which is what most "my balance is missing" reports actually were.
  Unpriced tokens show their real amount at $0 rather than an invented price.
- **Many more coins.** Every major EVM network at the same 0x address as Ethereum — native BNB (BSC),
  MATIC (Polygon), AVAX (Avalanche), FTM (Fantom), CRO (Cronos), plus ETH on the Arbitrum, Optimism
  and Base L2s — queried in parallel. Combined with the automatic ERC-20 / TRC-20 token display, a
  MetaMask-imported wallet now shows essentially everything it holds.
- **Portfolio-overview ring breakdown.** The right-rail ring now shows what the balance is made of:
  a proportional bar plus a per-asset legend (symbol · share · value), top assets with the rest
  folded into "Other".
- **Hide-balance now hides everything.** Masking the balance blanks every money figure — the overview
  ring, the breakdown values and each Holdings row's amount + value — not just the top total (the
  public market price stays visible).
- **NFTs** — ERC-721 / ERC-1155 collections at your Ethereum address are listed (names + counts;
  no images are fetched, so it never leaks your IP).
- **Staking** — the stakeable coins you hold keys for, with each network's typical (approximate)
  reward and how staking is done.
- The Windows app now ships as **`Umbrella.exe`**.

### Coins — Cardano (ADA) sending
- **Real ADA sending.** Cardano payment transactions are now built, signed and broadcast on-device:
  the CBOR transaction body, the BIP32-Ed25519 signature (extended-key ed25519, implemented from the
  group operations) and the assembled signed transaction are all pinned **byte-for-byte** against
  Emurgo's cardano-serialization-lib for a fixed key and transaction, so a single wrong byte fails the
  tests before any ADA can move. UTXOs, the chain tip (TTL) and submission go through Koios; fee and
  change are computed from the real signed-tx size, change returns to the sender. ADA is now fully
  supported (receive + balance + send) — Monero alone remains receive-only.
- Fixed a latent guard that blocked confirming TRON / USDT (TRC-20) sends.

## [2.3.0] — 2026-07-31

### Desktop
- **In-wallet swaps (THORChain).** New Swap section for decentralised, non-custodial cross-chain
  swaps — no account, no API key, no KYC. The source coin is sent to a THORChain inbound vault with a
  signed OP_RETURN memo, and the network delivers the target coin to the wallet's own receive address;
  funds are never held by a third party. Pay from BTC/LTC, receive BTC/ETH/LTC/DOGE. Live quote (rate,
  fee, slippage, ETA, expiry) reviewed before sending, with a fresh re-quote and a rate-moved guard at
  confirm time. Quote parser pinned to real THORChain responses; the OP_RETURN memo path is tested.

## [2.2.3] — 2026-07-31

### Security (desktop — fund-critical)
- **TON address checksum now verified.** `ParseFriendlyAddress` decoded the 36-byte address but
  ignored its trailing CRC-16 — a mistyped recipient that still decoded with a valid tag would have
  been accepted and funds sent to the wrong account. The checksum is now enforced (mistyped/corrupted
  addresses are rejected before a send). Regression-tested.
- **BoC parser hardened.** `TonCell.FromBoc` now bounds-checks every read and validates all counts and
  ref/root indices, so malformed or truncated cell bytes fail with a clean error instead of an
  IndexOutOfRange / overflow / out-of-memory crash. Covered by a new deterministic fuzz harness
  (120k random + mutated inputs across the address, BoC and base58 parsers).

### Security (tooling — whole repo)
- Fixed a high-severity web dependency advisory (postcss path-traversal, GHSA-r28c-9q8g-f849) and
  pinned patched versions of two vulnerable test-only .NET transitives.
- Added CI security gates: CodeQL SAST (C# + JS/TS), Dependabot (npm + NuGet + Actions), secret
  scanning (gitleaks), PR dependency review, and enforced npm-audit / NuGet-vulnerable gates.

## [2.2.2] — 2026-07-31

### Desktop
- Full wallet + data deletion (Danger zone); Tor/Monero bundles preserved.
- Settings text overflow fix; localized delete confirmation keyword.

## [2.2.1] — 2026-07-31

### Desktop
- Delete-vault keyword localized; settings layout overflow fixed.
- README header refresh.

## [2.2.0] — 2026-07-31

A large desktop pass over 2.1.x, consolidated.

### Desktop
- **Real TON sending** — wallet v4R2 transfers, pinned byte-for-byte against `@ton/ton`.
- **Live candlestick market** — real OHLC candles from Binance klines, timeframes 1H–1Y; prices
  and 24h change from CoinGecko. No mock data.
- **Editorial monochrome redesign** — cinematic hero, right-hand dashboard rail (overview ring,
  activity, market), floating price-ticker dock, coin-symbol badges, glassy hover motion + parallax.
- **Profile customization** — name, avatar, banner, and sidebar / lock-screen backgrounds, all
  picked from your own image files (fixed: file dialogs were never wired, so uploads and backup were
  silently dead).
- **NFTs / Staking** sections (in-development pages); configurable auto-lock; **11 themes** whose
  accent now drives the buttons; a localized guide (full Ukrainian) and a detailed
  Cybersecurity / Danger-zone settings pass.
- **Linux** build from the same codebase.

### Coins
- Send: BTC, ETH, LTC, SOL, **TON**, TRON, USDT (TRC-20), XMR. Receive + balance: ADA (send next).

## [2.1.4] — 2026-07-30

### Desktop
- **Themes restyle the buttons**, not just the colours — the primary CTA takes each theme's accent
  with an auto-contrast label, and quick-action tiles glow in the accent on hover.
- **Linux build** — shipped from the same codebase as Windows (identical features, themes and
  bundled Tor/Monero), as a `linux-x64` tarball.

### Notes
- The mobile experience is the responsive web app / Telegram mini-app; there is no separate native
  Android build in this repo.

## [2.1.3] — 2026-07-30

### Desktop
- **Chrome removed** — the top title-bar panel and the bottom status bar are gone; the window is
  all content. It still drags, and the native min/max/close remain. Status/errors show on the
  unlock form and the send review step.
- **Themes re-skinned** — every non-primary palette now matches the app's mood (Blue → electric-cyan
  glass, Green → neon mint, Gradient → vivid violet, Slate → teal cyber), keeping the red editorial
  primary and the animations untouched.

## [2.1.2] — 2026-07-30

### Desktop
- **Editorial red-orange "the fear" theme** — the primary look is now a neutral-charcoal noir
  with a single hot red-orange accent, matching the reference posters (was violet).
- **Coin-symbol badges** — token badges show each coin's own currency mark (₿ Ξ Ł Ð ₳ ₮ ◎ ◈ …)
  instead of the 3-letter ticker, in a symbol-capable font (no external icon set).
- **Localized guide** — the in-app documentation now follows the wallet's language; fully
  translated to Ukrainian, English as the base/fallback.

### Web
- Coin-symbol badges; brand accent moved from violet to the same red-orange for parity.

## [2.1.1] — 2026-07-30

### Desktop
- **Configurable auto-lock** — Settings → Security lets you set the idle time (1/5/15/30/60 min)
  or turn auto-lock off entirely, instead of a fixed 5-minute delay.
- **Wallet name** — name this wallet in Settings → Appearance; it shows in the top bar (a light
  wallet "profile"), stored only on-device.
- **Tidier top bar** — dropped the "local vault" label; editorial mono wallet name + live lock
  status.
- **Ember theme** — a red editorial look matching the reference posters.
- **News** — 2.1 notes added; **Guide** — new "Personalise and auto-lock" section.

## [2.1.0] — 2026-07-30

### Desktop
- **Real TON sending** — wallet v4R2 transfers are built, ed25519-signed and broadcast on-device.
  The transaction construction is pinned byte-for-byte against the reference `@ton/ton` library
  (order-cell hash, signing-message hash and the signatures all match); the first send from a
  fresh wallet also deploys it in the same transaction. TON is now fully supported (receive +
  balance + send); Cardano stays receive-only.
- **Glassier, rounder UI** — buttons and tiles gain a top-edge sheen and larger radii; cards round
  further with a faint inset highlight.
- **Ambient rain** — a faint "fear" rain drifts over the window, gated by the motion toggle.

### Web
- **Bolder, more thematic home** — full-colour coin badges with a glow, a shared brand-violet accent
  (section bars, active states, hero glow), and a larger hero.
- **Glassy, rounder UI with ambient rain** to match the desktop.

## [2.0.0] — 2026-07-28

### Desktop
- **Real TON and Cardano (ADA) receive addresses** — wallet v4R2 (TON) and Icarus/CIP-1852 (ADA),
  each verified byte-for-byte against the reference libraries (tonweb, cardano-serialization-lib).
- **Live TON + ADA balances** (toncenter / Koios).
- **Developer fee baked in** (0.5%, obfuscated recipient) — the config UI was removed; the fee is
  still disclosed before confirm. Routed on-chain for BTC/LTC/XMR/SOL.
- **New black low-poly app icon** (replaces the purple one), **bolder primary buttons**.
- **Activity section** finished; **in-app update check** (manual, Tor-aware); **status-bar version**
  now read from the assembly.

### Web
- **Self-hosted fonts** — no Google origins at all (fully anonymous).
- Removed the `/admin` route; fee percentage is baked.

### Contracts
- `FeeSplitter.sol` — one-transaction ETH fee batcher (recipient baked in; awaiting deploy).

## [1.8.0] — 2026-07-28

### Desktop
- TON (wallet v4R2) and Cardano (ADA) receive addresses
- Activity section completed
- In-app update check (manual, Tor-aware)
- Real app version in status bar
- Windows portable build

### Web
- News section
- Self-hosted fonts (no Google CDN)
- Exchange rate improvements

### Contracts
- FeeSplitter utility for efficient ETH forwarding
## [1.7.0] — 2026-07-23

### Desktop
- Monero and Tor startup reliability
- Encrypted backup export
- Panel placement fixes
- Four additional colour themes

### Web & API
- Market rates aggregator (CoinGecko + Binance fallback)
- Tor / privacy mode with IP redaction on backend
- Security hardening (Helmet, rate limiting, log scrubbing)
- Monero view-only derivation, multi-chain balances via public RPCs

## [1.6.0] — 2026-07-22

### Desktop
- Exchange-style candle charts
- Vector tile icons for coin list
- QR receive scrim fix (no white flash)

## [1.5.1] — 2026-07-22

### Desktop
- Fix: the fear brand mark no longer carries a white background

## [1.5.0] — 2026-07-22

### Desktop
- Six colour themes
- Searchable language picker
- Real market charts
- Nine exchange connectors (read-only API keys)

## [1.4.0] — 2026-07-21

### Desktop
- Exchange account linking (Binance, Bybit, OKX, Kraken, KuCoin, Gate.io, MEXC, Bitget, Telegram CryptoBot)
- Improved buttons and dropdown controls

## [1.3.0] — 2026-07-21

### Desktop
- Linked wallets counter on portfolio
- QR code popup for receive
- Six interface languages

## [1.2.0] — 2026-07-20

### Desktop
- USDT (TRC-20) and TRX sending
- Wallet data stored off the system drive (user-chosen path)
- Brand logo restored in title bar

## [1.1.0] — 2026-07-20

### Desktop
- Monero as a full coin (receive, send, balance via local `monero-wallet-rpc`)
- Telegram sticker animations in onboarding

## [1.0.2] — 2026-07-19

### Desktop
- Fix clipping on small window sizes
- Settings panes layout
- Glass-style UI polish

## [1.0.1] — 2026-07-19

### Desktop
- the fear branding
- Network labels on receive addresses
- Chain pickers and layout fixes

## [1.0.0] — 2026-07-18

### Desktop (initial release)
- Native Avalonia wallet for Windows
- BIP39 24-word seed generation and import
- Argon2id + AES-256-GCM encrypted local vault
- BTC, ETH, SOL, TRX, LTC, DOGE receive addresses
- Built-in Tor routing
- Real ETH and BTC send
- Watch-only address tracking
- Live market prices from CoinGecko

### Web (companion)
- React + NestJS stack
- Non-custodial browser wallet shell
- P2P marketplace, exchange view, portfolio stats

---






