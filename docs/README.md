# Umbrella Wallet — documentation

Umbrella is a **desktop-only**, self-custody crypto wallet (.NET 8 + Avalonia; Windows and Linux,
Android planned). There is **no web app and no backend server** — nothing to sign in to and nothing
that knows you exist.

New here? The [project README](../README.md) is the overview. These pages are the detail.

---

## For engineers

Start here if you are building on, forking, or auditing Umbrella.

| Document | What's inside |
|---|---|
| [../MANIFESTO.md](../MANIFESTO.md) | **Read first.** The rules everything below is held to |
| **[architecture.md](architecture.md)** | The three-project layering and why `Core` may never touch the network. Data flow, concurrency rules, the invariants the tests enforce. |
| **[building.md](building.md)** | Build, run, test. Producing installers, the portable build and checksums. The apphost rename that trips people up. |
| **[testing.md](testing.md)** | What the 526 tests actually protect, how to write one that belongs here, and the coverage gaps we know about. |
| **[forking.md](forking.md)** | Licence limits, the rebranding checklist, and the seven things that will hurt your users if you change them. |

## Extending it

| Document | What's inside |
|---|---|
| **[12-coins-and-chains.md](12-coins-and-chains.md)** | Every chain, coin and token: derivation paths, address formats, fees, privacy level and current status. |
| **[adding-a-chain.md](adding-a-chain.md)** | A coin end to end — derivation, validation, balance, history, send — with the gates that stop a new chain losing money on day one. |
| **[localization.md](localization.md)** | Add or fix a language. Placeholder rules, what must *not* be translated, and the parity tests. |
| **[theming.md](theming.md)** | Add a theme that passes the contrast and distinctness tests, and what makes a theme more than a recoloured default. |

## Security

| Document | What's inside |
|---|---|
| **[../SECURITY.md](../SECURITY.md)** | Reporting a vulnerability, scope, response times, supported versions. |
| **[security-model.md](security-model.md)** | Threat model, cryptographic choices, and an explicit list of what Umbrella does **not** protect you from. |
| **[BUILD_VERIFY.md](BUILD_VERIFY.md)** | Verifying a published build against source. |
| **[TOR.md](TOR.md)** | The bundled Tor client: ports, bootstrap, the kill-switch. |

## Using it

| Document | What's inside |
|---|---|
| **[troubleshooting.md](troubleshooting.md)** | Slow balances, stuck Monero, spam tokens, failed sends, blank icons — users first, developers second. |
| **[../CHANGELOG.md](../CHANGELOG.md)** | Every release. |

## Reference and history

| Document | What's inside |
|---|---|
| [04-desktop.md](04-desktop.md) | Older, more granular desktop notes: project structure, vault, packaging. |
| [07-financial.md](07-financial.md) | Notes on the financial flow. |
| [SECURE_ANON_ROADMAP.md](SECURE_ANON_ROADMAP.md) | The privacy/anonymity direction. |
| [CLAUDE_IMPLEMENTATION_ROADMAP_UK.md](CLAUDE_IMPLEMENTATION_ROADMAP_UK.md) | The long-form implementation roadmap (Ukrainian). |
| [telegram-news-uk.md](telegram-news-uk.md) | Release announcements (Ukrainian). |
| [archive/](archive/) | Documents for the discontinued web product. Kept for history; **not** current. |

---

## The short version

If you read nothing else before changing code:

1. **`Core` never touches the network.** That is what makes every piece of money logic testable
   offline against known vectors.
2. **Every human-typed amount goes through `AmountInput`.** `"0,5"` parsed naively is `5`.
3. **Every outbound request goes through `PublicHttp`.** A stray `HttpClient` bypasses Tor and the
   kill-switch.
4. **Never log a seed, a key, or a raw signed transaction.** No debug flag, no exceptions.
5. **A network error is "unknown", never "empty".** Showing zero because a request failed tells
   somebody their coins are gone.
6. **`CanSend: false` is an honest state.** Ship receive-only rather than a send path you have not
   tested with real money.
