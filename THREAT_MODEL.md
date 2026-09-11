# Threat model

Who this wallet defends against, how, and — the part that matters more — where the defence ends.

A threat model that only lists wins is marketing. Each vector below states the attacker, what the
wallet actually does, and what remains true afterwards. Where the honest answer is "this does not
help", it says so.

**Scope.** Umbrella is a desktop wallet for Windows and Linux, .NET 8 / Avalonia. There is no server,
no account and no backend: nothing about a user exists anywhere except on their own machine.

---

## Trust boundaries

| Trusted | Why |
|---|---|
| The local OS kernel and filesystem | A desktop app runs inside it. If this is compromised, nothing below matters. |
| The CPU's RNG via `RandomNumberGenerator` | Seed entropy comes from the OS CSPRNG, not a language RNG. |
| The bundled Tor binary and `monero-wallet-rpc` | Shipped with the build; verified by release checksums. |

| **Not** trusted | Consequence |
|---|---|
| The network | Everything goes over TLS; Tor is available for every request and enforced by a kill-switch. |
| Every block explorer and RPC node | They are named, listed, and replaceable by the user. |
| Price feeds | Never given an address; treated as a coin-name oracle only. |
| Any exchange the user connects | Opt-in, read-only keys, stored encrypted. |
| The settings file | Re-validated on load, never trusted because it was written earlier. |

---

## Vector 1 — Malware running as the user (infostealer, keylogger, RAT)

**Attacker.** Code already executing with the user's privileges on the same machine.

**What the wallet does.** The vault is Argon2id (m = 64 MiB, t = 4, p = 2) → AES-256-GCM with versioned
associated data. Keys are zeroed after signing. The seed screen sets `WDA_EXCLUDEFROMCAPTURE`, so the
window renders black to screenshots and screen sharing. The clipboard is cleared 45 seconds after an
address is copied, and only if it still holds exactly what the wallet put there. Auto-lock defaults to
five minutes.

**Where the defence ends.** It does not hold. An attacker at this level can read keystrokes, read the
clipboard before the timer, and read process memory while the wallet is unlocked — and the unlocked
mnemonic is a .NET `string`, which cannot be reliably zeroed: strings are immutable, the GC moves
them, and `SecureString` is a no-op outside Windows. **No desktop wallet survives this.** For balances
worth an attacker's effort, a hardware wallet is the answer, and Umbrella does not support one yet.

**Residual risk: HIGH and unmitigated.** Stated here rather than buried.

---

## Vector 2 — Someone at the keyboard of an unlocked or locked machine

**Attacker.** A person with physical access and time — a border official, a flatmate, a thief.

**What the wallet does.** Failed unlocks are counted and persisted: three free, then an exponential
backoff (5 s → 10 s → 20 s → … capped at 15 minutes). Persisted, because a counter that resets when the
window is closed stops nobody. Capped, because a forgotten password must not hold the owner's own
funds hostage.

**Where the defence ends.** Anyone who can edit the settings file resets the counter, and anyone who
can copy `vault.json` attacks it offline where no UI rule applies (see Vector 3). The throttle raises
the cost of guessing at the keyboard and nothing more.

**Residual risk: MEDIUM**, dominated by password strength.

---

## Vector 3 — Offline brute force of a stolen `vault.json`

**Attacker.** Someone who copied the vault file — from a backup, a stolen disk, a synced folder.

**What the wallet does.** Argon2id with 64 MiB of memory per guess, 4 iterations, 2 lanes. Memory-hard
by design: GPU and ASIC parallelism buys far less than it does against PBKDF2 or bcrypt. The envelope
carries its own parameters and is range-checked before being fed to the KDF, so a tampered file cannot
ask for a trivially cheap derivation.

**Where the defence ends.** At the password. A short or reused password falls regardless of the KDF,
and no parameter choice fixes that. The wallet enforces a minimum length and shows a strength meter;
it cannot enforce a good password.

**Residual risk: LOW for a strong passphrase, HIGH for a weak one.**

---

## Vector 4 — The network operator (ISP, café Wi-Fi, hostile country)

**Attacker.** Anyone who can watch or modify traffic between the machine and the internet.

**What the wallet does.** Every request goes over TLS with certificate validation; the validation
callback is never overridden, and a test fails the build if anyone adds one. Tor ships inside the
build as a child process on its own port, separate from any Tor Browser. The **kill-switch is
fail-closed**: with Tor-only on and Tor down, the shared HTTP client refuses connections in its
`ConnectCallback`, before DNS and before a socket. There is no fallback to clearnet — that silent
fallback is a classic wallet vulnerability and this one does not have it.

**Where the defence ends.** Traffic timing and volume are still observable. A `.onion` node removes
the exit node; a clearnet node over Tor does not.

**Residual risk: LOW with Tor on, HIGH with it off** — which the Security Center says out loud rather
than scoring generously.

---

## Vector 5 — The block explorer or RPC node

**Attacker.** Whoever answers "what is the balance of this address".

**What they learn.** The connecting IP (an exit node with Tor on), that it belongs to a wallet,
roughly which blocks it asked for, when it is online, and which connection a transaction entered the
network through. Crucially: **your addresses**, and the ability to tie every address asked about in one
session to one person.

**What the wallet does.** Names every one of them in Settings, with who runs it and what it learns.
Lets the user point any chain — Bitcoin, Litecoin, Bitcoin Cash, Dogecoin, Ethereum, Solana, TON,
Tron, Cardano, Monero — at a different company or at their own node. A server the user chose is never
silently replaced by the default. The catalog cannot fall behind the code: the build fails if a host
appears in the source without appearing in the list.

**Where the defence ends.** Tor hides the IP. **It does not un-send the address.** The only complete
answer is running your own node, which the wallet supports and cannot do for you.

**Residual risk: MEDIUM with a public node, LOW with your own.**

---

## Vector 6 — Chain analysis of your own transactions

**Attacker.** Anyone reading the public ledger afterwards — which is everyone, forever.

**What the wallet does.** A fresh receive address per payment on BTC, LTC, BCH and DOGE, offered
exactly where the wallet also scans and can spend across every address it issued. Coin control on the
UTXO chains, so a spend need not join coins from different parts of a life. Privacy Radar reads the
pending transaction offline and says what it would reveal — chiefly which addresses it links. Change
always goes to a freshly derived internal address.

**Where the defence ends.** A transparent chain publishes the amount and both addresses permanently.
A spend that joins two addresses cannot be un-joined. Whoever you paid knows you paid them. Umbrella
has **no CoinJoin, no PayJoin, no Dandelion++ and no Taproot** — these are real gaps, listed in the
roadmap rather than implied away.

**Residual risk: HIGH on transparent chains. Monero is the answer the wallet actually has.**

---

## Vector 7 — A compromised release binary (supply chain)

**Attacker.** Someone who replaces the download, or compromises the build.

**What the wallet does.** Releases are built in public CI from a tagged commit. A per-version
`SHA256SUMS-<version>.txt` is generated from the attached artifacts, self-verified before publishing,
and refuses to publish if empty. The artifact set is checked for completeness and for strays, so a
partial release cannot yield a manifest that silently omits a file. Dependencies are pinned, scanned
by Dependabot, and the source is analysed by CodeQL on every PR.

**Where the defence ends.** The binaries are **not code-signed** and the build is **not reproducible**.
A user verifying the checksum is verifying against the same GitHub release an attacker would have had
to compromise. Both are on the roadmap and neither is done.

**Residual risk: MEDIUM.** Do not let anyone tell you otherwise until those two ship.

---

## Vector 8 — Telemetry and metadata leakage from the app itself

**Attacker.** The wallet's own developers, present or future.

**What the wallet does.** There is no analytics SDK, no crash reporter, no account, and **no
application log at all** — a log is the easiest place for an address or an amount to end up sitting in
the clear. This is enforced rather than promised: the build fails if any of nineteen analytics or
crash-reporting packages is referenced, if any of their call shapes appears in the source, or if
anything writes a log file.

**Where the defence ends.** The bundled Monero daemon writes its own log, which is its business, and a
future maintainer can always change the rules. The test makes that a deliberate act with a visible
diff rather than an accident.

**Residual risk: LOW.**

---

## Vector 9 — Data left behind after "delete everything"

**Attacker.** Whoever examines the machine afterwards.

**What the wallet does.** The wipe removes the encrypted seed, every additional wallet, settings and
profile images, watch addresses, exchange keys, the address book, private transaction notes, the
activity log, cached balances and prices, and the Monero wallet. A test scans the source for every
path written under the data directory and fails the build if the wiper does not handle it.

The address book is now encrypted at rest under a seed-derived key, and an existing plaintext book is
migrated and the readable copy deleted. **Until 4.7 it was plaintext and survived the wipe** — the list
of who somebody pays, left behind by the act of erasing the wallet.

**Where the defence ends.** Files are deleted, not shredded; on an SSD, recovery of unlinked blocks is
a question for the drive's firmware, not for this program. Full-disk encryption is the correct answer
and is the operating system's job.

**Residual risk: LOW after 4.7, and it was not before.**

---

## What is deliberately out of scope

- **Recovering a lost seed phrase.** There is no path. That is the same property that means nobody can
  freeze the funds either.
- **Protecting against the recipient.** Whoever you pay knows they were paid by you.
- **Making a transparent chain private.** It cannot be done from the wallet side.
- **Defending a rooted or malware-infected machine.** See Vector 1.

---

## Reporting a vulnerability

See [SECURITY.md](SECURITY.md). The rules the code is held to are in [MANIFESTO.md](MANIFESTO.md);
the implementation detail is in [docs/security-model.md](docs/security-model.md).
