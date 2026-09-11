# Forking Umbrella

For engineers building on top of this. What you can change, what you should not, and the parts that
will silently hurt your users if you get them wrong.

## Read this first: the licence is not MIT

Umbrella is **free to use** but **not** a permissive open-source licence. See [LICENSE](../LICENSE)
for the binding text. In practice:

- ✅ Read it, audit it, build it, run it, learn from it.
- ✅ Fork it privately to fix something, and send the fix back as a PR.
- ⚠️ Redistributing a modified build under your own branding is **restricted**. Ask first —
  [t.me/UmbrellaWallet](https://t.me/UmbrellaWallet).

The reason is not possessiveness. A wallet is a trust object: a fork that keeps the Umbrella name and
look while quietly changing the fee address or the RNG is indistinguishable from the original to the
person losing money. If you want to ship a derivative, talk to us and we will almost certainly say yes
with a name change.

## Rebranding checklist

If you have permission to ship a derivative, change **all** of these. A half-rebrand is worse than
none, because it produces a binary that looks official and is not.

| What | Where |
|---|---|
| App name, publisher | `desktop/src/Umbrella.Wallet.App/Umbrella.Wallet.App.csproj` (`Product`, `AssemblyTitle`) |
| Installer identity | `desktop/installer/umbrella.iss` — `AppName`, `AppPublisher`, **and a new `AppId` GUID** |
| Icon | `Assets/umbrella.ico` + `Assets/umbrella-appicon.png` |
| Logos | `Assets/umbrella-*.png`, `Assets/thefear-logo.png` |
| In-app brand strings | `Localization.cs` — search for `UMBRELLA`, `the fear` |
| Telegram / support links | `Localization.cs`, `MainViewModel.ChannelUrl` |
| **Fee recipient** | `Infrastructure/DeveloperFeeConfig.cs` — see below |

> **Change the `AppId` GUID.** If you don't, your installer will upgrade-over and uninstall the real
> Umbrella on a user's machine.

## The fee is off — leave it off or be loud about it

`DeveloperFeeConfig.BakedBps` is **0**. Umbrella takes no cut of any send.

If your fork turns it back on, the honest thing is to say so on your download page in the same size
text as everything else. The machinery still discloses the percentage in the send review before the
user confirms, and there is a hard 2% ceiling that a bug cannot exceed — do not remove either.

`NoPlatformFeeTests` will fail the moment you set a non-zero rate. That is intentional: it is a
tripwire, not an obstacle. Update it deliberately, in a commit that says what you did.

## Things that will hurt your users if you change them

These are not style preferences. Each one is a real failure mode.

### 1. `AmountInput`

`decimal.TryParse("0,5", …, InvariantCulture)` returns **5**. A user in a comma-decimal locale typing
"0,5 BTC" sends ten times what they meant.

Every human-typed amount goes through `AmountInput.TryParsePositive`. If you add an amount field, use
it. There is a test; do not weaken it.

### 2. `PublicHttp`

Every outbound request goes through one Tor-aware client. A stray `new HttpClient()` anywhere else
bypasses Tor and the kill-switch — that is a privacy hole, not a refactor.

### 3. The kill-switch must fail closed

When Tor-only is on and Tor is unavailable, the request must **not happen**. "Fall back to direct" is
the single worst thing you can do here: the user believes they are on Tor.

### 4. Never log secrets

No seed, no private key, no raw signed transaction — not behind a debug flag, not in a crash dump, not
in a "temporary" `Console.WriteLine`. Log the decision, not the material.

### 5. Partial scans must not lower a balance

If a network scan comes back incomplete, the wallet keeps the previous, higher balance and refuses to
spend rather than quietly showing less money than you have. Inverting this produces a wallet that
occasionally tells people their coins vanished.

### 6. Fee floors

`FeeLevels.Adjust` clamps every tier inside the chain's safe band. Below the relay floor a transaction
is accepted by your code and rejected by the network, and the user watches it sit unconfirmed forever.

### 7. Capture protection

Seed and Monero key screens set `WDA_EXCLUDEFROMCAPTURE`. Removing it means the next screen-share
leaks somebody's wallet.

## Adding things

| I want to… | Read |
|---|---|
| add a coin | [adding-a-chain.md](adding-a-chain.md) |
| add a language | [localization.md](localization.md) |
| add a theme | [theming.md](theming.md) |
| understand the layering | [architecture.md](architecture.md) |
| package a build | [building.md](building.md) |

## House rules the tests enforce

You will hit these. They are all there because the alternative already happened once:

- **No hardcoded user-facing strings.** Views, the send/backup flows and the status line are scanned.
  Put it in `Localization.cs`.
- **Language parity.** Every language defines every key English does. A missing key silently falls
  back to English and nobody notices for months.
- **Placeholder parity.** A translation that drops `{0}` renders a sentence with a hole in it.
- **Theme contrast.** Body text, muted text and button labels must clear WCAG AA in every theme.
- **Guide parity.** English and Ukrainian guides must have the same number of sections.
- **Compiled bindings.** A XAML binding typo is a build error.

## Pull requests

Welcome. See [CONTRIBUTING.md](../CONTRIBUTING.md).

Anything touching the send path, the vault, or derivation needs tests. Not bureaucracy — those are the
paths where a bug costs somebody their money, and a test is the only way a reviewer can believe you.

Tests should be offline and deterministic. If yours needs a live explorer, name it `LiveExplorer…` so
it stays out of the default run.
