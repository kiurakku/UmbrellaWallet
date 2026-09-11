# Testing

526 offline tests. This page is about what they actually protect, because a test count is not
evidence of anything on its own.

## Running them

```bash
# the default: fully offline, deterministic, works on a plane
dotnet test desktop/Umbrella.Wallet.sln -c Release --filter 'FullyQualifiedName!~LiveExplorer'
```

```bash
# one area
dotnet test desktop/Umbrella.Wallet.sln -c Release --filter 'FullyQualifiedName~Utxo'
```

```bash
# including the handful that hit real explorers
dotnet test desktop/Umbrella.Wallet.sln -c Release
```

Anything named `LiveExplorer…` talks to the network. Those are excluded by default so the standard run
cannot fail because someone else's API had a bad minute.

## What the suite protects

### Money

| Area | What a failure would have meant |
|---|---|
| Derivation vectors | An address the user is told to use that the reference wallet cannot spend |
| `AmountInput` | `"0,5"` read as `5` — ten times the intended amount |
| Spend planning | Spending coins the user excluded, or change to the wrong place |
| Fee levels | A fee below the relay floor — accepted by us, dropped by the network |
| Partial scans | A degraded network read shown as "your balance went down" |
| No platform fee | The no-cut promise reversed silently |

Derivation is pinned **byte-for-byte** to official test vectors and to the reference libraries —
`@ton/ton` for TON, `cardano-serialization-lib` for Cardano. That is what lets the wallet claim an
address is spendable in another wallet.

### Privacy

| Area | What it pins |
|---|---|
| Address scanning | The concurrent window never queries more addresses than a sequential walk would |
| Spam detection | A priced token is never flagged; ordinary token names never trip the heuristics |
| Privacy score | The top grade requires every lever genuinely on |

That first one matters more than it looks. Making the scan faster by probing further ahead would be a
privacy regression, so the test asserts the exact probe count.

### Correctness the user sees

| Area | What it pins |
|---|---|
| Localization parity | No language is missing a key another has |
| Placeholder parity | A translation cannot drop `{0}` |
| Hardcoded strings | Views, the send/backup flows and the status line have no literals |
| Theme contrast | Every theme clears WCAG AA for body, muted and button text |
| Guide parity | English and Ukrainian have the same sections |
| Money formatting | One format per locale across the whole wallet |
| Candle aggregation | Merging candles never invents or loses a price |

## Writing a test here

**Offline and deterministic.** No network, no clock dependence, no ordering assumptions. If it needs a
live explorer, prefix the name with `LiveExplorer`.

**Say what a failure means.** The class doc should explain the bug being prevented, not describe the
method. Compare:

```csharp
// ❌ tells a future reader nothing
/// <summary>Tests the amount parser.</summary>

// ✅ explains why this exists
/// <summary>
/// "0,5" must be a half, never five. A naive invariant parse reads a comma-decimal amount as ten
/// times the intended value, which on a send is somebody's money.
/// </summary>
```

**Weight the negative cases.** For anything that hides or blocks, the dangerous failure is the false
positive. `SpamTokenInspectorTests` has more tests asserting that ordinary tokens are *not* flagged
than that spam is — wrongly hiding someone's money is worse than showing spam.

**Verify a guard actually guards.** After writing a rule-enforcing test, break the rule on purpose and
confirm it fails, then revert. A guard that passes vacuously is worse than none, because it looks like
coverage. Several in this repo were silently inert until probed this way.

## Testing view models

`MainViewModel` is constructible directly:

```csharp
var vm = new MainViewModel(new EncryptedFileSeedVault(Path.Combine(dir, "vault.json")));
```

Use a temp directory per test and delete it in `Dispose`. Never point a test at a real wallet
directory.

Private methods are reached through the public command that calls them:

```csharp
// RefreshHoldings is private; this is the command that triggers it
vm.SetHoldingsSortCommand.Execute("Name");
```

## Testing the UI itself

There is no automated UI test harness. Two things substitute:

1. **Compiled bindings.** `x:CompileBindings` makes a binding typo a build error, so "it builds" means
   every binding resolves.
2. **Manual review against an isolated wallet.** Never the real one — a separate data directory and a
   throwaway password, so no real balance or seed is ever on screen.

## CI

`.github/workflows/`

| Workflow | Gate |
|---|---|
| `ci.yml` | build + the offline suite |
| `codeql.yml` | static analysis |
| `security.yml` | gitleaks, vulnerable-package scan |
| `release.yml` | tagged release artifacts |

`main` requires all of them green plus a review before merge. They are branch-protection rules, not
suggestions.

## Coverage gaps — being honest

- **No automated UI tests.** Layout regressions are caught by eye.
- **No fuzzing** of address parsers or transaction builders.
- **No external audit.** The suite is evidence, not a substitute, and the README says so.
- **Live explorer tests are thin.** They check the shape of a response, not every provider's quirks.

If you want to contribute tests, those four are where they would matter most.
