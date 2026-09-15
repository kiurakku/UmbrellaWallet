# Umbrella Wallet — documentation

Umbrella is a **desktop-only**, self-custody crypto wallet (.NET 8 + Avalonia; Windows and Linux,
Android planned). There is **no web app and no backend server** — nothing to sign in to and nothing
that knows you exist.

**Start here:** **[INDEX.md](INDEX.md)** — full documentation map.

Product overview: [README.md](../README.md) · Backlog: [ROADMAP.md](ROADMAP.md) · Legal: [LEGAL/](../LEGAL/README.md)

---

## For engineers

| Document | What's inside |
|---|---|
| [../MANIFESTO.md](../MANIFESTO.md) | **Read first.** The rules everything below is held to |
| [../THREAT_MODEL.md](../THREAT_MODEL.md) | Attack vectors, and where each defence ends |
| [../PRIVACY.md](../PRIVACY.md) | What leaves this machine and what never does |
| **[architecture.md](architecture.md)** | Layers, data flow, why `Core` never touches the network |
| **[building.md](building.md)** | Build, run, test, package |
| **[testing.md](testing.md)** | What the offline suite protects |
| **[forking.md](forking.md)** | Licence limits and rebrand checklist |

## Extending it

| Document | What's inside |
|---|---|
| **[12-coins-and-chains.md](12-coins-and-chains.md)** | Every chain: paths, fees, privacy, status |
| **[adding-a-chain.md](adding-a-chain.md)** | End-to-end procedure with safety gates |
| **[localization.md](localization.md)** | Languages and parity tests |
| **[theming.md](theming.md)** | Themes and contrast rules |

## Security

| Document | What's inside |
|---|---|
| **[../SECURITY.md](../SECURITY.md)** | Reporting, scope, response times |
| **[security-model.md](security-model.md)** | Crypto choices and explicit non-goals |
| **[BUILD_VERIFY.md](BUILD_VERIFY.md)** | Verify a published build against source |
| **[TOR.md](TOR.md)** | Bundled Tor, kill-switch, circuits |

## Using it

| Document | What's inside |
|---|---|
| **[getting-started.md](getting-started.md)** | Install, verify, first wallet |
| **[troubleshooting.md](troubleshooting.md)** | Common failures |
| **[../CHANGELOG.md](../CHANGELOG.md)** | Every release |

## Reference and history

| Document | What's inside |
|---|---|
| **[ROADMAP.md](ROADMAP.md)** | Single backlog — coins, security, UX, store/legal |
| **[INDEX.md](INDEX.md)** | Central documentation hub |
| [04-desktop.md](04-desktop.md) | Older granular desktop notes |
| [07-financial.md](07-financial.md) | Financial flow notes |
| [SECURE_ANON_ROADMAP.md](SECURE_ANON_ROADMAP.md) | Privacy/anonymity pillars |
| [CLAUDE_IMPLEMENTATION_ROADMAP_UK.md](CLAUDE_IMPLEMENTATION_ROADMAP_UK.md) | Long-form implementation phases (**Ukrainian**, historical) |
| [telegram-news-uk.md](telegram-news-uk.md) | Release announcements (**Ukrainian**) |
| [archive/](archive/) | Discontinued web product — **not** current |

---

## Engineering invariants

1. **`Core` never touches the network.**  
2. **Every human-typed amount goes through `AmountInput`.**  
3. **Every outbound request goes through `PublicHttp`.**  
4. **Never log a seed, key, or raw signed transaction.**  
5. **A network error is "unknown", never "empty".**  
6. **`CanSend: false` is an honest state.**

---

📖 Back to [INDEX.md](INDEX.md)
