# Third-party notices

Umbrella Wallet includes third-party software under separate licenses. Those licenses remain in force
and are **not** replaced by the Umbrella [LICENSE](LICENSE).

---

## Bundled binaries (pinned + SHA-256 verified)

These executables are **not committed** to the repository. Release/build scripts download a **pinned
version** and verify SHA-256 against the values below before use. Any mismatch fails the build
(fail-closed). See `desktop/scripts/fetch-tor.ps1` and `desktop/scripts/fetch-monero.ps1`.

When bumping a pin, update version **and** hash here and in the fetch script, using the upstream
project’s own signed hashes file — never a random mirror.

### Tor Expert Bundle

| | |
|---|---|
| Component | `tor.exe` + GeoIP databases (Windows x86_64 expert bundle) |
| Version | 15.0.22 |
| Archive | `tor-expert-bundle-windows-x86_64-15.0.22.tar.gz` |
| Source URL | https://dist.torproject.org/torbrowser/15.0.22/tor-expert-bundle-windows-x86_64-15.0.22.tar.gz |
| SHA-256 | `231dad6b9cb401a54c260db7046965ef04e4f72ff071b140d423fb5da281ab1e` |
| Hash source | https://dist.torproject.org/torbrowser/15.0.22/sha256sums-unsigned-build.txt |
| License | BSD 3-Clause (Tor Project upstream `LICENSE`) |

### Monero CLI (`monero-wallet-rpc`)

| | |
|---|---|
| Component | `monero-wallet-rpc.exe` (from the Windows x64 CLI archive) |
| Version | 0.18.5.1 |
| Archive | `monero-win-x64-v0.18.5.1.zip` |
| Source URL | https://downloads.getmonero.org/cli/monero-win-x64-v0.18.5.1.zip |
| SHA-256 | `cf2ae8273977697d9ef2031c7337b781e6e5936578f602444b2990a173a2437d` |
| Hash source | https://www.getmonero.org/downloads/hashes.txt (PGP-signed) |
| License | BSD 3-Clause (Monero project upstream `LICENSE`) |

### Verifying binaries by hand

```powershell
Get-FileHash -Algorithm SHA256 .\tor-expert-bundle-windows-x86_64-15.0.22.tar.gz
Get-FileHash -Algorithm SHA256 .\monero-win-x64-v0.18.5.1.zip
```

For the strongest guarantee, verify the upstream hashes file’s PGP signature before trusting the hash.

---

## .NET / NuGet libraries (representative)

Versions move with Dependabot; this table lists **primary** runtime dependencies and typical license
families. Always confirm the license text shipped with the NuGet package for the exact version in use.

| Component | Typical license | Source |
|-----------|-----------------|--------|
| .NET runtime / BCL | MIT | https://dotnet.microsoft.com |
| Avalonia UI | MIT | https://avaloniaui.net |
| CommunityToolkit.Mvvm | MIT | https://github.com/CommunityToolkit/dotnet |
| NBitcoin / NBitcoin.Altcoins | MIT | https://github.com/MetacoSA/NBitcoin |
| Nethereum | MIT | https://github.com/Nethereum/Nethereum |
| BouncyCastle.Cryptography | MIT | https://www.bouncycastle.org |
| Konscious.Security.Cryptography.Argon2 | MIT | https://github.com/kmaragon/Konscious.Security.Cryptography |
| QRCoder | MIT | https://github.com/codebude/QRCoder |
| xUnit / test SDK (tests only) | Apache-2.0 / MIT | https://xunit.net |

Full transitive graphs appear in each project’s `*.csproj` / restore graph.

---

## Attribution

Some components require attribution in documentation or an About screen. This file and the in-app
About / open-source notices (where present) satisfy that requirement for Umbrella’s distribution.

If a license is missing or incorrect, report via [CONTACT.md](CONTACT.md) (never send seeds).

---

Related: [LICENSE](LICENSE) · [TRADEMARK_POLICY.md](TRADEMARK_POLICY.md) · [LEGAL/README.md](LEGAL/README.md)

---

📖 Back to [Documentation Index](docs/INDEX.md)
