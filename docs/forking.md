# Forking Umbrella

Umbrella’s **code** is under the [MIT License](../LICENSE). Forks and derivatives are allowed.
The **name and logos** are not a free-for-all — see [TRADEMARK_POLICY.md](../TRADEMARK_POLICY.md).

Why we opened the licence: [LICENSE_CHANGE.md](../LICENSE_CHANGE.md).

## What you can do

| | |
|---|---|
| ✅ | Fork, modify, redistribute under MIT (keep the copyright notice) |
| ✅ | Submit pull requests upstream |
| ✅ | Build and run locally for audit |
| ❌ | Ship a product that looks like official Umbrella (name, icons, splash, store listing) |
| ❌ | Use “Umbrella Wallet”, “the fear”, or “thefear078” branding without written permission |
| ❌ | Quietly change fee recipients, RNG, or Tor behaviour while still looking official |

A wallet is a trust object. A clone that keeps the Umbrella look while changing the money path is how
users lose funds. Permission for branded use almost always requires a **full rebrand** and preserved
honesty docs (threat model / privacy limits).

Third-party components keep **their own** licences — [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).

## Rebranding checklist

If you ship a public derivative, change **all** of these:

| What | Where |
|---|---|
| App name, publisher | `desktop/src/Umbrella.Wallet.App/Umbrella.Wallet.App.csproj` (`Product`, `AssemblyTitle`, `Company`) |
| Installer identity | `desktop/installer/umbrella.iss` — `AppName`, `AppPublisher`, **and a new `AppId` GUID** |
| Icon / logos | `Assets/umbrella*` |
| In-app brand strings | `Localization.cs` — search for `UMBRELLA`, `the fear` |
| Telegram / support links | `Localization.cs`, About URLs |
| **Fee recipient** | `Infrastructure/DeveloperFeeConfig.cs` — leave at 0 or disclose loudly |

> **Change the `AppId` GUID.** If you don't, your installer can upgrade-over and uninstall the real
> Umbrella on a user's machine.

## The fee is off — leave it off or be loud about it

`DeveloperFeeConfig.BakedBps` is **0**. Umbrella takes no cut of any send. If you turn a fee on in a
fork, say so in the UI before the user confirms — silent cuts are how forks become malware.

## Prefer contributing upstream

Security and fund-path fixes help more people if they land in the main repo. See
[CONTRIBUTING.md](../CONTRIBUTING.md) and [WORKFLOW.md](WORKFLOW.md).

---

📖 Back to [Documentation Index](INDEX.md)
