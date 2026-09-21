# Verify your wallet

Every number this wallet shows you comes from the same program that tells you your money is safe.
That is not a reason to distrust it. It is the reason this page exists: each check below asks
somebody who is **not** Umbrella the same question, so the answer stops depending on us being honest.

None of it requires trusting this document either — every command is one you run yourself, and every
result is something you can see.

| You want to know | Check |
|---|---|
| Is the balance real? | [1. Your balance, from somebody else](#1-your-balance-from-somebody-else) |
| Is Tor actually carrying my traffic? | [2. Where the traffic goes](#2-where-the-traffic-goes) |
| Did I download the real build? | [3. The download](#3-the-download) |
| Does the build match this source? | [4. The build](#4-the-build) |
| What leaves my machine at all? | [5. Who this wallet talks to](#5-who-this-wallet-talks-to) |

---

## 1. Your balance, from somebody else

**Settings → Security → "Check this wallet against something else"** exports a **watch-only key**
(an account `xpub`) for each UTXO chain. Paste it into a block explorer you chose, and it will derive
the same addresses and report the same balance — or it will not, and that answer is worth more than
anything we could tell you.

What the key is: the account-level extended **public** key, at the path shown beside it
(`m/84'/0'/0'` for Bitcoin, `m/44'/145'/0'` for Bitcoin Cash, and so on). It is exactly the level
this wallet's own addresses hang off, which is why a scanner sees the same set — including the
internal change addresses, where money returns after a send.

**Bitcoin has two.** The wallet also finds and spends coins on the Taproot account (`m/86'/0'/0'`)
that a restored seed may have used, and its Bitcoin balance includes them. So the screen exports a
second key, **BTC · Taproot**. Check both — in the scanner, choose the Taproot (P2TR / `tr()`)
script type for that one — and add the two together. A scanner given only the SegWit key will report
less than the wallet shows, and that difference is not a discrepancy.

**What it costs.** A watch-only key cannot spend a single coin. It *does* reveal every address on
that account, past and future, to whoever you give it to: how much you hold, when, and who you paid.
Use a scanner you trust, over Tor, and do not leave the key lying around. This is the one check with
a real privacy price, and it is your call whether to pay it.

Scanners that take an xpub without an account: [mempool.space](https://mempool.space) (Bitcoin),
[blockchair.com](https://blockchair.com), or your own Electrum server if you run one.

**A payment, before it is signed.** On the Bitcoin review screen, **Export as PSBT** gives the
exact unsigned transaction. Open it in another wallet (Sparrow, Electrum) and read it there: the
same recipient, the same amount, change back to an address of yours, the same fee. If a second
program you chose agrees with this one about what is about to happen, you are no longer taking this
wallet's word for it. The file names this wallet's key fingerprint and the paths of the coins it
spends; it cannot move anything.

---

## 2. Where the traffic goes

The sidebar chip always states the live route: **TOR**, **PROXY**, **DIRECT** or **BLOCKED**. It reads
the same state the send gate reads, so a send cannot be refused for a route the chip calls fine.

To check it from outside the wallet:

```bash
curl --socks5-hostname 127.0.0.1:9250 https://check.torproject.org/api/ip
```

Port **9250** is the bundled Tor deliberately — not 9050 or 9150 — so it never collides with a Tor
Browser you are already running. A response with `"IsTor":true` means that SOCKS port really is Tor.

Then check the wallet itself is using it. While the wallet is running, with **Tor-only** armed in
Settings → Privacy, watch its connections:

```powershell
Get-NetTCPConnection -State Established |
  Where-Object OwningProcess -eq (Get-Process Umbrella).Id |
  Select-Object RemoteAddress, RemotePort
```

```bash
# Linux
ss -tnp | grep Umbrella
```

With Tor-only on, every remote endpoint should be loopback (`127.0.0.1:9250`). Anything else going
out is a bug worth reporting — [SECURITY.md](../SECURITY.md).

The wallet proves the same property to itself in CI: a test opens a listener on loopback and asserts
that with the kill-switch armed **no socket is opened at all** — not that the request failed, that it
never happened. With the kill-switch off the same client must reach that listener, so the first half
cannot pass for the wrong reason.

---

## 3. The download

Every release carries `SHA256SUMS-<version>.txt`. Verify what you downloaded before you run it:

```powershell
Get-FileHash -Algorithm SHA256 .\UmbrellaWallet-Setup-4.8.1.exe
```

```bash
sha256sum -c SHA256SUMS-4.8.1.txt
```

The release workflow generates that manifest from the artifacts it attached and verifies every line
before publishing. You can re-check what the page serves **now**, which is the part a workflow cannot
promise:

```bash
bash scripts/verify-published-release.sh v4.8.1
```

A checksum proves the file matches what the release page says. It says nothing about **who put that
page there** — somebody who can replace the artifacts can replace the manifest beside them. So every
release from the next one on also carries a build attestation:

```bash
gh attestation verify UmbrellaWallet-Setup-<version>.exe --repo thefear078/UmbrellaWallet
```

That checks against a public transparency log — not against us — that these exact bytes came out of
this repository's release workflow, at a named commit. There is no signing key involved, which means
there is no signing key for anybody to steal.

**Releases published before that have checksums only.** An attestation cannot be added to a build after the
fact; a page claiming otherwise would be worth nothing.
[AUDIT_STATUS.md](../AUDIT_STATUS.md) keeps that kind of gap in one place.

---

## 4. The build

Reproducible builds are what make a published checksum mean anything: without them you are comparing
a download against a manifest produced by the same release an attacker would have had to compromise.

```bash
bash scripts/verify-reproducible-build.sh
```

It builds this commit twice, from two different absolute paths, and compares the assemblies that
actually run. CI runs it on every pull request. [BUILD_VERIFY.md](BUILD_VERIFY.md) explains what is
and is not byte-identical — the single-file installer is **not**, and the document says why rather
than claiming otherwise.

The third-party binaries this wallet bundles are pinned and checked against their publishers' own
signed sums:

```bash
bash scripts/check-pinned-binaries.sh     # the pins in the scripts match THIRD_PARTY_NOTICES.md
pwsh desktop/scripts/fetch-tor.ps1 -RequireSignature
pwsh desktop/scripts/fetch-monero.ps1 -RequireSignature
```

With `-RequireSignature` the fetch fails closed rather than falling back to the hash alone — which is
what a release build uses. The key fingerprints are in
[THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).

---

## 5. Who this wallet talks to

**Settings → Privacy** lists every server the wallet can contact: who runs it, why it is contacted,
and what it learns. The build fails if a server appears in the code without appearing on that list,
and fails the other way if the list names a host the code no longer uses — a transparency page that
can quietly fall behind the code is worse than none.

You can point every chain somewhere else from the same screen, including at a node you run. If you
choose one, it is used or the operation fails with a reason; it is never silently swapped back for
ours.

---

## What none of this proves

- **That your machine is clean.** Malware with your user account can read what you read. See
  [THREAT_MODEL.md](../THREAT_MODEL.md), Vector 1.
- **That a transparent chain is private.** Bitcoin, Ethereum and the rest are public ledgers; Tor
  hides your IP from the servers you ask, and un-sends nothing.
- **That the code has been audited.** It has not. [AUDIT_STATUS.md](../AUDIT_STATUS.md) says so
  plainly and will keep saying so until it changes.
- **That the source behind a signed build is good.** An attestation ties bytes to a commit in this
  repository. Reading that commit is still your job — which is why the source is here.

Found something that does not check out? [SECURITY.md](../SECURITY.md) — and never send a recovery
phrase to anyone, including us, while "verifying".

---

📖 Back to [Documentation Index](INDEX.md)
