# Building Umbrella

Everything from "clone it" to "produce the same installer we publish".

## Requirements

| | |
|---|---|
| **.NET 8 SDK** | the only hard requirement — [download](https://dotnet.microsoft.com/download/dotnet/8.0) |
| **Inno Setup 6** | Windows installer only. [jrsoftware.org](https://jrsoftware.org/isdl.php) |
| Disk | ~2 GB for the SDK, NuGet cache and a self-contained publish |

No Node, no Python, no Docker. The UI is Avalonia, not a web view.

## Clone, build, run

```bash
git clone https://github.com/kiurakku/UmbrellaWallet.git
cd UmbrellaWallet
dotnet build desktop/Umbrella.Wallet.sln -c Release
```

```bash
dotnet run --project desktop/src/Umbrella.Wallet.App
```

A clean build should end with **0 warnings, 0 errors**. Warnings are not tolerated here — XAML
bindings are compiled (`x:CompileBindings`), so binding mistakes are build errors rather than blank
labels you discover later.

## Tests

```bash
dotnet test desktop/Umbrella.Wallet.sln -c Release --filter 'FullyQualifiedName!~LiveExplorer'
```

The filter excludes the few tests that hit real block explorers. Everything else — 526 tests — is
fully offline and deterministic, so this works on a plane.

To run the live ones as well (needs network, and they can fail for reasons that are not your fault):

```bash
dotnet test desktop/Umbrella.Wallet.sln -c Release
```

See [testing.md](testing.md) for what the suite actually covers.

## Bundled binaries: Tor and Monero

The wallet ships a Tor client and `monero-wallet-rpc` inside the build. They are **not** in git — they
are fetched and hash-verified by scripts:

```powershell
desktop/scripts/fetch-tor.ps1
desktop/scripts/fetch-monero.ps1
```

Both are idempotent: if the binary is already staged, they skip the download. Both verify a pinned
SHA-256 against the official release before staging anything.

> **Antivirus note.** `monero-wallet-rpc` is a well-known false positive — several engines flag Monero
> tooling as a miner. If Windows Defender blocks the copy, the fetch script will fail *after* the hash
> check has already passed. Allowing it is your decision; the build will otherwise produce a wallet
> without Monero balance/send support and everything else intact.

## Producing a release

One command does the lot on Windows:

```powershell
pwsh desktop/scripts/release-windows.ps1
```

It produces, under `D:\umbrella-dist` by default:

| Output | What it is |
|---|---|
| `app\` | folder publish — what the installer packages |
| `portable\Umbrella.exe` | single-file, self-contained, no install |
| `UmbrellaWallet-Setup-<version>.exe` | the Inno Setup installer |

Override the output root or the compiler path if your machine differs:

```powershell
pwsh desktop/scripts/release-windows.ps1 -OutRoot C:\dist -Iscc "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

To skip the installer step entirely:

```powershell
pwsh desktop/scripts/release-windows.ps1 -SkipInstaller
```

### The version comes from one place

`desktop/src/Umbrella.Wallet.App/Umbrella.Wallet.App.csproj` → `<Version>`.

The release script reads it and passes it to the installer compiler. The `.iss` no longer carries its
own copy — it used to, and it drifted: a 4.6.0 build once shipped as `Setup-4.5.0.exe`. If you compile
the `.iss` directly, it reads the version out of the published exe instead, so it still cannot name
itself after a stale literal.

### Doing it by hand

If you'd rather run the steps yourself:

```bash
# folder publish (what the installer packages)
dotnet publish desktop/src/Umbrella.Wallet.App/Umbrella.Wallet.App.csproj \
  -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o dist/app

# portable single file
dotnet publish desktop/src/Umbrella.Wallet.App/Umbrella.Wallet.App.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/portable
```

Then rename the apphost in **both** output folders:

```
Umbrella.Wallet.App.exe  →  Umbrella.exe
```

This is safe and it is what the release script does. The assembly stays `Umbrella.Wallet.App` so
`avares://` resource URIs keep resolving; the apphost loads its dll by the **embedded** name, not by
its own filename.

> Skipping the rename is how a stale `Umbrella.exe` from a previous version survives a "deploy" that
> only copied DLLs. If the app reports an old version after you built a new one, check this first.

Finally, compile the installer:

```powershell
& "$env:LocalAppData\Programs\Inno Setup 6\ISCC.exe" /DAppVersion=4.6.0 desktop\installer\umbrella.iss
```

## Linux

```bash
dotnet publish desktop/src/Umbrella.Wallet.App/Umbrella.Wallet.App.csproj \
  -c Release -r linux-x64 --self-contained true -o dist/linux
tar -czf UmbrellaWallet-4.6.0-linux-x64.tar.gz -C dist/linux .
```

## Checksums

Publish a `SHA256SUMS-<version>.txt` next to the artifacts so people can verify what they downloaded:

```powershell
Get-FileHash .\UmbrellaWallet-Setup-4.6.0.exe -Algorithm SHA256
```

```bash
sha256sum UmbrellaWallet-Setup-4.6.0.exe UmbrellaWallet-4.6.0-win-x64-portable.exe \
  > SHA256SUMS-4.6.0.txt
```

Generate them from the **final** files, after every rename and replacement. A sums file assembled
mid-process can mix an old hash with a new artifact, which is worse than having none.

## Reproducing a published build

Reproducible builds are on the roadmap, not done. Today you can get close:

1. Check out the tag for the release (`git checkout v4.6.0`).
2. Build with the same SDK feature band.
3. Compare the *payload* — DLLs, assets — rather than the installer, which embeds a timestamp.

If you find a discrepancy you cannot explain, please
[report it privately](https://github.com/kiurakku/UmbrellaWallet/security/advisories/new) rather than
opening a public issue. See [BUILD_VERIFY.md](BUILD_VERIFY.md) for the current verification notes.

## Common build problems

| Symptom | Cause |
|---|---|
| `MSB3021: cannot copy … used by another process` | The app (or a previous publish) is still running. Close it. |
| `error CS1061` after editing a record | Positional record parameters changed; update the call sites. |
| XAML binding error at build time | Correct — `x:CompileBindings` is on. Fix the binding. |
| `monero-wallet-rpc` copy fails | Antivirus false positive. See the note above. |
| Localization parity test fails | You added a key to one language only. Add it everywhere. |
| Theme contrast test fails | A palette's text dropped below WCAG AA. See [theming.md](theming.md). |
